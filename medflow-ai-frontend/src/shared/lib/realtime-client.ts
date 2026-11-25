import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import type { QueryClient, QueryKey } from '@tanstack/react-query';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { API_BASE_URL, getValidAccessTokenForRealtime } from './axios';

export type RealtimeConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export type RealtimeChannel =
  | { readonly type: 'visit-payment'; readonly visitIdOrCode: string }
  | { readonly type: 'appointment-queue'; readonly appointmentIdOrCode: string }
  | { readonly type: 'service-point'; readonly servicePointId: string }
  | { readonly type: 'hospital-queue' };

export type RealtimeWireEventName = 'QueueChanged' | 'VisitPaymentChanged';

export interface QueueChangedWirePayload {
  eventType: 'QueueChanged';
  servicePointId?: number | null;
}

export interface VisitPaymentChangedWirePayload {
  eventType: 'VisitPaymentChanged';
  visitId: number;
}

export interface QueueScopeChangedWirePayload {
  eventType: 'QueueScopeChanged';
  appointmentId: number;
}

export interface MembershipRevokedWirePayload {
  eventType: 'MembershipRevoked';
  resourceKey: string;
  reasonCode: string;
}

export interface RealtimeSubscriptionAck {
  resourceKey: string;
  expiresAtUtc: string;
}

