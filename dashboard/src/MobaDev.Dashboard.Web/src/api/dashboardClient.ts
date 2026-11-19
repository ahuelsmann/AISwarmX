import type { DashboardSnapshot } from '../types/dashboard';

export async function fetchSnapshot(
  forceRefresh = false,
  signal?: AbortSignal
): Promise<DashboardSnapshot> {
  const endpoint = forceRefresh ? '/api/snapshot?refresh=true' : '/api/snapshot';
  const response = await fetch(endpoint, {
    method: 'GET',
    headers: {
      Accept: 'application/json'
    },
    signal
  });

  if (!response.ok) {
    throw new Error(`HTTP ${response.status} loading ${endpoint}`);
  }

  return (await response.json()) as DashboardSnapshot;
}

export function formatRelativeTimeEn(isoUtc: string | null | undefined, nowMs: number): string {
  if (!isoUtc) {
    return 'unknown';
  }

  const parsedMs = Date.parse(isoUtc);
  if (Number.isNaN(parsedMs)) {
    return 'unknown';
  }

  const diffSeconds = Math.max(0, Math.floor((nowMs - parsedMs) / 1000));
  if (diffSeconds < 5) {
    return 'just now';
  }
  if (diffSeconds < 60) {
    return `${diffSeconds}s ago`;
  }

  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `${diffMinutes}m ago`;
  }

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours}h ago`;
  }

  const diffDays = Math.floor(diffHours / 24);
  return diffDays === 1 ? '1d ago' : `${diffDays}d ago`;
}

export function formatAbsoluteEn(isoUtc: string | null | undefined): string {
  if (!isoUtc) {
    return 'No timestamp';
  }

  const date = new Date(isoUtc);
  if (Number.isNaN(date.getTime())) {
    return isoUtc;
  }

  return date.toLocaleString('en-US', {
    dateStyle: 'medium',
    timeStyle: 'medium'
  });
}
