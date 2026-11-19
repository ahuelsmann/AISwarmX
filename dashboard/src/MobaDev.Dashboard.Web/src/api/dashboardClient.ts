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
    throw new Error(`HTTP ${response.status} beim Laden von ${endpoint}`);
  }

  return (await response.json()) as DashboardSnapshot;
}

export function formatRelativeTimeDe(isoUtc: string | null | undefined, nowMs: number): string {
  if (!isoUtc) {
    return 'unbekannt';
  }

  const parsedMs = Date.parse(isoUtc);
  if (Number.isNaN(parsedMs)) {
    return 'unbekannt';
  }

  const diffSeconds = Math.max(0, Math.floor((nowMs - parsedMs) / 1000));
  if (diffSeconds < 5) {
    return 'gerade eben';
  }
  if (diffSeconds < 60) {
    return `vor ${diffSeconds} s`;
  }

  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `vor ${diffMinutes} Min.`;
  }

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `vor ${diffHours} Std.`;
  }

  const diffDays = Math.floor(diffHours / 24);
  return diffDays === 1 ? 'vor 1 Tag' : `vor ${diffDays} Tagen`;
}

export function formatAbsoluteDe(isoUtc: string | null | undefined): string {
  if (!isoUtc) {
    return 'Kein Zeitstempel';
  }

  const date = new Date(isoUtc);
  if (Number.isNaN(date.getTime())) {
    return isoUtc;
  }

  return date.toLocaleString('de-DE', {
    dateStyle: 'medium',
    timeStyle: 'medium'
  });
}
