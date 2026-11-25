import type { NavigateOptions, Navigator, To } from 'react-router-dom';

export type NavigationActionType = 'PUSH' | 'REPLACE' | 'POP' | 'ACTION';

export interface BlockedNavigationPayload {
  actionType: NavigationActionType;
  proceed: () => void;
  cancel: () => void;
}

export interface GuardSessionSnapshot {
  wasAuthenticated: boolean;
  isCurrentlyAuthenticated: boolean;
}

export interface NavigationGuardEntry {
  id: string;
  shouldBlock: () => boolean;
  onBlocked: (payload: BlockedNavigationPayload) => void;
  onResetBlocked: () => void;
  getSessionSnapshot?: () => GuardSessionSnapshot;
}

type HistoryLikeNavigator = Navigator & {
  index?: number;
  location?: {
    pathname: string;
    search: string;
    hash: string;
  };
};

interface ActiveBlockState {
  guardId: string;
  cancel: () => void;
}

interface NavigatorController {
  navigator: HistoryLikeNavigator;
  isBrowserHistory: boolean;
  guards: Map<string, NavigationGuardEntry>;
  activeBlock: ActiveBlockState | null;
  singleShotBypass: boolean;
  trackedHistoryIdx: number | null;
  popPhase: 'idle' | 'reverting' | 'proceeding';
  revertResolver: (() => void) | null;
  origPush: HistoryLikeNavigator['push'];
  origReplace: HistoryLikeNavigator['replace'];
  origGo: HistoryLikeNavigator['go'];
  handlePopState: (event: PopStateEvent) => void;
  detach: () => void;
}

const controllersByNavigator = new WeakMap<Navigator, NavigatorController>();
const activeControllers = new Set<NavigatorController>();

let authInvalidatedUntilMs = 0;
let beforeUnloadAttached = false;
let globalPopStateAttached = false;

function handleGlobalPopState(event: PopStateEvent): void {
  if (activeControllers.size === 0) {
    return;
  }
  for (const controller of Array.from(activeControllers)) {
    if (controller.isBrowserHistory) {
      controller.handlePopState(event);
    }
  }
}

/**
 * Installs a single persistent `popstate` interceptor on `window` BEFORE `<BrowserRouter>`
 * mounts. In Blink/Chromium, `PopStateEvent` (`target === window`, `AT_TARGET` phase) invokes
 * listeners in registration order rather than separating `capture: true` from `capture: false`.
 * Keeping this listener registered ahead of `BrowserRouter`'s `history.listen` ensures
 * `event.stopImmediatePropagation()` prevents route unmounting when a dirty form blocks Back/Forward.
 */
export function installGlobalPopStateInterceptor(): void {
  if (typeof window === 'undefined' || globalPopStateAttached) {
    return;
  }
  window.addEventListener('popstate', handleGlobalPopState, { capture: true });
  globalPopStateAttached = true;
}

installGlobalPopStateInterceptor();

function readWindowHistoryIdx(): number | null {
  if (typeof window === 'undefined') return null;
  const rawState: unknown = window.history.state;
  if (rawState && typeof rawState === 'object' && 'idx' in rawState) {
    const idx = (rawState as { idx?: unknown }).idx;
    if (typeof idx === 'number' && Number.isFinite(idx)) {
      return idx;
    }
  }
  return null;
}

function getCurrentHref(nav: HistoryLikeNavigator, isBrowserHistory: boolean): string {
  if (nav.location) {
    return `${nav.location.pathname}${nav.location.search}${nav.location.hash}`;
  }
  if (isBrowserHistory && typeof window !== 'undefined') {
    return `${window.location.pathname}${window.location.search}${window.location.hash}`;
  }
  return '';
}

function resolveTargetHref(nav: HistoryLikeNavigator, to: To): string {
  try {
    return nav.createHref(to);
  } catch {
    if (typeof to === 'string') return to;
    const pathname = to.pathname ?? '';
    const search = to.search ?? '';
    const hash = to.hash ?? '';
    return `${pathname}${search}${hash}`;
  }
}

