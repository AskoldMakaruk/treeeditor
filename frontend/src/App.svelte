<script lang="ts">
  import { onMount } from 'svelte';
  import TreeView from './lib/components/TreeView.svelte';
  import { sync } from './lib/stores/sync.svelte';

  onMount(() => {
    void sync.start();
  });
</script>

<main>
  <header>
    <div>
      <h1>Tree Editor</h1>
      <p>Edit the tree directly, then apply everything at once.</p>
    </div>
    <div class="right">
      <span class="status" class:live={sync.connected}>
        <span class="dot"></span>
        {sync.connected ? 'Live' : 'Offline'}
      </span>
      <span class="stack">Svelte · ASP.NET Core · PostgreSQL · Redis</span>
    </div>
  </header>

  <TreeView />
</main>

<style>
  main {
    max-width: 1000px;
    margin: 0 auto;
  }

  header {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: 16px;
    margin-bottom: 20px;
  }

  h1 {
    margin: 0;
    font-size: 1.6rem;
  }

  header p {
    margin: 4px 0 0;
    color: var(--muted);
  }

  .right {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: 6px;
  }

  .status {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    font-size: 0.78rem;
    color: var(--muted);
    border: 1px solid var(--border);
    border-radius: 999px;
    padding: 2px 10px;
  }

  .status .dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--muted);
  }

  .status.live {
    color: var(--green);
    border-color: color-mix(in srgb, var(--green) 50%, transparent);
  }

  .status.live .dot {
    background: var(--green);
    box-shadow: 0 0 0 3px color-mix(in srgb, var(--green) 25%, transparent);
  }

  .stack {
    color: var(--muted);
    font-size: 0.85rem;
    white-space: nowrap;
  }
</style>
