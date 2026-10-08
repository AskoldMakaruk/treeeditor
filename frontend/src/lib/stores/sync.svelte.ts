import {
  HubConnectionBuilder,
  type HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { API_BASE, api } from '../api';
import type { TreeChangedNotification } from '../types';
import { cacheTree } from './cacheTree.svelte';

/**
 * Keeps the local cache (which also drives the tree view) in sync with the server.
 *
 * - A SignalR connection pushes "TreeChanged" notifications when any client applies changes.
 * - On every notification, reconnect, or initial load we run a *version check*: we send the ids we
 *   hold and the server replies with their current versions. Only nodes whose version changed are
 *   refetched, and ids the server no longer knows about are removed. This works for a partially
 *   loaded tree because both the check and the refetch are scoped to the ids this client holds.
 * - Expanded nodes whose version changed also have their children reloaded, so structural changes
 *   (a child added or removed elsewhere) appear without a full reload.
 * - Ids named in the notification that this client does not hold yet are fetched and added when
 *   reachable, so a newly created root shows up on every connected client.
 * - If a node changed on the server while a local edit was pending, the local value is kept and
 *   flagged as a conflict instead of being overwritten.
 */
export class SyncStore {
  connected = $state(false);
  revision = $state<number | null>(null);
  error = $state<string | null>(null);

  private connection: HubConnection | null = null;
  private reconciling = false;
  private pendingChangedIds = new Set<number>();
  private visibleIds: number[] = [];

  async start(): Promise<void> {
    if (this.connection) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/tree`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('TreeChanged', (notification: TreeChangedNotification) => {
      if (notification.reset) {
        void this.reloadAfterReset(notification.revision);
        return;
      }

      // The notification names the touched ids. New roots are not in this client's cache, so they
      // would never be discovered by a version check alone; remember them and fetch after the check.
      for (const id of notification.changedIds) {
        this.pendingChangedIds.add(id);
      }
      void this.reconcile();
    });
    connection.onreconnecting(() => {
      this.connected = false;
    });
    connection.onreconnected(() => {
      this.connected = true;
      void this.reconcile();
      void this.pushVisibility();
    });
    connection.onclose(() => {
      this.connected = false;
    });

    this.connection = connection;

    try {
      await connection.start();
      this.connected = true;
      this.error = null;
      await this.reconcile();
      await this.pushVisibility();
    } catch (error) {
      this.connected = false;
      this.error = (error as Error).message;
    }
  }

  /** A Reset replaced the whole tree: drop the cache and reload from the single root. */
  private async reloadAfterReset(revision: number): Promise<void> {
    this.revision = revision;
    this.error = null;
    this.pendingChangedIds.clear();
    try {
      await cacheTree.loadRoots(true);
    } catch (error) {
      this.error = (error as Error).message;
    }
  }

  /**
   * Reports the nodes currently visible in this UI session. The server scopes change events to
   * these ids, so a client is only told about things it can actually see.
   */
  setVisible(ids: number[]): void {
    const unique = [...new Set(ids)].sort((a, b) => a - b);
    if (
      unique.length === this.visibleIds.length &&
      unique.every((id, index) => id === this.visibleIds[index])
    ) {
      return;
    }

    this.visibleIds = unique;
    void this.pushVisibility();
  }

  private async pushVisibility(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('SetVisible', this.visibleIds);
    } catch {
      // Best-effort; visibility is re-sent after the next (re)connect.
    }
  }

  async reconcile(): Promise<void> {
    if (this.reconciling) {
      return;
    }

    this.reconciling = true;
    try {
      const held = cacheTree.serverIds();
      const result = await api.check(held);
      this.revision = result.revision;
      this.error = null;

      const changed = result.nodes
        .filter((node) => cacheTree.versionOf(node.id) !== node.version)
        .map((node) => node.id);

      for (const id of changed) {
        if (!cacheTree.contains(id)) {
          continue;
        }

        try {
          cacheTree.applyServerNode(await api.getElement(id));
        } catch {
          // deleted between the check and the fetch; the next check reports it
        }
      }

      if (result.deleted.length > 0) {
        cacheTree.removeServerIds(result.deleted);
      }

      // Fallback for a missed Reset event: if everything we held is gone, reload from the root.
      if (held.length > 0 && result.nodes.length === 0 && result.deleted.length === held.length) {
        this.pendingChangedIds.clear();
        await cacheTree.loadRoots(true);
        return;
      }

      // Structural changes: reload the children of expanded nodes that changed.
      for (const id of changed) {
        if (cacheTree.isExpanded(id)) {
          await cacheTree.reloadChildren(id);
        }
      }

      await this.applyPushedNodes();
    } catch (error) {
      this.error = (error as Error).message;
    } finally {
      this.reconciling = false;
    }

    // A change announced while we were reconciling (or right after we drained) still needs handling.
    if (this.pendingChangedIds.size > 0) {
      await this.reconcile();
    }
  }

  /**
   * Fetches ids announced by the server that this client does not hold yet — most importantly a
   * newly added root, which no version check against the client's own ids could ever discover.
   */
  private async applyPushedNodes(): Promise<void> {
    while (this.pendingChangedIds.size > 0) {
      const ids = [...this.pendingChangedIds];
      this.pendingChangedIds.clear();

      for (const id of ids) {
        if (cacheTree.contains(id)) {
          continue;
        }

        try {
          cacheTree.loadReachable(await api.getElement(id));
        } catch {
          // Deleted before we could fetch it; the next check reports it.
        }
      }
    }
  }
}

export const sync = new SyncStore();
