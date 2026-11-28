import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection
} from '@microsoft/signalr';
import type { DashboardSnapshot, SwarmBusResponse } from '../types/dashboard';

export type LiveConnectionState = 'live' | 'reconnecting' | 'polling';

export interface SwarmHubSubscriptionHandlers {
  onSwarmBusUpdated: (swarmBus: SwarmBusResponse) => void;
  onSnapshotUpdated: (snapshot: DashboardSnapshot) => void;
  onStateChange?: (state: LiveConnectionState) => void;
}

export interface SwarmHubConnectionLike {
  readonly state: HubConnectionState;
  on(methodName: string, newMethod: (...args: any[]) => any): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export interface SubscribeSwarmHubOptions {
  connectionFactory?: (hubUrl: string) => SwarmHubConnectionLike;
  enableInTestMode?: boolean;
  initialRetryBaseMs?: number;
  maxRetryDelayMs?: number;
}

export function buildSwarmHubConnection(hubUrl: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, { withCredentials: false })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retryContext) =>
        Math.min(30_000, 1000 * Math.pow(2, Math.min(retryContext.previousRetryCount, 5)))
    })
    .configureLogging(LogLevel.Warning)
    .build();
}

export function subscribeSwarmHub(
  handlers: SwarmHubSubscriptionHandlers,
  hubUrl = '/hubs/swarm',
  options?: SubscribeSwarmHubOptions
): () => void {
  if (import.meta.env.MODE === 'test' && !options?.enableInTestMode && !options?.connectionFactory) {
    handlers.onStateChange?.('polling');
    return () => {};
  }

  let disposed = false;
  let initialAttempt = 0;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  const baseDelayMs = options?.initialRetryBaseMs ?? 1000;
  const maxDelayMs = options?.maxRetryDelayMs ?? 30_000;

  const createConnection = options?.connectionFactory ?? buildSwarmHubConnection;
  const connection = createConnection(hubUrl);

  const clearRetryTimer = () => {
    if (retryTimer !== undefined) {
      clearTimeout(retryTimer);
      retryTimer = undefined;
    }
  };

  const scheduleStartRetry = () => {
    if (disposed || retryTimer !== undefined) {
      return;
    }
    const delayMs = Math.min(maxDelayMs, baseDelayMs * Math.pow(2, Math.min(initialAttempt, 5)));
    initialAttempt += 1;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      void attemptStart();
    }, delayMs);
  };

  const attemptStart = async () => {
    if (disposed) {
      return;
    }
    try {
      await connection.start();
      if (!disposed && connection.state === HubConnectionState.Connected) {
        initialAttempt = 0;
        handlers.onStateChange?.('live');
      }
    } catch {
      if (!disposed) {
        handlers.onStateChange?.('polling');
        scheduleStartRetry();
      }
    }
  };

  connection.on('SwarmBusUpdated', (swarmBus: SwarmBusResponse) => {
    if (!disposed) {
      handlers.onSwarmBusUpdated(swarmBus);
    }
  });

  connection.on('SnapshotUpdated', (snapshot: DashboardSnapshot) => {
    if (!disposed) {
      handlers.onSnapshotUpdated(snapshot);
    }
  });

  connection.onreconnecting(() => {
    if (!disposed) {
      handlers.onStateChange?.('reconnecting');
    }
  });

  connection.onreconnected(() => {
    if (!disposed) {
      initialAttempt = 0;
      clearRetryTimer();
      handlers.onStateChange?.('live');
    }
  });

  connection.onclose(() => {
    if (!disposed) {
      handlers.onStateChange?.('polling');
      scheduleStartRetry();
    }
  });

  void attemptStart();

  return () => {
    disposed = true;
    clearRetryTimer();
    void connection.stop().catch(() => {});
  };
}