export interface HubConnectionLike {
  readonly state: HubConnectionState;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T>;
  on(methodName: string, newMethod: (...args: never[]) => void): void;
  off(methodName: string, method?: (...args: never[]) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

export type RealtimeConnectionFactory = (
  hubUrl: string,
  accessTokenFactory: () => Promise<string>,
) => HubConnectionLike;

interface ChannelSubscriber {
  readonly id: number;
  readonly event: RealtimeWireEventName;
  onEvent: (payload: unknown) => void;
  onReconnected?: () => void;
}

interface ManagedChannelEntry {
  readonly key: string;
  readonly channel: RealtimeChannel;
  serverResourceKey: string | null;
  joined: boolean;
  joining: boolean;
  revoked: boolean;
  hasConnectedOnce: boolean;
  leaveTimer: ReturnType<typeof setTimeout> | null;
  readonly subscribers: Map<number, ChannelSubscriber>;
}

const RECONNECT_DELAYS_MS = [0, 1_000, 2_000, 5_000, 10_000, 30_000];
const INITIAL_START_RETRY_DELAYS_MS = [1_000, 2_000, 5_000, 10_000, 30_000];
const STRICT_MODE_LEAVE_GRACE_MS = 50;
const LEASE_RENEWAL_INTERVAL_MS = 4 * 60 * 1_000;
const DEFAULT_COALESCE_MS = 250;

const PERMANENT_REJECTION_CODES = [
  '[FORBIDDEN]',
  '[NOT_FOUND]',
  '[CONFLICT]',
  '[VALIDATION_ERROR]',
];
const SESSION_REVOCATION_REASONS = new Set([
  'ACCOUNT_LOCKED',
  'ACCOUNT_INACTIVE',
  'ACCOUNT_REVOKED',
  'STAFF_DEACTIVATED',
  'STAFF_SCOPE_CHANGED',
  'REFRESH_REUSE_DETECTED',
  'LOGOUT',
  'PASSWORD_RESET',
  'PASSWORD_CHANGED',
  'SESSION_REVOKED',
]);

let connection: HubConnectionLike | null = null;
let connectionSessionKey: string | null = null;
let startPromise: Promise<void> | null = null;
let status: RealtimeConnectionStatus = 'disconnected';
let initialRetryAttempt = 0;
let initialRetryTimer: ReturnType<typeof setTimeout> | null = null;
let idleStopTimer: ReturnType<typeof setTimeout> | null = null;
let leaseRenewalTimer: ReturnType<typeof setInterval> | null = null;
let nextSubscriberId = 1;
let customConnectionFactory: RealtimeConnectionFactory | null = null;

const channels = new Map<string, ManagedChannelEntry>();
const stateListeners = new Set<() => void>();
const coalescedInvalidationTimers = new Map<string, ReturnType<typeof setTimeout>>();

/** Chuẩn hóa URL SignalR Hub từ cấu hình môi trường hoặc suy ra từ `API_BASE_URL`. */
export function resolveRealtimeHubUrl(rawHubUrl?: unknown, apiBaseUrl = API_BASE_URL): string {
  if (typeof rawHubUrl === 'string' && rawHubUrl.trim()) {
    return rawHubUrl.trim().replace(/\/+$/, '');
  }

  if (/^https?:\/\//i.test(apiBaseUrl)) {
    try {
      const parsed = new URL(apiBaseUrl);
      return `${parsed.origin}/hubs/clinical`;
    } catch {
      return '/hubs/clinical';
    }
  }

  return '/hubs/clinical';
}

export function buildChannelKey(channel: RealtimeChannel): string {
  switch (channel.type) {
    case 'visit-payment':
      return `visit-payment:${channel.visitIdOrCode.trim().toUpperCase()}`;
    case 'appointment-queue':
      return `appointment-queue:${channel.appointmentIdOrCode.trim().toUpperCase()}`;
    case 'service-point':
      return `service-point:${channel.servicePointId.trim().toUpperCase()}`;
    case 'hospital-queue':
      return 'hospital-queue';
  }
}

function isRealtimeAllowedInCurrentEnv(): boolean {
  if (customConnectionFactory !== null) return true;
  if (import.meta.env.MODE === 'test') return false;
  return typeof window !== 'undefined';
}

function computeActiveSessionKey(): string | null {
  const auth = useAuthStore.getState();
  if (!auth.isAuthenticated || !auth.accessToken) return null;
  const userIdPart = auth.user?.id ?? 'anon';
  return `${userIdPart}:${auth.sessionEpoch}`;
}

function notifyStateListeners(): void {
  for (const listener of stateListeners) {
    listener();
  }
}

function setStatus(next: RealtimeConnectionStatus): void {
  if (status === next) return;
  status = next;
  notifyStateListeners();
}

function clearInitialRetryTimer(): void {
  if (initialRetryTimer !== null) {
    clearTimeout(initialRetryTimer);
    initialRetryTimer = null;
  }
}

function clearIdleStopTimer(): void {
  if (idleStopTimer !== null) {
    clearTimeout(idleStopTimer);
    idleStopTimer = null;
  }
}

function stopLeaseRenewalTimer(): void {
  if (leaseRenewalTimer !== null) {
    clearInterval(leaseRenewalTimer);
    leaseRenewalTimer = null;
  }
}

function startLeaseRenewalTimer(): void {
  stopLeaseRenewalTimer();
  leaseRenewalTimer = setInterval(() => {
    if (status === 'connected' && connection?.state === HubConnectionState.Connected) {
      void syncAllActiveChannels(false);
    }
  }, LEASE_RENEWAL_INTERVAL_MS);
}

function clearAllCoalescedTimers(): void {
  for (const timer of coalescedInvalidationTimers.values()) {
    clearTimeout(timer);
  }
  coalescedInvalidationTimers.clear();
}

function hasAnyActiveSubscribers(): boolean {
  for (const entry of channels.values()) {
    if (entry.subscribers.size > 0) return true;
  }
  return false;
}

function isPermanentHubRejection(err: unknown): boolean {
  const message = err instanceof Error ? err.message : String(err ?? '');
  return PERMANENT_REJECTION_CODES.some((code) => message.includes(code));
}

function createDefaultHubConnection(
  hubUrl: string,
  accessTokenFactory: () => Promise<string>,
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => {
        if (ctx.previousRetryCount >= RECONNECT_DELAYS_MS.length) {
          return RECONNECT_DELAYS_MS[RECONNECT_DELAYS_MS.length - 1] ?? 30_000;
        }
        return RECONNECT_DELAYS_MS[ctx.previousRetryCount] ?? 30_000;
      },
    })
    .configureLogging(LogLevel.None)
    .build();
}

