import { HubConnectionState } from '@microsoft/signalr';
import { QueryClient } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { resetRefreshCoordinatorForTests } from '@/shared/lib/axios';
import {
  getRealtimeConnectionStatus,
  isRealtimeChannelJoined,
  resetRealtimeClientForTests,
  resolveRealtimeHubUrl,
  scheduleCoalescedInvalidation,
  setRealtimeConnectionFactoryForTests,
  subscribeRealtimeChannel,
  type HubConnectionLike,
} from '@/shared/lib/realtime-client';

import viteConfigSource from '../../../../vite.config.ts?raw';

class FakeHubConnection implements HubConnectionLike {
  state: HubConnectionState = HubConnectionState.Disconnected;
  readonly invoked: Array<{ method: string; args: unknown[] }> = [];
  readonly handlers = new Map<string, Array<(...args: never[]) => void>>();
  reconnectingCb: ((error?: Error) => void) | null = null;
  reconnectedCb: ((connectionId?: string) => void) | null = null;
  closeCb: ((error?: Error) => void) | null = null;
  startImpl: () => Promise<void> = async () => {
    this.state = HubConnectionState.Connected;
  };
  invokeImpl: (method: string, args: unknown[]) => Promise<unknown> = async (method, args) => ({
    resourceKey: `${method}:${String(args[0] ?? 'hospital')}`,
    expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
  });
  stopCalls = 0;

  async start(): Promise<void> {
    this.state = HubConnectionState.Connecting;
    try {
      await this.startImpl();
    } catch (err) {
      this.state = HubConnectionState.Disconnected;
      throw err;
    }
  }

  async stop(): Promise<void> {
    this.stopCalls += 1;
    this.state = HubConnectionState.Disconnected;
  }

  async invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T> {
    this.invoked.push({ method: methodName, args });
    return (await this.invokeImpl(methodName, args)) as T;
  }

  on(methodName: string, newMethod: (...args: never[]) => void): void {
    const existing = this.handlers.get(methodName) ?? [];
    existing.push(newMethod);
    this.handlers.set(methodName, existing);
  }

  off(methodName: string): void {
    this.handlers.delete(methodName);
  }

  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnectingCb = callback;
  }

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnectedCb = callback;
  }

  onclose(callback: (error?: Error) => void): void {
    this.closeCb = callback;
  }

  emit(eventName: string, payload: unknown): void {
    const list = this.handlers.get(eventName) ?? [];
    for (const fn of list) {
      (fn as (p: unknown) => void)(payload);
    }
  }

  simulateReconnect(): void {
    this.state = HubConnectionState.Reconnecting;
    this.reconnectingCb?.(new Error('network blip'));
    this.state = HubConnectionState.Connected;
    this.reconnectedCb?.('conn-reconnected');
  }
}

function seedAuthenticatedUser(userId = 10, token = 'token-user-10') {
  useAuthStore.getState().setAuth({
    user: {
      id: userId,
      email: `user${userId}@medflow.vn`,
      fullName: `Người dùng ${userId}`,
      roles: ['PATIENT'],
      patientCode: `PAT-${userId}`,
      staffCode: null,
    },
    accessToken: token,
    refreshToken: `refresh-${userId}`,
  });
}

