<script lang="ts">
  import { onMount } from 'svelte';
  import type { CachedElement } from '../cache/forest';
  import { cacheTree } from '../stores/cacheTree.svelte';
  import { sync } from '../stores/sync.svelte';

  type Row = {
    element: CachedElement;
    depth: number;
    parentMissing: boolean;
    hasChildren: boolean;
  };

  let rows = $derived(buildRows());
  const pending = $derived(cacheTree.pending);

  onMount(() => {
    void cacheTree.loadRoots(true);
  });

  function buildRows(): Row[] {
    const elements = cacheTree.elements;
    const byKey = new Map(elements.map((element) => [element.key, element]));
    const childrenByParent = new Map<number, CachedElement[]>();
    const roots: CachedElement[] = [];

    for (const element of elements) {
      if (element.parentId !== null && byKey.has(element.parentId)) {
        const bucket = childrenByParent.get(element.parentId) ?? [];
        bucket.push(element);
        childrenByParent.set(element.parentId, bucket);
      } else {
        roots.push(element);
      }
    }

    const order = (a: CachedElement, b: CachedElement) =>
      a.value.localeCompare(b.value) || a.key - b.key;

    const result: Row[] = [];
    const walk = (element: CachedElement, depth: number, parentMissing: boolean) => {
      const children = childrenByParent.get(element.key) ?? [];
      result.push({
        element,
        depth,
        parentMissing,
        hasChildren: element.hasChildren || children.length > 0,
      });

      if (!element.expanded) {
        return;
      }

      for (const child of [...children].sort(order)) {
        walk(child, depth + 1, false);
      }
    };

    for (const root of [...roots].sort(order)) {
      walk(root, 0, root.parentId !== null);
    }

    return result;
  }

  async function onApply(): Promise<void> {
    try {
      await cacheTree.apply();
      await sync.reconcile();
    } catch {
      // message already set by the store
    }
  }

  async function onReset(): Promise<void> {
    try {
      await cacheTree.reset();
      await sync.reconcile();
    } catch {
      // message already set by the store
    }
  }
</script>