function isAuthBypassActiveForGuard(guard: NavigationGuardEntry): boolean {
  if (Date.now() < authInvalidatedUntilMs) {
    return true;
  }
  const snapshot = guard.getSessionSnapshot?.();
  if (snapshot && snapshot.wasAuthenticated && !snapshot.isCurrentlyAuthenticated) {
    return true;
  }
  return false;
}

function findTopBlockingGuard(controller: NavigatorController): NavigationGuardEntry | null {
  const ordered = Array.from(controller.guards.values());
  for (let i = ordered.length - 1; i >= 0; i -= 1) {
    const candidate = ordered[i];
    if (isAuthBypassActiveForGuard(candidate)) {
      continue;
    }
    if (candidate.shouldBlock()) {
      return candidate;
    }
  }
  return null;
}

function handleGlobalBeforeUnload(event: BeforeUnloadEvent): void {
  if (Date.now() < authInvalidatedUntilMs) {
    return;
  }

  for (const controller of activeControllers) {
    if (controller.singleShotBypass) {
      continue;
    }
    const blockingGuard = findTopBlockingGuard(controller);
    if (blockingGuard) {
      event.preventDefault();
      event.returnValue = '';
      return;
    }
  }
}

function syncBeforeUnloadListener(): void {
  if (typeof window === 'undefined') return;
  if (activeControllers.size > 0 && !beforeUnloadAttached) {
    window.addEventListener('beforeunload', handleGlobalBeforeUnload);
    beforeUnloadAttached = true;
  } else if (activeControllers.size === 0 && beforeUnloadAttached) {
    window.removeEventListener('beforeunload', handleGlobalBeforeUnload);
    beforeUnloadAttached = false;
  }
}

function armSingleShotBypass(controller: NavigatorController): void {
  controller.singleShotBypass = true;
  queueMicrotask(() => {
    controller.singleShotBypass = false;
  });
}

function consumeSingleShotBypass(controller: NavigatorController): boolean {
  if (!controller.singleShotBypass) {
    return false;
  }
  controller.singleShotBypass = false;
  return true;
}

