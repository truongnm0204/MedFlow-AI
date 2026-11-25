import { useQueryClient } from '@tanstack/react-query';
import { Activity, AlertTriangle, Home, RefreshCw } from 'lucide-react';
import { Component, Fragment, useState, type ErrorInfo, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

export interface AppErrorBoundaryProps {
  children: ReactNode;
  resetKeys?: readonly unknown[];
  onReset?: () => void;
  onNavigateHome?: () => void;
}

interface AppErrorBoundaryState {
  hasError: boolean;
  remountKey: number;
}

function haveResetKeysChanged(
  prevKeys?: readonly unknown[],
  nextKeys?: readonly unknown[],
): boolean {
  if (prevKeys === nextKeys) return false;
  if (!prevKeys || !nextKeys) return Boolean(prevKeys || nextKeys);
  if (prevKeys.length !== nextKeys.length) return true;
  for (let i = 0; i < prevKeys.length; i += 1) {
    if (!Object.is(prevKeys[i], nextKeys[i])) {
      return true;
    }
  }
  return false;
}

/**
 * Provider-independent React Error Boundary.
 * Safe to mount at the very root of the application (outside QueryClientProvider & BrowserRouter)
 * as well as inside route trees. Never exposes raw error messages, stack traces, tokens, or PHI.
 */
export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  override state: AppErrorBoundaryState = {
    hasError: false,
    remountKey: 0,
  };

  static getDerivedStateFromError(): Partial<AppErrorBoundaryState> {
    return { hasError: true };
  }

  override componentDidCatch(error: Error, _errorInfo: ErrorInfo): void {
    // Never log raw error.message, stack trace, or componentStack in production to prevent PHI/token leakage.
    if (!import.meta.env.PROD) {
      const safeErrorKind = error?.name ? String(error.name).slice(0, 64) : 'RenderError';
      console.error('[MedFlow] UI boundary caught render exception:', safeErrorKind);
    }
  }

  override componentDidUpdate(prevProps: AppErrorBoundaryProps): void {
    if (this.state.hasError && haveResetKeysChanged(prevProps.resetKeys, this.props.resetKeys)) {
      this.setState((prev) => ({
        hasError: false,
        remountKey: prev.remountKey + 1,
      }));
    }
  }

  private handleRetry = (): void => {
    this.props.onReset?.();
    this.setState((prev) => ({
      hasError: false,
      remountKey: prev.remountKey + 1,
    }));
  };

  private handleGoHome = (): void => {
    if (this.props.onNavigateHome) {
      this.props.onNavigateHome();
      return;
    }

    this.setState((prev) => ({
      hasError: false,
      remountKey: prev.remountKey + 1,
    }));

    if (typeof window !== 'undefined') {
      window.location.assign('/');
    }
  };

  override render(): ReactNode {
    if (this.state.hasError) {
      return (
        <main
          role="alert"
          aria-live="assertive"
          data-testid="app-error-boundary-fallback"
          className="flex min-h-screen flex-col items-center justify-center bg-canvas px-4 py-12 font-sans text-ink antialiased"
        >
          <div className="w-full max-w-lg rounded-2xl border border-border bg-white p-6 sm:p-8 shadow-sm">
            <div className="flex items-center justify-between border-b border-border pb-4">
              <div className="flex items-center gap-2.5">
                <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-white">
                  <Activity className="h-4 w-4" aria-hidden="true" />
                </span>
                <span className="text-base font-bold tracking-tight text-navy">MedFlow AI</span>
              </div>
              <span className="inline-flex items-center gap-1.5 rounded-full bg-rose-50 px-2.5 py-1 text-xs font-semibold text-danger">
                <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Gián đoạn hiển thị</span>
              </span>
            </div>

            <div className="mt-6 space-y-2.5">
              <h1 className="text-lg font-bold text-navy">
                Đã xảy ra sự cố hiển thị trên màn hình này
              </h1>
              <p className="text-sm leading-relaxed text-sub">
                Hệ thống tạm thời không thể hiển thị nội dung hiện tại. Dữ liệu đã lưu trước đó trên
                hệ thống vẫn được bảo toàn. Vui lòng thử tải lại màn hình hoặc quay về trang chính.
              </p>
            </div>

            <div className="mt-6 flex flex-col gap-2.5 sm:flex-row">
              <Button
                type="button"
                variant="primary"
                size="md"
                onClick={this.handleRetry}
                className="flex-1 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
              >
                <RefreshCw className="h-4 w-4" aria-hidden="true" />
                <span>Thử tải lại</span>
              </Button>
              <Button
                type="button"
                variant="secondary"
                size="md"
                onClick={this.handleGoHome}
                className="flex-1 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
              >
                <Home className="h-4 w-4" aria-hidden="true" />
                <span>Quay về trang chính</span>
              </Button>
            </div>
          </div>
        </main>
      );
    }

    return <Fragment key={this.state.remountKey}>{this.props.children}</Fragment>;
  }
}

/**
 * Inner route-level boundary mounted inside QueryClientProvider + BrowserRouter.
 * Automatically resets when route location changes and resets errored React Query caches on retry.
 */
export function RouteAwareErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [manualHomeResetCount, setManualHomeResetCount] = useState(0);

  const resetErroredQueries = () => {
    void queryClient.resetQueries({
      predicate: (query) => query.state.status === 'error',
    });
  };

  const handleNavigateHome = () => {
    resetErroredQueries();
    if (location.pathname === '/' && !location.search) {
      setManualHomeResetCount((c) => c + 1);
      return;
    }
    navigate('/', { replace: true });
  };

  return (
    <AppErrorBoundary
      resetKeys={[location.pathname, location.search, manualHomeResetCount]}
      onReset={resetErroredQueries}
      onNavigateHome={handleNavigateHome}
    >
      {children}
    </AppErrorBoundary>
  );
}
