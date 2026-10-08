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

  private static readonly StorageKey = 'tree-editor:cache';
  private static readonly StorageVersion = 1;

  private nextTempId = -1;
  private persistHandle: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    const restored = CacheTreeStore.readStorage();
    if (restored && restored.length > 0) {
      this.elements = restored;
      // Keep new temporary ids below any restored ones so they cannot collide.
      this.nextTempId = restored.reduce(
        (min, element) => (element.key < 0 && element.key < min ? element.key : min),
        -1,
      );
    }
  }

  private inform(text: string): void {
    this.message = text;
    this.messageType = 'info';
  }

  private fail(text: string): void {
    this.message = text;
    this.messageType = 'error';
  }

  /**
   * Restores the persisted cache (if any) and then loads the server tree. When a cache was
   * restored we merge (reset = false) so pending local changes are kept; only a cold start with
   * nothing persisted clears the cache first.
   */
  async bootstrap(): Promise<void> {
    await this.loadRoots(this.elements.length === 0);
  }

  private static readStorage(): CachedElement[] | null {
    if (typeof localStorage === 'undefined') {
      return null;
    }

    try {
      const raw = localStorage.getItem(CacheTreeStore.StorageKey);
      if (!raw) {
        return null;
      }

      const parsed = JSON.parse(raw) as { version?: number; elements?: unknown };
      if (parsed?.version !== CacheTreeStore.StorageVersion) {
        return null;
      }

      return CacheTreeStore.normalize(parsed.elements);
    } catch {
      return null;
    }
  }

  /** Fills in defaults so a slightly older/partial payload cannot break the cache. */
  private static normalize(raw: unknown): CachedElement[] | null {
    if (!Array.isArray(raw)) {
      return null;
    }

    const result: CachedElement[] = [];
    for (const item of raw) {
      if (!item || typeof item !== 'object') {
        continue;
      }

      const element = item as Partial<CachedElement>;
      if (typeof element.key !== 'number' || typeof element.value !== 'string') {
        continue;
      }

      result.push({
        key: element.key,
        id: typeof element.id === 'number' ? element.id : null,
        parentId: typeof element.parentId === 'number' ? element.parentId : null,
        value: element.value,
        hasChildren: element.hasChildren === true,
        expanded: element.expanded === true,
        version: typeof element.version === 'number' ? element.version : null,
        pendingAdd: element.pendingAdd === true,
        pendingUpdate: element.pendingUpdate === true,
        pendingDelete: element.pendingDelete === true,
        conflict: element.conflict === true,
        serverValue: typeof element.serverValue === 'string' ? element.serverValue : null,
        serverUpdatedAt: typeof element.serverUpdatedAt === 'string' ? element.serverUpdatedAt : null,
        editedAt: typeof element.editedAt === 'string' ? element.editedAt : null,
      });
    }

    return result.length > 0 ? result : null;
  }

  /** Debounced write so rapid edits do not thrash localStorage. */
  private schedulePersist(): void {
    if (typeof localStorage === 'undefined') {
      return;
    }

    if (this.persistHandle !== null) {
      clearTimeout(this.persistHandle);
    }

    this.persistHandle = setTimeout(() => {
      this.persistHandle = null;
      this.persistNow();
    }, 300);
  }

  private persistNow(): void {
    if (typeof localStorage === 'undefined') {
      return;
    }

    try {
      const snapshot = $state.snapshot(this.elements);
      localStorage.setItem(
        CacheTreeStore.StorageKey,
        JSON.stringify({ version: CacheTreeStore.StorageVersion, elements: snapshot }),
      );
    } catch {
      // Quota exceeded or storage unavailable: persistence is best-effort.
    }
  }

  private clearStorage(): void {
    if (typeof localStorage === 'undefined') {
      return;
    }

    if (this.persistHandle !== null) {
      clearTimeout(this.persistHandle);
      this.persistHandle = null;
    }

    try {
      localStorage.removeItem(CacheTreeStore.StorageKey);
    } catch {
      // ignore
    }
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
      if (!existing.pendingUpdate && !existing.pendingDelete) {
        existing.value = node.value;
        existing.parentId = node.parentId;
        existing.hasChildren = node.hasChildren;
        existing.version = node.version;
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
        serverValue: null,
        serverUpdatedAt: null,
        editedAt: null,
      },
    ];
  }

  loadMany(nodes: ElementNode[]): void {
    for (const node of nodes) {
      this.load(node);
    }
    this.schedulePersist();
  }

  /**
   * Adds a node pushed by another client when it is reachable in this cache: a root, or a child
   * whose parent is already loaded. Unreachable nodes are skipped and picked up if/when their
   * parent is expanded later.
   */
  loadReachable(node: ElementNode): void {
    if (node.parentId !== null && !this.contains(node.parentId)) {
      return;
    }
    this.load(node);
  }

  /** Expands a node, loading its children on first expansion; collapses when already open. */
  async toggle(key: number): Promise<void> {
    const element = this.elements.find((candidate) => candidate.key === key);
    if (!element) {
      return;
    }

    if (element.expanded) {
      element.expanded = false;
      this.schedulePersist();
      return;
    }

    const hasLoadedChildren = this.elements.some(
      (candidate) => candidate.parentId === key && !candidate.pendingAdd,
    );
    if (!hasLoadedChildren && element.id !== null && element.hasChildren) {
      await this.loadChildren(element.id);
    }

    element.expanded = true;
    this.schedulePersist();
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
    element.editedAt = new Date().toISOString();
    if (!element.pendingAdd) {
      element.pendingUpdate = true;
    }
    this.schedulePersist();
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

    parent.expanded = true;
    this.createPending(value, parentKey);
  }

  /** Adds a pending root element (used to create the first element of an empty tree). */
  addRoot(value: string): void {
    this.createPending(value, null);
  }

  private createPending(value: string, parentId: number | null): void {
    const key = this.nextTempId--;
    this.elements = [
      ...this.elements,
      {
        key,
        id: null,
        parentId,
        value,
        hasChildren: false,
        expanded: true,
        version: null,
        pendingAdd: true,
        pendingUpdate: false,
        pendingDelete: false,
        conflict: false,
        serverValue: null,
        serverUpdatedAt: null,
        editedAt: new Date().toISOString(),
      },
    ];
    this.schedulePersist();
  }

  /** Marks an element and its cached descendants as deleted (the server cascades the rest). */
  markDeleted(key: number): void {
    if (!this.elements.some((element) => element.key === key)) {
      return;
    }

    const subtree = new Set<number>([key, ...this.collectDescendants(key)]);
    const now = new Date().toISOString();
    for (const element of this.elements) {
      if (subtree.has(element.key)) {
        element.pendingDelete = true;
        element.editedAt = now;
      }
    }
    this.schedulePersist();
  }

  async apply(): Promise<void> {
    const request: ApplyRequest = {
      updates: this.elements
        .filter((element) => element.pendingUpdate && !element.pendingDelete && element.id !== null)
        .map((element) => ({ id: element.id as number, value: element.value })),
      additions: this.elements
        .filter((element) => element.pendingAdd && !element.pendingDelete)
        .map((element) => ({ tempId: element.key, parentId: element.parentId, value: element.value })),
      deletions: this.deletionRoots(),
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
      this.schedulePersist();
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

  /** Discards all local pending changes and reloads the tree from the server. Does not touch the DB. */
  async reset(): Promise<void> {
    this.busy = true;
    this.resetting = true;
    this.message = null;
    this.clearStorage();
    try {
      await this.loadRoots(true);
      this.inform('Changes discarded. Reloaded from the server.');
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
      element.serverValue = node.value;
      element.serverUpdatedAt = node.updatedAt;
      this.schedulePersist();
      return;
    }

    element.value = node.value;
    element.parentId = node.parentId;
    element.hasChildren = node.hasChildren;
    element.version = node.version;
    element.conflict = false;
    element.serverValue = null;
    element.serverUpdatedAt = null;
    this.schedulePersist();
  }

  /** Keep the local variant and clear the conflict so it can be applied. */
  resolveConflictKeepMine(key: number): void {
    const element = this.elements.find((candidate) => candidate.key === key);
    if (!element) {
      return;
    }

    element.conflict = false;
    element.serverValue = null;
    element.serverUpdatedAt = null;
    this.schedulePersist();
  }

  /** Discard the local edit and take the database variant. */
  resolveConflictUseServer(key: number): void {
    const element = this.elements.find((candidate) => candidate.key === key);
    if (!element || element.serverValue === null) {
      return;
    }

    element.value = element.serverValue;
    element.pendingUpdate = false;
    element.pendingDelete = false;
    element.conflict = false;
    element.serverValue = null;
    element.serverUpdatedAt = null;
    element.editedAt = null;
    this.schedulePersist();
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
      this.schedulePersist();
    }
  }

  /**
   * Only the top-most pending deletions are sent. Descendants that share a pending-deleted
   * ancestor are covered by the server's recursive cascade, so listing them would make the
   * server reject them as already deleted.
   */
  private deletionRoots(): number[] {
    const deletedKeys = new Set(
      this.elements.filter((element) => element.pendingDelete).map((element) => element.key),
    );

    return this.elements
      .filter(
        (element) =>
          element.pendingDelete &&
          element.id !== null &&
          !(element.parentId !== null && deletedKeys.has(element.parentId)),
      )
      .map((element) => element.id as number);
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