async function invokeChannelJoin(
  conn: HubConnectionLike,
  channel: RealtimeChannel,
): Promise<RealtimeSubscriptionAck | null> {
  switch (channel.type) {
    case 'visit-payment':
      return conn.invoke<RealtimeSubscriptionAck>('JoinVisitPaymentGroup', channel.visitIdOrCode);
    case 'appointment-queue':
      return conn.invoke<RealtimeSubscriptionAck>(
        'JoinAppointmentQueueGroup',
        channel.appointmentIdOrCode,
      );
    case 'service-point':
      return conn.invoke<RealtimeSubscriptionAck>('JoinServicePointGroup', channel.servicePointId);
    case 'hospital-queue':
      return conn.invoke<RealtimeSubscriptionAck>('JoinHospitalQueueGroup');
  }
}

async function invokeChannelLeave(
  conn: HubConnectionLike,
  entry: ManagedChannelEntry,
): Promise<void> {
  if (entry.serverResourceKey) {
    await conn.invoke('LeaveSubscription', entry.serverResourceKey);
    return;
  }

  switch (entry.channel.type) {
    case 'visit-payment':
      await conn.invoke('LeaveVisitPaymentGroup', entry.channel.visitIdOrCode);
      return;
    case 'appointment-queue':
      await conn.invoke('LeaveAppointmentQueueGroup', entry.channel.appointmentIdOrCode);
      return;
    case 'service-point':
      await conn.invoke('LeaveServicePointGroup', entry.channel.servicePointId);
      return;
    case 'hospital-queue':
      await conn.invoke('LeaveHospitalQueueGroup');
      return;
  }
}

async function joinSingleChannel(
  entry: ManagedChannelEntry,
  triggerCatchUpRefetch: boolean,
): Promise<void> {
  if (
    entry.subscribers.size === 0 ||
    entry.revoked ||
    entry.joining ||
    !connection ||
    connection.state !== HubConnectionState.Connected
  ) {
    return;
  }

  entry.joining = true;
  try {
    const ack = await invokeChannelJoin(connection, entry.channel);
    if (entry.subscribers.size === 0 || channels.get(entry.key) !== entry) {
      entry.serverResourceKey = ack?.resourceKey ?? entry.serverResourceKey;
      if (connection && connection.state === HubConnectionState.Connected) {
        void invokeChannelLeave(connection, entry).catch(() => {});
      }
      return;
    }

    const wasConnectedBefore = entry.hasConnectedOnce;
    entry.serverResourceKey = ack?.resourceKey ?? entry.serverResourceKey;
    entry.joined = true;
    entry.hasConnectedOnce = true;
    notifyStateListeners();

    if (triggerCatchUpRefetch || wasConnectedBefore) {
      for (const sub of entry.subscribers.values()) {
        sub.onReconnected?.();
      }
    }
  } catch (err) {
    entry.joined = false;
    if (isPermanentHubRejection(err)) {
      entry.revoked = true;
    }
    notifyStateListeners();
  } finally {
    entry.joining = false;
  }
}

async function syncAllActiveChannels(triggerCatchUpRefetch: boolean): Promise<void> {
  const tasks: Promise<void>[] = [];
  for (const entry of channels.values()) {
    if (entry.subscribers.size > 0 && !entry.revoked) {
      tasks.push(joinSingleChannel(entry, triggerCatchUpRefetch));
    }
  }
  await Promise.allSettled(tasks);
}

function dispatchWireEvent(eventName: RealtimeWireEventName, payload: unknown): void {
  for (const entry of channels.values()) {
    if (!entry.joined || entry.revoked || entry.subscribers.size === 0) continue;

    if (eventName === 'VisitPaymentChanged') {
      if (entry.channel.type !== 'visit-payment') continue;
      const targetVisitId = (payload as VisitPaymentChangedWirePayload | undefined)?.visitId;
      if (
        typeof targetVisitId === 'number' &&
        targetVisitId > 0 &&
        entry.serverResourceKey &&
        /^visit-payment:\d+$/.test(entry.serverResourceKey) &&
        entry.serverResourceKey !== `visit-payment:${targetVisitId}`
      ) {
        continue;
      }
    }
    if (eventName === 'QueueChanged' && entry.channel.type === 'visit-payment') {
      continue;
    }

    for (const sub of entry.subscribers.values()) {
      if (sub.event === eventName) {
        sub.onEvent(payload);
      }
    }
  }
}