function createNavigatorController(rawNavigator: Navigator): NavigatorController {
  const nav = rawNavigator as HistoryLikeNavigator;
  const isMemoryNavigator = typeof nav.index === 'number';
  const isBrowserHistory = !isMemoryNavigator && typeof window !== 'undefined';

  const rawPush = nav.push;
  const rawReplace = nav.replace;
  const rawGo = nav.go;
  const origPush: HistoryLikeNavigator['push'] = (to, state, opts) =>
    rawPush.call(nav, to, state, opts);
  const origReplace: HistoryLikeNavigator['replace'] = (to, state, opts) =>
    rawReplace.call(nav, to, state, opts);
  const origGo: HistoryLikeNavigator['go'] = (delta) => rawGo.call(nav, delta);

  const controller: NavigatorController = {
    navigator: nav,
    isBrowserHistory,
    guards: new Map(),
    activeBlock: null,
    singleShotBypass: false,
    trackedHistoryIdx: isBrowserHistory ? readWindowHistoryIdx() : null,
    popPhase: 'idle',
    revertResolver: null,
    origPush,
    origReplace,
    origGo,
    handlePopState: () => {},
    detach: () => {},
  };

  const clearControllerBlock = () => {
    if (!controller.activeBlock) return;
    const prevGuard = controller.guards.get(controller.activeBlock.guardId);
    controller.activeBlock = null;
    prevGuard?.onResetBlocked();
  };

  const interceptTransition = (
    actionType: 'PUSH' | 'REPLACE' | 'POP',
    executeTransition: () => void,
  ) => {
    if (Date.now() < authInvalidatedUntilMs || consumeSingleShotBypass(controller)) {
      executeTransition();
      if (controller.isBrowserHistory && actionType !== 'POP') {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
      return;
    }

    const blockingGuard = findTopBlockingGuard(controller);
    if (!blockingGuard) {
      executeTransition();
      if (controller.isBrowserHistory && actionType !== 'POP') {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
      return;
    }

    if (controller.activeBlock) {
      return;
    }

    const cancel = () => {
      if (controller.activeBlock?.guardId === blockingGuard.id) {
        controller.activeBlock = null;
      }
      blockingGuard.onResetBlocked();
    };

    const proceed = () => {
      if (controller.activeBlock?.guardId === blockingGuard.id) {
        controller.activeBlock = null;
      }
      blockingGuard.onResetBlocked();
      armSingleShotBypass(controller);
      executeTransition();
      if (controller.isBrowserHistory && actionType !== 'POP') {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
    };

    controller.activeBlock = { guardId: blockingGuard.id, cancel };
    blockingGuard.onBlocked({ actionType, proceed, cancel });
  };

  nav.push = (to: To, state?: unknown, opts?: NavigateOptions) => {
    const currentHref = getCurrentHref(nav, isBrowserHistory);
    const nextHref = resolveTargetHref(nav, to);
    if (currentHref && nextHref === currentHref) {
      origPush(to, state, opts);
      if (controller.isBrowserHistory) {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
      return;
    }
    interceptTransition('PUSH', () => {
      origPush(to, state, opts);
    });
  };

  nav.replace = (to: To, state?: unknown, opts?: NavigateOptions) => {
    const currentHref = getCurrentHref(nav, isBrowserHistory);
    const nextHref = resolveTargetHref(nav, to);
    if (currentHref && nextHref === currentHref) {
      origReplace(to, state, opts);
      if (controller.isBrowserHistory) {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
      return;
    }
    interceptTransition('REPLACE', () => {
      origReplace(to, state, opts);
    });
  };

  nav.go = (delta: number) => {
    if (delta === 0) {
      origGo(delta);
      return;
    }
    interceptTransition('POP', () => {
      if (controller.isBrowserHistory) {
        controller.popPhase = 'proceeding';
      }
      origGo(delta);
    });
  };

  const handlePopStateCapture = (event: PopStateEvent) => {
    if (controller.popPhase === 'reverting') {
      const currentIdx = readWindowHistoryIdx();
      event.stopImmediatePropagation();
      if (
        currentIdx !== null &&
        controller.trackedHistoryIdx !== null &&
        currentIdx !== controller.trackedHistoryIdx
      ) {
        window.history.go(controller.trackedHistoryIdx - currentIdx);
        return;
      }
      controller.popPhase = 'idle';
      const resolve = controller.revertResolver;
      controller.revertResolver = null;
      resolve?.();
      return;
    }

    if (controller.popPhase === 'proceeding') {
      controller.popPhase = 'idle';
      controller.trackedHistoryIdx = readWindowHistoryIdx();
      return;
    }

    if (Date.now() < authInvalidatedUntilMs || consumeSingleShotBypass(controller)) {
      controller.trackedHistoryIdx = readWindowHistoryIdx();
      return;
    }

    const blockingGuard = findTopBlockingGuard(controller);
    if (!blockingGuard) {
      controller.trackedHistoryIdx = readWindowHistoryIdx();
      return;
    }

    const nextIdx = readWindowHistoryIdx();
    const lockedIdx = controller.trackedHistoryIdx;

    // Limitation matching @remix-run/router: if a history entry lacks numeric state.idx
    // (e.g. manually mutated outside React Router), delta cannot be inverted reliably.
    if (nextIdx === null || lockedIdx === null || nextIdx === lockedIdx) {
      controller.trackedHistoryIdx = nextIdx;
      return;
    }

    const delta = nextIdx - lockedIdx;
    event.stopImmediatePropagation();

    let revertSettled = false;
    const revertPromise = new Promise<void>((resolve) => {
      controller.revertResolver = () => {
        if (!revertSettled) {
          revertSettled = true;
          resolve();
        }
      };
      // Safety fallback for environments (e.g. jsdom) where history.go may not emit popstate
      window.setTimeout(() => {
        if (!revertSettled) {
          revertSettled = true;
          if (controller.popPhase === 'reverting') {
            controller.popPhase = 'idle';
          }
          controller.revertResolver = null;
          resolve();
        }
      }, 60);
    });

    controller.popPhase = 'reverting';
    window.history.go(-delta);

    if (controller.activeBlock) {
      return;
    }

    const cancel = () => {
      if (controller.activeBlock?.guardId === blockingGuard.id) {
        controller.activeBlock = null;
      }
      blockingGuard.onResetBlocked();
    };

    const proceed = () => {
      if (controller.activeBlock?.guardId === blockingGuard.id) {
        controller.activeBlock = null;
      }
      blockingGuard.onResetBlocked();
      void revertPromise.then(() => {
        controller.popPhase = 'proceeding';
        window.history.go(delta);
      });
    };

    controller.activeBlock = { guardId: blockingGuard.id, cancel };
    blockingGuard.onBlocked({ actionType: 'POP', proceed, cancel });
  };

  controller.handlePopState = handlePopStateCapture;
  if (isBrowserHistory) {
    installGlobalPopStateInterceptor();
  }

  controller.detach = () => {
    clearControllerBlock();
    nav.push = rawPush;
    nav.replace = rawReplace;
    nav.go = rawGo;
    controller.handlePopState = () => {};
    activeControllers.delete(controller);
    controllersByNavigator.delete(rawNavigator);
    syncBeforeUnloadListener();
  };

  controllersByNavigator.set(rawNavigator, controller);
  activeControllers.add(controller);
  syncBeforeUnloadListener();

  return controller;
}

export interface RegisteredGuardHandle {
  allowSingleNavigation: () => void;
  guardAction: (action: () => void) => void;
  syncHistoryIndex: () => void;
  unregister: () => void;
}

export function registerNavigationGuard(
  navigator: Navigator | null,
  entry: NavigationGuardEntry,
): RegisteredGuardHandle {
  if (!navigator) {
    return {
      allowSingleNavigation: () => {},
      guardAction: (action) => action(),
      syncHistoryIndex: () => {},
      unregister: () => {},
    };
  }

  const controller = controllersByNavigator.get(navigator) ?? createNavigatorController(navigator);
  controller.guards.set(entry.id, entry);

  if (controller.isBrowserHistory && controller.trackedHistoryIdx === null) {
    controller.trackedHistoryIdx = readWindowHistoryIdx();
  }

  return {
    allowSingleNavigation: () => {
      armSingleShotBypass(controller);
    },
    guardAction: (action: () => void) => {
      if (
        Date.now() < authInvalidatedUntilMs ||
        isAuthBypassActiveForGuard(entry) ||
        !entry.shouldBlock()
      ) {
        action();
        return;
      }

      if (controller.activeBlock) {
        return;
      }

      const cancel = () => {
        if (controller.activeBlock?.guardId === entry.id) {
          controller.activeBlock = null;
        }
        entry.onResetBlocked();
      };

      const proceed = () => {
        if (controller.activeBlock?.guardId === entry.id) {
          controller.activeBlock = null;
        }
        entry.onResetBlocked();
        // Grant single-shot bypass so if `action()` calls `navigate(...)`,
        // the wrapped navigator will not open a second confirmation modal.
        armSingleShotBypass(controller);
        action();
      };

      controller.activeBlock = { guardId: entry.id, cancel };
      entry.onBlocked({ actionType: 'ACTION', proceed, cancel });
    },
    syncHistoryIndex: () => {
      if (controller.isBrowserHistory && controller.popPhase === 'idle') {
        controller.trackedHistoryIdx = readWindowHistoryIdx();
      }
    },
    unregister: () => {
      if (controller.activeBlock?.guardId === entry.id) {
        controller.activeBlock = null;
        entry.onResetBlocked();
      }
      controller.guards.delete(entry.id);
      if (controller.guards.size === 0) {
        controller.detach();
      }
    },
  };
}

/**
 * Called during logout or 401/403 session invalidation so active dirty forms
 * never block redirecting the user out of protected clinical/admin screens.
 */
export function notifyAuthSessionInvalidated(durationMs = 1_500): void {
  authInvalidatedUntilMs = Date.now() + durationMs;
  for (const controller of activeControllers) {
    if (controller.activeBlock) {
      const guard = controller.guards.get(controller.activeBlock.guardId);
      controller.activeBlock = null;
      guard?.onResetBlocked();
    }
  }
}

export function resetNavigationGuardManagerForTests(): void {
  authInvalidatedUntilMs = 0;
  for (const controller of Array.from(activeControllers)) {
    controller.detach();
  }
}
