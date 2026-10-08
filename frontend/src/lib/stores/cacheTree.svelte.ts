import { api } from '../api';
import type { CachedElement } from '../cache/forest';
import type { ApplyRequest, ElementNode } from '../types';

/**
 * Single source of truth for the editor: a lazily-populated cache of elements that also drives
 * the tree view. Expanding a node fetches its children on demand; edits/additions/deletions stay
 * here until Apply.
 */
export class CacheTreeStore {
  elements = $state<CachedElement[]>([]);
  message = $state<string | null>(null);
  messageType = $state<'info' | 'error'>('info');
  busy = $state(false);
  applying = $state(false);
  resetting = $state(false);
  loadingKeys = $state<number[]>([]);

  private nextTempId = -1;

  private inform(text: string): void {
    this.message = text;
    this.messageType = 'info';
  }

  private fail(text: string): void {
    this.message = text;
    this.messageType = 'error';
  }

  get pending(): { updates: number; additions: number; deletions: number } {
    let updates = 0;
    let additions = 0;
    let deletions = 0;
    for (const element of this.elements) {
      if (element.pendingDelete) {
        deletions++;
      } else if (element.pendingAdd) {
        additions++;
      } else if (element.pendingUpdate) {
        updates++;
      }
    }
    return { updates, additions, deletions };
  }

  get pendingTotal(): number {
    const pending = this.pending;
    return pending.updates + pending.additions + pending.deletions;
  }

  contains(id: number): boolean {
    return this.elements.some((element) => element.key === id);
  }

  isLoading(key: number): boolean {
    return this.loadingKeys.includes(key);
  }

  isExpanded(key: number): boolean {
    return this.elements.find((element) => element.key === key)?.expanded ?? false;
  }

  childrenOf(key: number): CachedElement[] {
    return this.elements.filter((element) => element.parentId === key);
  }

  /** Loads the roots. Pending edits are only cleared when there are none. */
  async loadRoots(reset = false): Promise<void> {
    this.busy = true;
    try {
      const roots = await api.getRoots();
      if (reset) {
        this.elements = [];
      }
      this.loadMany(roots);
    } catch (error) {
      this.fail(`Failed to load the tree: ${(error as Error).message}`);
    } finally {
      this.busy = false;
    }
  }

  /** Adds an element to the cache (idempotent); keeps local edits that are still pending. */
  load(node: ElementNode): void {
    const existing = this.elements.find((element) => element.key === node.id);
    if (existing) {
      existing.version = node.version;
      if (!existing.pendingUpdate && !existing.pendingDelete) {
        existing.value = node.value;
        existing.parentId = node.parentId;
        existing.hasChildren = node.hasChildren;
      }
      return;
    }

    this.elements = [
      ...this.elements,
      {
        key: node.id,
        id: node.id,
        parentId: node.parentId,
        value: node.value,
        hasChildren: node.hasChildren,
        expanded: false,
        version: node.version,
        pendingAdd: false,
        pendingUpdate: false,
        pendingDelete: false,
        conflict: false,
      },
    ];
  }

  loadMany(nodes: ElementNode[]): void {
    for (const node of nodes) {
      this.load(node);
    }
  }

  /** Expands a node, loading its children on first expansion; collapses when already open. */
  async toggle(key: number): Promise<void> {
    const element = this.elements.find((candidate) => candidate.key === key);
    if (!element) {
      return;
    }

    if (element.expanded) {
      element.expanded = false;
      return;
    }

    const hasLoadedChildren = this.elements.some(
      (candidate) => candidate.parentId === key && !candidate.pendingAdd,
    );
    if (!hasLoadedChildren && element.id !== null && element.hasChildren) {
      await this.loadChildren(element.id);
    }

    element.expanded = true;
  }

  private async loadChildren(id: number): Promise<void> {
    if (this.isLoading(id)) {
      return;
    }

    this.loadingKeys = [...this.loadingKeys, id];
    try {
      this.loadMany(await api.getChildren(id));
    } catch (error) {
      this.fail(`Failed to load children: ${(error as Error).message}`);
    } finally {
      this.loadingKeys = this.loadingKeys.filter((key) => key !== id);
    }
  }

  /** Re-fetches the children of an expanded node (used when the server reports structural changes). */
  async reloadChildren(id: number): Promise<void> {
    if (this.isLoading(id)) {
      return;
    }

    this.loadingKeys = [...this.loadingKeys, id];
    try {
      const fresh = await api.getChildren(id);
      const freshIds = new Set(fresh.map((node) => node.id));
      const staleIds = this.elements
        .filter(
          (element) =>
            element.parentId === id &&
            element.id !== null &&
            !element.pendingAdd &&
            !freshIds.has(element.id as number),
        )
        .map((element) => element.id as number);

      if (staleIds.length > 0) {
        this.removeServerIds(staleIds);
      }

      this.loadMany(fresh);
    } catch {
      // best-effort; the next reconcile will retry
    } finally {
      this.loadingKeys = this.loadingKeys.filter((key) => key !== id);
    }
  }

  updateValue(key: number, value: string): void {
    const element = this.elements.find((candidate) => candidate.key === key);
    if (!element || element.pendingDelete) {
      return;
    }
    element.value = value;
    if (!element.pendingAdd) {
      element.pendingUpdate = true;
    }
  }