function handleQueueScopeChanged(payload: QueueScopeChangedWirePayload | undefined): void {
  const targetAppointmentId = payload?.appointmentId;
  for (const entry of channels.values()) {
    if (entry.channel.type !== 'appointment-queue' || entry.subscribers.size === 0) continue;
    const expectedKey = targetAppointmentId ? `appointment-queue:${targetAppointmentId}` : null;
    if (expectedKey && entry.serverResourceKey && entry.serverResourceKey !== expectedKey) {
      continue;
    }

    entry.revoked = false;
    void joinSingleChannel(entry, true);
  }
}

function handleMembershipRevoked(payload: MembershipRevokedWirePayload | undefined): void {
  if (!payload) return;

  if (SESSION_REVOCATION_REASONS.has(payload.reasonCode)) {
    void teardownConnection(false);
    return;
  }

  const isLeaseExpiry = payload.reasonCode === 'LEASE_EXPIRED';
  for (const entry of channels.values()) {
    if (
      payload.resourceKey === 'session' ||
      (entry.serverResourceKey &&
        entry.serverResourceKey.toLowerCase() === payload.resourceKey.toLowerCase())
    ) {
      entry.joined = false;
      if (isLeaseExpiry && entry.subscribers.size > 0) {
        entry.revoked = false;
        void joinSingleChannel(entry, true);
      } else {
        entry.revoked = true;
      }
    }
  }
  notifyStateListeners();
}

function scheduleInitialStartRetry(expectedSessionKey: string): void {
  clearInitialRetryTimer();
  if (!hasAnyActiveSubscribers() || computeActiveSessionKey() !== expectedSessionKey) {
    return;
  }

  const idx = Math.min(initialRetryAttempt, INITIAL_START_RETRY_DELAYS_MS.length - 1);
  const delayMs = INITIAL_START_RETRY_DELAYS_MS[idx] ?? 30_000;
  initialRetryAttempt += 1;

  initialRetryTimer = setTimeout(() => {
    initialRetryTimer = null;
    if (hasAnyActiveSubscribers() && computeActiveSessionKey() === expectedSessionKey) {
      void ensureConnected();
    }
  }, delayMs);
}

function attachConnectionHandlers(conn: HubConnectionLike, sessionKey: string): void {
  conn.on('QueueChanged', (payload: QueueChangedWirePayload) => {
    dispatchWireEvent('QueueChanged', payload);
  });

  conn.on('VisitPaymentChanged', (payload: VisitPaymentChangedWirePayload) => {
    dispatchWireEvent('VisitPaymentChanged', payload);
  });

  conn.on('QueueScopeChanged', (payload: QueueScopeChangedWirePayload) => {
    handleQueueScopeChanged(payload);
  });

  conn.on('MembershipRevoked', (payload: MembershipRevokedWirePayload) => {
    handleMembershipRevoked(payload);
  });

  conn.onreconnecting(() => {
    if (connection !== conn) return;
    for (const entry of channels.values()) {
      entry.joined = false;
    }
    setStatus('reconnecting');
  });

  conn.onreconnected(() => {
    if (connection !== conn) return;
    initialRetryAttempt = 0;
    setStatus('connected');
    void syncAllActiveChannels(true);
  });

  conn.onclose(() => {
    if (connection !== conn) return;
    connection = null;
    connectionSessionKey = null;
    stopLeaseRenewalTimer();
    for (const entry of channels.values()) {
      entry.joined = false;
    }
    setStatus('disconnected');

    const latestSessionKey = computeActiveSessionKey();
    if (latestSessionKey && latestSessionKey === sessionKey && hasAnyActiveSubscribers()) {
      scheduleInitialStartRetry(latestSessionKey);
    }
  });
}

