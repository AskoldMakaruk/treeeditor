import { HubConnectionBuilder, type HubConnection, LogLevel } from '@microsoft/signalr';
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
 * - If a node changed on the server while a local edit was pending, the local value is kept and
 *   flagged as a conflict instead of being overwritten.
 */
export class SyncStore {
  connected = $state(false);
  revision = $state<number | null>(null);
  error = $state<string | null>(null);

  private connection: HubConnection | null = null;
  private reconciling = false;

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
      if (this.revision === null || notification.revision !== this.revision) {
        void this.reconcile();
      }
    });
    connection.onreconnecting(() => {
      this.connected = false;
    });
    connection.onreconnected(() => {
      this.connected = true;
      void this.reconcile();
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
    } catch (error) {
      this.connected = false;
      this.error = (error as Error).message;
    }
  }

  /** A Reset replaced the whole tree: drop the cache and reload from the single root. */
  private async reloadAfterReset(revision: number): Promise<void> {
    this.revision = revision;
    this.error = null;
    try {
      await cacheTree.loadRoots(true);
    } catch (error) {
      this.error = (error as Error).message;
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
        await cacheTree.loadRoots(true);
        return;
      }

      // Structural changes: reload the children of expanded nodes that changed.
      for (const id of changed) {
        if (cacheTree.isExpanded(id)) {
          await cacheTree.reloadChildren(id);
        }
      }
    } catch (error) {
      this.error = (error as Error).message;
    } finally {
      this.reconciling = false;
    }
  }
}

export const sync = new SyncStore();