  /** Adds a pending child and opens the parent so it is visible. */
  async addChild(parentKey: number, value: string): Promise<void> {
    const parent = this.elements.find((candidate) => candidate.key === parentKey);
    if (!parent || parent.pendingDelete) {
      return;
    }

    // Make sure existing server children are loaded before revealing the parent.
    const hasLoadedChildren = this.elements.some(
      (candidate) => candidate.parentId === parentKey && !candidate.pendingAdd,
    );
    if (parent.id !== null && parent.hasChildren && !hasLoadedChildren) {
      await this.loadChildren(parent.id);
    }

    const key = this.nextTempId--;
    this.elements = [
      ...this.elements,
      {
        key,
        id: null,
        parentId: parentKey,
        value,
        hasChildren: false,
        expanded: true,
        version: null,
        pendingAdd: true,
        pendingUpdate: false,
        pendingDelete: false,
        conflict: false,
      },
    ];
    parent.expanded = true;
  }

  /** Marks an element and its cached descendants as deleted (the server cascades the rest). */
  markDeleted(key: number): void {
    if (!this.elements.some((element) => element.key === key)) {
      return;
    }

    const subtree = new Set<number>([key, ...this.collectDescendants(key)]);
    for (const element of this.elements) {
      if (subtree.has(element.key)) {
        element.pendingDelete = true;
      }
    }
  }

  async apply(): Promise<void> {
    const request: ApplyRequest = {
      updates: this.elements
        .filter((element) => element.pendingUpdate && !element.pendingDelete && element.id !== null)
        .map((element) => ({ id: element.id as number, value: element.value })),
      additions: this.elements
        .filter((element) => element.pendingAdd && !element.pendingDelete)
        .map((element) => ({ tempId: element.key, parentId: element.parentId as number, value: element.value })),
      deletions: this.elements
        .filter((element) => element.pendingDelete && element.id !== null)
        .map((element) => element.id as number),
    };

    this.busy = true;
    this.applying = true;
    this.message = null;
    try {
      const result = await api.apply(request);
      this.rebase(result.added);
      this.elements = this.elements.filter((element) => !element.pendingDelete);
      for (const element of this.elements) {
        // Only touched nodes need their version refreshed; untouched ones keep theirs.
        if (element.pendingAdd || element.pendingUpdate) {
          element.version = null;
        }
        element.pendingAdd = false;
        element.pendingUpdate = false;
        element.conflict = false;
      }
      this.inform(
        `Applied ${result.updatedCount} update(s), ${result.addedCount} addition(s), ${result.deletedCount} deletion(s).`,
      );
    } catch (error) {
      this.fail(`Apply failed: ${(error as Error).message}`);
      throw error;
    } finally {
      this.applying = false;
      this.busy = false;
    }
  }

  async reset(): Promise<void> {
    this.busy = true;
    this.resetting = true;
    this.message = null;
    try {
      await api.reset();
      this.elements = [];
      await this.loadRoots();
      this.inform('Database restored to the initial sample data.');
    } catch (error) {
      this.fail(`Reset failed: ${(error as Error).message}`);
      throw error;
    } finally {
      this.resetting = false;
      this.busy = false;
    }
  }

  serverIds(): number[] {
    return this.elements
      .filter((element) => element.id !== null && !element.pendingAdd)
      .map((element) => element.id as number);
  }

  versionOf(id: number): number | null {
    return this.elements.find((element) => element.id === id)?.version ?? null;
  }

  /** Applies a server snapshot; keeps local edits and flags a conflict instead of overwriting. */
  applyServerNode(node: ElementNode): void {
    const element = this.elements.find((candidate) => candidate.key === node.id);
    if (!element) {
      return;
    }

    if (element.pendingUpdate || element.pendingDelete) {
      element.conflict = true;
      element.version = node.version;
      return;
    }

    element.value = node.value;
    element.parentId = node.parentId;
    element.hasChildren = node.hasChildren;
    element.version = node.version;
    element.conflict = false;
  }

  /** Removes elements that were deleted on the server (and their cached descendants). */
  removeServerIds(ids: number[]): void {
    const toRemove = new Set(ids);
    if (toRemove.size === 0) {
      return;
    }

    const doomed = new Set<number>();
    for (const element of this.elements) {
      if (element.id !== null && toRemove.has(element.id)) {
        doomed.add(element.key);
      }
    }

    let grew = true;
    while (grew) {
      grew = false;
      for (const element of this.elements) {
        if (element.parentId !== null && doomed.has(element.parentId) && !doomed.has(element.key)) {
          doomed.add(element.key);
          grew = true;
        }
      }
    }

    if (doomed.size > 0) {
      this.elements = this.elements.filter((element) => !doomed.has(element.key));
    }
  }

  private rebase(added: { tempId: number; id: number }[]): void {
    const mapping = new Map<number, number>();
    for (const item of added) {
      mapping.set(item.tempId, item.id);
    }

    for (const element of this.elements) {
      if (mapping.has(element.key)) {
        element.key = mapping.get(element.key)!;
        element.id = element.key;
      }
    }

    for (const element of this.elements) {
      if (element.parentId !== null && mapping.has(element.parentId)) {
        element.parentId = mapping.get(element.parentId)!;
      }
    }
  }

  private collectDescendants(key: number): number[] {
    const result: number[] = [];
    const stack = [key];
    while (stack.length > 0) {
      const current = stack.pop()!;
      for (const element of this.elements) {
        if (element.parentId === current) {
          result.push(element.key);
          stack.push(element.key);
        }
      }
    }
    return result;
  }
}

export const cacheTree = new CacheTreeStore();
