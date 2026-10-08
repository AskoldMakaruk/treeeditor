import type { ApplyRequest, ApplyResult, ElementNode, TreeCheckResult } from './types';

export const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5080';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = (await response.json()) as { detail?: string; title?: string };
      detail = body.detail ?? body.title ?? detail;
    } catch {
      // body was not JSON
    }
    throw new Error(detail);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  getRoots: () => request<ElementNode[]>('/api/tree/roots'),

  getChildren: (id: number) => request<ElementNode[]>(`/api/tree/${id}/children`),

  getElement: (id: number) => request<ElementNode>(`/api/tree/${id}`),

  apply: (body: ApplyRequest) =>
    request<ApplyResult>('/api/tree/apply', { method: 'POST', body: JSON.stringify(body) }),

  /** Version check: send the ids held locally, get their current versions + removed ids. */
  check: (ids: number[]) =>
    request<TreeCheckResult>('/api/tree/check', { method: 'POST', body: JSON.stringify({ ids }) }),

  getRevision: () => request<{ revision: number }>('/api/tree/revision'),

  reset: () => request<{ status: string; revision: number }>('/api/admin/reset', { method: 'POST' }),
};