async function ensureConnected(): Promise<void> {
  if (!isRealtimeAllowedInCurrentEnv()) return;

  const sessionKey = computeActiveSessionKey();
  if (!sessionKey || !hasAnyActiveSubscribers()) {
    return;
  }

  clearIdleStopTimer();

  if (connection && connectionSessionKey && connectionSessionKey !== sessionKey) {
    await teardownConnection(false);
  }

  if (connection && connection.state === HubConnectionState.Connected) {
    setStatus('connected');
    await syncAllActiveChannels(false);
    return;
  }

  if (startPromise) {
    return startPromise;
  }

  clearInitialRetryTimer();
  const hubUrl = resolveRealtimeHubUrl(import.meta.env.VITE_REALTIME_HUB_URL);
  const factory = customConnectionFactory ?? createDefaultHubConnection;
  const conn = factory(hubUrl, getValidAccessTokenForRealtime);

  connection = conn;
  connectionSessionKey = sessionKey;
  attachConnectionHandlers(conn, sessionKey);
  setStatus('connecting');

  startPromise = (async () => {
    try {
      await conn.start();
      if (connection !== conn || computeActiveSessionKey() !== sessionKey) {
        await conn.stop().catch(() => {});
        return;
      }

      initialRetryAttempt = 0;
      const hadPreviouslyConnectedChannel = Array.from(channels.values()).some(
        (c) => c.hasConnectedOnce && c.subscribers.size > 0,
      );
      setStatus('connected');
      startLeaseRenewalTimer();
      await syncAllActiveChannels(hadPreviouslyConnectedChannel);
    } catch {
      if (connection === conn) {
        connection = null;
        connectionSessionKey = null;
        stopLeaseRenewalTimer();
        setStatus('disconnected');
        scheduleInitialStartRetry(sessionKey);
      }
    } finally {
      startPromise = null;
    }
  })();

  return startPromise;
}

async function teardownConnection(clearChannelEntries: boolean): Promise<void> {
  clearInitialRetryTimer();
  clearIdleStopTimer();
  stopLeaseRenewalTimer();
  clearAllCoalescedTimers();
  initialRetryAttempt = 0;

  for (const entry of channels.values()) {
    if (entry.leaveTimer !== null) {
      clearTimeout(entry.leaveTimer);
      entry.leaveTimer = null;
    }
    entry.joined = false;
    entry.joining = false;
    entry.serverResourceKey = null;
    if (clearChannelEntries) {
      entry.revoked = false;
      entry.hasConnectedOnce = false;
    }
  }

  if (clearChannelEntries) {
    channels.clear();
  }

  const activeConn = connection;
  connection = null;
  connectionSessionKey = null;
  startPromise = null;
  setStatus('disconnected');

  if (activeConn) {
    await activeConn.stop().catch(() => {});
  }
}

export function subscribeRealtimeChannel(params: {
  channel: RealtimeChannel;
  event: RealtimeWireEventName;
  onEvent: (payload: unknown) => void;
  onReconnected?: () => void;
}): () => void {
  const key = buildChannelKey(params.channel);
  let entry = channels.get(key);

  if (!entry) {
    entry = {
      key,
      channel: params.channel,
      serverResourceKey: null,
      joined: false,
      joining: false,
      revoked: false,
      hasConnectedOnce: false,
      leaveTimer: null,
      subscribers: new Map(),
    };
    channels.set(key, entry);
  }

  if (entry.leaveTimer !== null) {
    clearTimeout(entry.leaveTimer);
    entry.leaveTimer = null;
  }

  const subId = nextSubscriberId++;
  entry.subscribers.set(subId, {
    id: subId,
    event: params.event,
    onEvent: params.onEvent,
    onReconnected: params.onReconnected,
  });

  if (isRealtimeAllowedInCurrentEnv() && computeActiveSessionKey()) {
    if (connection?.state === HubConnectionState.Connected && !entry.joined && !entry.revoked) {
      void joinSingleChannel(entry, false);
    } else if (!connection) {
      void ensureConnected();
    }
  }

  return () => {
    const current = channels.get(key);
    if (!current) return;

    current.subscribers.delete(subId);
    if (current.subscribers.size > 0) return;

    // Grace window protects React 19 StrictMode unmount/remount from redundant Leave/Join churn.
    current.leaveTimer = setTimeout(() => {
      current.leaveTimer = null;
      if (current.subscribers.size > 0) return;

      channels.delete(key);
      const wasJoined = current.joined;
      current.joined = false;
      notifyStateListeners();

      if (wasJoined && connection?.state === HubConnectionState.Connected) {
        void invokeChannelLeave(connection, current).catch(() => {});
      }

      if (!hasAnyActiveSubscribers()) {
        clearIdleStopTimer();
        idleStopTimer = setTimeout(() => {
          idleStopTimer = null;
          if (!hasAnyActiveSubscribers()) {
            void teardownConnection(false);
          }
        }, STRICT_MODE_LEAVE_GRACE_MS);
      }
    }, STRICT_MODE_LEAVE_GRACE_MS);
  };
}

