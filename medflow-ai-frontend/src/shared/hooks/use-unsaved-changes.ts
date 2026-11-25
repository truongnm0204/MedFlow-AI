import { useCallback, useContext, useId, useLayoutEffect, useRef, useState } from 'react';
import { UNSAFE_NavigationContext, useLocation } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import {
  registerNavigationGuard,
  type NavigationActionType,
  type RegisteredGuardHandle,
} from '@/shared/lib/navigation-guard-manager';

export {
  UnsavedChangesModal,
  type UnsavedChangesModalProps,
} from '@/shared/components/form/unsaved-changes-modal';

export interface UseUnsavedChangesOptions {
  enabled?: boolean;
}

export interface UseUnsavedChangesReturn {
  isBlocked: boolean;
  blockedActionType: NavigationActionType | null;
  confirmNavigation: () => void;
  cancelNavigation: () => void;
  allowNavigation: () => void;
  guardAction: (action: () => void) => void;
}

export function useUnsavedChanges(
  isDirty: boolean,
  options: UseUnsavedChangesOptions = {},
): UseUnsavedChangesReturn {
  const { enabled = true } = options;
  const guardId = useId();
  const navigationContext = useContext(UNSAFE_NavigationContext);
  const navigator = navigationContext?.navigator ?? null;
  const location = useLocation();

  const shouldBlock = enabled && isDirty;
  const shouldBlockRef = useRef(shouldBlock);
  const wasAuthenticatedRef = useRef<boolean>(useAuthStore.getState().isAuthenticated);
  const currentAuthenticated = useAuthStore((state) => state.isAuthenticated);

  const [rawIsBlocked, setRawIsBlocked] = useState(false);
  const [rawBlockedActionType, setRawBlockedActionType] = useState<NavigationActionType | null>(
    null,
  );

  const pendingProceedRef = useRef<(() => void) | null>(null);
  const pendingCancelRef = useRef<(() => void) | null>(null);
  const handleRef = useRef<RegisteredGuardHandle | null>(null);

  useLayoutEffect(() => {
    shouldBlockRef.current = shouldBlock;
    if (!shouldBlock && pendingCancelRef.current) {
      const cancel = pendingCancelRef.current;
      pendingProceedRef.current = null;
      pendingCancelRef.current = null;
      cancel();
    }
  }, [shouldBlock]);

  useLayoutEffect(() => {
    if (currentAuthenticated) {
      wasAuthenticatedRef.current = true;
    }
  }, [currentAuthenticated]);

  useLayoutEffect(() => {
    const handle = registerNavigationGuard(navigator, {
      id: guardId,
      shouldBlock: () => shouldBlockRef.current,
      onBlocked: ({ actionType, proceed, cancel }) => {
        pendingProceedRef.current = proceed;
        pendingCancelRef.current = cancel;
        setRawBlockedActionType(actionType);
        setRawIsBlocked(true);
      },
      onResetBlocked: () => {
        pendingProceedRef.current = null;
        pendingCancelRef.current = null;
        setRawBlockedActionType(null);
        setRawIsBlocked(false);
      },
      getSessionSnapshot: () => ({
        wasAuthenticated: wasAuthenticatedRef.current,
        isCurrentlyAuthenticated: useAuthStore.getState().isAuthenticated,
      }),
    });

    handleRef.current = handle;
    return () => {
      handle.unregister();
      handleRef.current = null;
    };
  }, [guardId, navigator]);

  useLayoutEffect(() => {
    handleRef.current?.syncHistoryIndex();
  }, [location.pathname, location.search, location.hash]);

  const confirmNavigation = useCallback(() => {
    const proceed = pendingProceedRef.current;
    pendingProceedRef.current = null;
    pendingCancelRef.current = null;
    setRawBlockedActionType(null);
    setRawIsBlocked(false);
    proceed?.();
  }, []);

  const cancelNavigation = useCallback(() => {
    const cancel = pendingCancelRef.current;
    pendingProceedRef.current = null;
    pendingCancelRef.current = null;
    setRawBlockedActionType(null);
    setRawIsBlocked(false);
    cancel?.();
  }, []);

  const allowNavigation = useCallback(() => {
    handleRef.current?.allowSingleNavigation();
  }, []);

  const guardAction = useCallback((action: () => void) => {
    if (handleRef.current) {
      handleRef.current.guardAction(action);
      return;
    }
    action();
  }, []);

  const isBlocked = shouldBlock && rawIsBlocked;
  const blockedActionType = isBlocked ? rawBlockedActionType : null;

  return {
    isBlocked,
    blockedActionType,
    confirmNavigation,
    cancelNavigation,
    allowNavigation,
    guardAction,
  };
}