<section class="panel">
  <header>
    <div>
      <h2>Tree</h2>
      <p class="subtitle">Click a chevron to expand. Edit values, add children and delete — then Apply.</p>
    </div>
    <div class="actions">
      <button class="ghost" onclick={() => sync.reconcile()} disabled={cacheTree.busy}>Refresh</button>
      <button class="danger" class:loading={cacheTree.resetting} onclick={onReset} disabled={cacheTree.busy}>
        {#if cacheTree.resetting}<span class="btn-spinner"></span>{/if}
        {cacheTree.resetting ? 'Resetting…' : 'Reset'}
      </button>
      <button
        class="primary"
        class:loading={cacheTree.applying}
        onclick={onApply}
        disabled={cacheTree.pendingTotal === 0 || cacheTree.busy}
      >
        {#if cacheTree.applying}<span class="btn-spinner"></span>{/if}
        {cacheTree.applying ? 'Applying…' : 'Apply'}
      </button>
      <span class="badge" class:pending={cacheTree.pendingTotal > 0}>
        {cacheTree.pendingTotal} pending
      </span>
    </div>
  </header>

  <div class="counts">
    {pending.updates} edit · {pending.additions} new · {pending.deletions} deleted
  </div>

  {#if cacheTree.message}
    <p class="message" class:error={cacheTree.messageType === 'error'} role="status">
      {cacheTree.message}
    </p>
  {/if}

  <div class="tree">
    {#if rows.length === 0 && cacheTree.busy}
      <p class="muted">Loading…</p>
    {:else if rows.length === 0}
      <p class="muted">No elements.</p>
    {/if}

    {#each rows as row (row.element.key)}
      <div
        class="row"
        class:deleted={row.element.pendingDelete}
        style="--depth: {row.depth}"
      >
        {#if row.depth > 0}
          <span class="guide" aria-hidden="true"></span>
        {/if}

        {#if row.hasChildren}
          <button
            class="chevron"
            class:open={row.element.expanded}
            aria-label={row.element.expanded ? 'Collapse' : 'Expand'}
            onclick={() => void cacheTree.toggle(row.element.key)}
          >
            {#if cacheTree.isLoading(row.element.key)}
              <span class="spinner"></span>
            {:else}
              <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3.5 10.5 8 6 12.5" /></svg>
            {/if}
          </button>
        {:else}
          <span class="chevron spacer" aria-hidden="true"></span>
        {/if}

        {#if row.element.pendingAdd}
          <span class="tag new">new</span>
        {/if}
        {#if row.element.pendingUpdate}
          <span class="tag edit">edit</span>
        {/if}
        {#if row.element.pendingDelete}
          <span class="tag del">deleted</span>
        {/if}
        {#if row.element.conflict}
          <span class="tag conflict" title="Changed on the server while you had unsaved edits">conflict</span>
        {/if}
        {#if row.parentMissing}
          <span class="tag orphan" title="The parent is not loaded">parent not loaded</span>
        {/if}

        <input
          type="text"
          value={row.element.value}
          disabled={row.element.pendingDelete}
          aria-label="Element value"
          oninput={(event) => cacheTree.updateValue(row.element.key, event.currentTarget.value)}
        />

        <button
          class="ghost"
          title="Add child"
          disabled={row.element.pendingDelete}
          onclick={() => void cacheTree.addChild(row.element.key, 'New element')}
        >
          + child
        </button>
        <button
          class="ghost danger"
          title="Delete element (and descendants)"
          disabled={row.element.pendingDelete}
          onclick={() => cacheTree.markDeleted(row.element.key)}
        >
          delete
        </button>
      </div>
    {/each}
  </div>
</section>

<style>
  .panel {
    background: var(--panel);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 16px;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.25);
  }

  header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 12px;
    flex-wrap: wrap;
  }

  h2 {
    margin: 0;
    font-size: 1rem;
  }

  .subtitle {
    margin: 4px 0 0;
    color: var(--muted);
    font-size: 0.78rem;
  }

  .actions {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .actions button.loading:disabled {
    opacity: 1;
    cursor: progress;
  }

  .badge {
    font-size: 0.78rem;
    color: var(--muted);
    border: 1px solid var(--border);
    border-radius: 999px;
    padding: 2px 10px;
  }

  .badge.pending {
    color: #0b1120;
    background: var(--amber);
    border-color: var(--amber);
    font-weight: 600;
  }

  .counts {
    margin: 8px 0 4px;
    color: var(--muted);
    font-size: 0.8rem;
  }

  .message {
    margin: 8px 0;
    padding: 8px 10px;
    border-radius: 6px;
    background: var(--panel-2);
    border: 1px solid var(--border);
    font-size: 0.85rem;
  }

  .message.error {
    background: color-mix(in srgb, var(--red) 14%, var(--panel-2));
    border-color: color-mix(in srgb, var(--red) 55%, transparent);
    color: #ffb4b4;
  }

  .btn-spinner {
    display: inline-block;
    width: 13px;
    height: 13px;
    margin-right: 7px;
    vertical-align: -2px;
    border: 2px solid color-mix(in srgb, currentColor 30%, transparent);
    border-top-color: currentColor;
    border-radius: 50%;
    animation: spin 0.7s linear infinite;
  }

  .tree {
    display: flex;
    flex-direction: column;
    gap: 1px;
    margin-top: 8px;
    max-height: 72vh;
    overflow: auto;
  }

  .row {
    position: relative;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 6px 3px calc(6px + var(--depth) * 16px);
    border-radius: 7px;
    border: 1px solid transparent;
  }

  .row.deleted {
    opacity: 0.6;
  }

  .row.deleted input[type='text'] {
    text-decoration: line-through;
  }

  .guide {
    position: absolute;
    left: calc(var(--depth) * 16px + 2px);
    top: 0;
    bottom: 0;
    width: 1px;
    background: var(--border);
    opacity: 0.5;
    pointer-events: none;
  }

  .chevron {
    flex: 0 0 auto;
    width: 28px;
    height: 28px;
    padding: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    background: transparent;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.12s ease;
  }

  .chevron.spacer {
    cursor: default;
    pointer-events: none;
  }

  button.chevron:hover {
    background: var(--panel-2);
  }

  .chevron svg {
    width: 18px;
    height: 18px;
    fill: none;
    stroke: var(--muted);
    stroke-width: 2;
    stroke-linecap: round;
    stroke-linejoin: round;
    transition: transform 0.16s ease, stroke 0.16s ease;
  }

  .chevron.open svg {
    transform: rotate(90deg);
    stroke: var(--accent);
  }

  button.chevron:hover svg {
    stroke: var(--accent);
  }

  .spinner {
    width: 12px;
    height: 12px;
    border: 2px solid var(--border);
    border-top-color: var(--accent);
    border-radius: 50%;
    animation: spin 0.7s linear infinite;
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }

  input[type='text'] {
    flex: 1;
    min-width: 0;
  }

  .tag {
    font-size: 0.66rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    border-radius: 4px;
    padding: 1px 6px;
    white-space: nowrap;
  }

  .tag.new {
    background: var(--green);
    color: #06231a;
  }

  .tag.edit {
    background: var(--accent);
    color: #08152c;
  }

  .tag.del {
    background: var(--red);
    color: #2a0b0b;
  }

  .tag.conflict {
    background: var(--red);
    color: #2a0b0b;
  }

  .tag.orphan {
    border: 1px dashed var(--amber);
    color: var(--amber);
  }

  .muted {
    color: var(--muted);
  }
</style>
