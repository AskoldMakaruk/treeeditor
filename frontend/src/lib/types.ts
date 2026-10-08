export interface ElementNode {
  id: number;
  value: string;
  parentId: number | null;
  hasChildren: boolean;
  /** Tree revision this node last changed at; compared to detect changes elsewhere. */
  version: number;
  /** When the server last changed this row (ISO string). */
  updatedAt: string;
}

export interface UpdateOperation {
  id: number;
  value: string;
}

export interface AddOperation {
  tempId: number;
  /** Real parent id, a negative temporary id, or null to create a root element. */
  parentId: number | null;
  value: string;
}

export interface ApplyRequest {
  updates: UpdateOperation[];
  additions: AddOperation[];
  deletions: number[];
}

export interface AddedElementResult {
  tempId: number;
  id: number;
}

export interface ApplyResult {
  updatedCount: number;
  addedCount: number;
  deletedCount: number;
  added: AddedElementResult[];
  revision: number;
}

export interface NodeVersion {
  id: number;
  version: number;
}

export interface TreeCheckResult {
  revision: number;
  nodes: NodeVersion[];
  deleted: number[];
}

export interface TreeChangedNotification {
  revision: number;
  changedIds: number[];
  /** True when the whole tree was replaced (Reset), so clients should reload rather than reconcile. */
  reset: boolean;
}