/** Yêu cầu tham gia lại group khi phòng khám / phạm vi của lịch hẹn thay đổi. */
export function refreshRealtimeChannelScope(channel: RealtimeChannel): void {
  const key = buildChannelKey(channel);
  const entry = channels.get(key);
  if (!entry || entry.subscribers.size === 0) return;

  entry.revoked = false;
  if (connection?.state === HubConnectionState.Connected) {
    void joinSingleChannel(entry, false);
  }
}

export function subscribeRealtimeStatus(listener: () => void): () => void {
  stateListeners.add(listener);
  return () => {
    stateListeners.delete(listener);
  };
}

export function getRealtimeConnectionStatus(): RealtimeConnectionStatus {
  return status;
}

export function isRealtimeChannelJoined(channel: RealtimeChannel | null): boolean {
  if (!channel || status !== 'connected') return false;
  const entry = channels.get(buildChannelKey(channel));
  return Boolean(entry?.joined && !entry.revoked);
}

/**
 * Gộp các tín hiệu cập nhật liên tiếp trên cùng một `queryKey` cụ thể để chống bão REST request.
 * Cấm tuyệt đối gọi với `queryKey` rỗng nhằm bảo vệ cache của các biểu mẫu đang nhập dở (#825).
 */
export function scheduleCoalescedInvalidation(
  queryClient: QueryClient,
  queryKey: QueryKey,
  options?: { coalesceMs?: number; maxJitterMs?: number },
): void {
  if (!Array.isArray(queryKey) || queryKey.length === 0) {
    throw new Error('scheduleCoalescedInvalidation requires a non-empty scoped queryKey.');
  }

  const serializedKey = JSON.stringify(queryKey);
  if (coalescedInvalidationTimers.has(serializedKey)) {
    return;
  }

  const baseMs = Math.max(0, options?.coalesceMs ?? DEFAULT_COALESCE_MS);
  const jitterMs =
    options?.maxJitterMs && options.maxJitterMs > 0
      ? Math.floor(Math.random() * options.maxJitterMs)
      : 0;

  const timer = setTimeout(() => {
    coalescedInvalidationTimers.delete(serializedKey);
    void queryClient.invalidateQueries({ queryKey });
  }, baseMs + jitterMs);

  coalescedInvalidationTimers.set(serializedKey, timer);
}

// Đồng bộ vòng đời kết nối với Zustand auth store (bao gồm cả logout/switch tài khoản đa tab).
let observedSessionKey: string | null = computeActiveSessionKey();

useAuthStore.subscribe(() => {
  const nextSessionKey = computeActiveSessionKey();
  if (nextSessionKey === observedSessionKey) return;

  const previousSessionKey = observedSessionKey;
  observedSessionKey = nextSessionKey;

  if (!nextSessionKey) {
    void teardownConnection(false);
    return;
  }

  if (previousSessionKey && previousSessionKey !== nextSessionKey) {
    void teardownConnection(false).then(() => {
      for (const entry of channels.values()) {
        entry.revoked = false;
        entry.hasConnectedOnce = false;
      }
      if (hasAnyActiveSubscribers()) {
        void ensureConnected();
      }
    });
    return;
  }

  if (hasAnyActiveSubscribers()) {
    void ensureConnected();
  }
});

/** Hook kiểm thử: tiêm factory giả lập `HubConnection` và reset toàn bộ trạng thái module. */
export function setRealtimeConnectionFactoryForTests(
  factory: RealtimeConnectionFactory | null,
): void {
  customConnectionFactory = factory;
}

export async function resetRealtimeClientForTests(): Promise<void> {
  customConnectionFactory = null;
  await teardownConnection(true);
  observedSessionKey = computeActiveSessionKey();
}
