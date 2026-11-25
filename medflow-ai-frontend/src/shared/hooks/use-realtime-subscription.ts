import { useCallback, useEffect, useMemo, useRef, useSyncExternalStore } from 'react';

import {
  buildChannelKey,
  getRealtimeConnectionStatus,
  isRealtimeChannelJoined,
  subscribeRealtimeChannel,
  subscribeRealtimeStatus,
  type RealtimeChannel,
  type RealtimeConnectionStatus,
  type RealtimeWireEventName,
} from '@/shared/lib/realtime-client';

export interface UseRealtimeSubscriptionOptions {
  channel: RealtimeChannel | null;
  event: RealtimeWireEventName;
  onEvent: (payload: unknown) => void;
  onReconnected?: () => void;
  enabled?: boolean;
}

export interface UseRealtimeSubscriptionResult {
  connectionState: RealtimeConnectionStatus;
  isConnected: boolean;
  isJoined: boolean;
  isLive: boolean;
}

export function useRealtimeSubscription({
  channel,
  event,
  onEvent,
  onReconnected,
  enabled = true,
}: UseRealtimeSubscriptionOptions): UseRealtimeSubscriptionResult {
  const onEventRef = useRef(onEvent);
  const onReconnectedRef = useRef(onReconnected);

  useEffect(() => {
    onEventRef.current = onEvent;
    onReconnectedRef.current = onReconnected;
  });

  const channelKey = channel ? buildChannelKey(channel) : null;

  const stableChannel = useMemo<RealtimeChannel | null>(() => {
    if (!enabled || !channel || !channelKey) return null;
    return channel;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, channelKey]);

  const getSnapshot = useCallback((): string => {
    const connStatus = getRealtimeConnectionStatus();
    const joined = stableChannel ? isRealtimeChannelJoined(stableChannel) : false;
    return `${connStatus}:${joined ? '1' : '0'}`;
  }, [stableChannel]);

  const snapshot = useSyncExternalStore(subscribeRealtimeStatus, getSnapshot, getSnapshot);
  const [rawState, joinedFlag] = snapshot.split(':') as [RealtimeConnectionStatus, string];

  useEffect(() => {
    if (!stableChannel) return undefined;

    return subscribeRealtimeChannel({
      channel: stableChannel,
      event,
      onEvent: (payload) => onEventRef.current(payload),
      onReconnected: () => onReconnectedRef.current?.(),
    });
  }, [stableChannel, event]);

  const isConnected = rawState === 'connected';
  const isJoined = isConnected && joinedFlag === '1';

  return {
    connectionState: rawState,
    isConnected,
    isJoined,
    isLive: isJoined,
  };
}
