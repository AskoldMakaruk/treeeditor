export interface ElementNode {
  id: number;
  value: string;
  parentId: number | null;
  hasChildren: boolean;
  /** Tree revision this node last changed at; compared to detect changes elsewhere. */
  version: number;
}

export interface UpdateOperation {
  id: number;
  value: string;
}

export interface AddOperation {
  tempId: number;
  parentId: number;
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
}