describe('realtime-client & Vite WebSocket proxy (#831)', () => {
  beforeEach(async () => {
    vi.useFakeTimers();
    localStorage.clear();
    useAuthStore.getState().clearAuth();
    resetRefreshCoordinatorForTests();
    await resetRealtimeClientForTests();
  });

  afterEach(async () => {
    await resetRealtimeClientForTests();
    useAuthStore.getState().clearAuth();
    localStorage.clear();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('configures /hubs proxy with ws: true in vite.config.ts and resolves hub URL cleanly', () => {
    expect(viteConfigSource).toMatch(/['"]\/hubs['"]\s*:\s*\{[\s\S]*?ws\s*:\s*true/);
    expect(resolveRealtimeHubUrl('', 'https://api.medflow.vn/api/v1')).toBe(
      'https://api.medflow.vn/hubs/clinical',
    );
    expect(resolveRealtimeHubUrl('', '/api/v1')).toBe('/hubs/clinical');
  });

  it('shares a single HubConnection per login session, reads latest token, rejoins on reconnect, and tears down on logout/account switch', async () => {
    const createdConnections: FakeHubConnection[] = [];
    const capturedTokenFactories: Array<() => Promise<string>> = [];

    setRealtimeConnectionFactoryForTests((_url, tokenFactory) => {
      const fake = new FakeHubConnection();
      createdConnections.push(fake);
      capturedTokenFactories.push(tokenFactory);
      return fake;
    });

    seedAuthenticatedUser(10, 'jwt-initial');

    const paymentEvents: unknown[] = [];
    const reconnectCallbacks = vi.fn();

    const unsub1 = subscribeRealtimeChannel({
      channel: { type: 'visit-payment', visitIdOrCode: 'VIS-100' },
      event: 'VisitPaymentChanged',
      onEvent: (p) => paymentEvents.push(p),
      onReconnected: reconnectCallbacks,
    });
    const unsub2 = subscribeRealtimeChannel({
      channel: { type: 'visit-payment', visitIdOrCode: 'vis-100' },
      event: 'VisitPaymentChanged',
      onEvent: (p) => paymentEvents.push(p),
    });

    await vi.advanceTimersByTimeAsync(10);

    // Only ONE HubConnection and ONE JoinVisitPaymentGroup for duplicate channel subscribers
    expect(createdConnections).toHaveLength(1);
    const conn1 = createdConnections[0]!;
    expect(getRealtimeConnectionStatus()).toBe('connected');
    expect(isRealtimeChannelJoined({ type: 'visit-payment', visitIdOrCode: 'VIS-100' })).toBe(true);
    expect(conn1.invoked.filter((i) => i.method === 'JoinVisitPaymentGroup')).toHaveLength(1);
    expect(conn1.handlers.get('VisitPaymentChanged')).toHaveLength(1);

    // Rotate token in store -> accessTokenFactory always reads latest token (never stale closure)
    useAuthStore.getState().setTokens({ accessToken: 'jwt-rotated', refreshToken: 'refresh-10-b' });
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe('jwt-rotated');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-10-b');
    expect(await capturedTokenFactories[0]!()).toBe('jwt-rotated');

    // Simulate reconnect -> rejoins active group AND triggers catch-up refetch callback
    conn1.simulateReconnect();
    await vi.advanceTimersByTimeAsync(10);
    expect(conn1.invoked.filter((i) => i.method === 'JoinVisitPaymentGroup')).toHaveLength(2);
    expect(reconnectCallbacks).toHaveBeenCalledTimes(1);

    // Server MembershipRevoked marks channel unjoined immediately
    conn1.emit('MembershipRevoked', {
      eventType: 'MembershipRevoked',
      resourceKey: 'JoinVisitPaymentGroup:VIS-100',
      reasonCode: 'FAMILY_LINK_REVOKED',
    });
    expect(isRealtimeChannelJoined({ type: 'visit-payment', visitIdOrCode: 'VIS-100' })).toBe(
      false,
    );

    // Switching from User 10 -> User 20 stops User 10's connection and opens a fresh one for User 20
    seedAuthenticatedUser(20, 'jwt-user-20');
    await vi.advanceTimersByTimeAsync(20);
    expect(conn1.stopCalls).toBeGreaterThanOrEqual(1);
    expect(createdConnections).toHaveLength(2);

    // Logout stops active connection immediately
    const conn2 = createdConnections[1]!;
    useAuthStore.getState().clearAuth();
    await vi.advanceTimersByTimeAsync(20);
    expect(conn2.stopCalls).toBeGreaterThanOrEqual(1);
    expect(getRealtimeConnectionStatus()).toBe('disconnected');

    unsub1();
    unsub2();
  });

  it('retries initial start() failures with exponential backoff while keeping status disconnected for polling fallback', async () => {
    let attempts = 0;
    setRealtimeConnectionFactoryForTests(() => {
      const fake = new FakeHubConnection();
      fake.startImpl = async () => {
        attempts += 1;
        if (attempts < 3) {
          throw new Error('502 Bad Gateway');
        }
        fake.state = HubConnectionState.Connected;
      };
      return fake;
    });

    seedAuthenticatedUser(15, 'jwt-15');
    const unsub = subscribeRealtimeChannel({
      channel: { type: 'appointment-queue', appointmentIdOrCode: 'APT-15' },
      event: 'QueueChanged',
      onEvent: () => {},
    });

    await vi.advanceTimersByTimeAsync(10);
    expect(attempts).toBe(1);
    expect(getRealtimeConnectionStatus()).toBe('disconnected');

    // Backoff 1s -> attempt 2 fails
    await vi.advanceTimersByTimeAsync(1_000);
    expect(attempts).toBe(2);
    expect(getRealtimeConnectionStatus()).toBe('disconnected');

    // Backoff 2s -> attempt 3 succeeds and joins group
    await vi.advanceTimersByTimeAsync(2_000);
    expect(attempts).toBe(3);
    expect(getRealtimeConnectionStatus()).toBe('connected');
    expect(
      isRealtimeChannelJoined({ type: 'appointment-queue', appointmentIdOrCode: 'APT-15' }),
    ).toBe(true);

    unsub();
  });

  it('coalesces burst invalidation signals into a single REST refetch and forbids empty global queryKey', async () => {
    const queryClient = new QueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    expect(() => scheduleCoalescedInvalidation(queryClient, [])).toThrow(
      /non-empty scoped queryKey/,
    );

    for (let i = 0; i < 25; i += 1) {
      scheduleCoalescedInvalidation(queryClient, ['appointments', 'queue', 'APT-99'], {
        coalesceMs: 250,
        maxJitterMs: 0,
      });
    }

    expect(invalidateSpy).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(260);
    expect(invalidateSpy).toHaveBeenCalledTimes(1);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['appointments', 'queue', 'APT-99'] });
  });
});
