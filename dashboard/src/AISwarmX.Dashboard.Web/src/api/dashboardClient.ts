import type {
  ConfigDocContentResponse,
  ConfigDocsCatalogResponse,
  ConfigDocsScope,
  DashboardSnapshot,
  RuleDiffResponse
} from '../types/dashboard';

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

export async function fetchRuleDiff(
  tool: string,
  kind: string,
  fileName: string,
  signal?: AbortSignal
): Promise<RuleDiffResponse> {
  const params = new URLSearchParams({ tool, kind, fileName });
  const endpoint = `/api/governance/diff?${params.toString()}`;
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

  return (await response.json()) as RuleDiffResponse;
}

export async function fetchConfigDocsCatalog(
  scope: ConfigDocsScope,
  signal?: AbortSignal
): Promise<ConfigDocsCatalogResponse> {
  const params = new URLSearchParams({ scope });
  const endpoint = `/api/docs?${params.toString()}`;
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

  return (await response.json()) as ConfigDocsCatalogResponse;
}

export async function fetchConfigDocContent(
  scope: ConfigDocsScope,
  relativePath: string,
  signal?: AbortSignal
): Promise<ConfigDocContentResponse> {
  const params = new URLSearchParams({ scope, path: relativePath });
  const endpoint = `/api/docs/content?${params.toString()}`;
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

  return (await response.json()) as ConfigDocContentResponse;
}

export function formatAutomationStatusEn(raw: string | null | undefined): string | null {
  if (!raw || !raw.trim()) {
    return null;
  }

  const trimmed = raw.trim();
  const isPaused = /^PAUSED/i.test(trimmed);
  const isActive = /^ACTIVE/i.test(trimmed);

  const freqMatch = /FREQ=([A-Z]+)/i.exec(trimmed);
  const intervalMatch = /INTERVAL=(\d+)/i.exec(trimmed);

  const freq = freqMatch ? freqMatch[1].toUpperCase() : null;
  const interval = intervalMatch ? Number.parseInt(intervalMatch[1], 10) : 1;

  let cadence: string | null = null;
  if (freq === 'HOURLY') {
    cadence = interval > 1 ? `Every ${interval}h` : 'Hourly';
  } else if (freq === 'DAILY') {
    cadence = interval > 1 ? `Every ${interval}d` : 'Daily';
  } else if (freq === 'WEEKLY') {
    cadence = interval > 1 ? `Every ${interval}w` : 'Weekly';
  } else if (freq) {
    cadence = freq.charAt(0) + freq.slice(1).toLowerCase();
  }

  const prefix = isPaused ? 'Paused' : isActive ? 'Active' : trimmed;
  if (isPaused || isActive) {
    return cadence ? `${prefix} · ${cadence}` : prefix;
  }

  return trimmed;
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
