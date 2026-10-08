/**
 * The editable tree is backed by a single client-side cache of elements.
 *
 * Elements are loaded lazily: expanding a node fetches only its children and adds them to the
 * cache, so a tree of thousands of nodes never loads at once. Pending edits, additions and
 * deletions live in this cache until Apply. Because the cache is keyed by id and each element
 * knows its parentId, the hierarchy is reconstructed from whatever is present — so separately
 * loaded elements nest correctly.
 */

export interface CachedElement {
  /** Stable client key: the server id, or a negative temporary id for pending additions. */
  key: number;
  /** Server id, or null while the addition has not been applied. */
  id: number | null;
  /** Parent key within the cache (server id or temporary id). */
  parentId: number | null;
  value: string;
  hasChildren: boolean;
  /** Whether the node's children are currently shown. */
  expanded: boolean;
  /** Server version this element was loaded/refreshed at (null for pending additions). */
  version: number | null;
  pendingAdd: boolean;
  pendingUpdate: boolean;
  pendingDelete: boolean;
  /** The server changed this element while a local edit was pending. */
  conflict: boolean;
}
