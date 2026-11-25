import { QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { BrowserRouter } from 'react-router-dom';

import {
  AppErrorBoundary,
  RouteAwareErrorBoundary,
} from '@/shared/components/layout/app-error-boundary';
import { installGlobalPopStateInterceptor } from '@/shared/lib/navigation-guard-manager';
import { queryClient } from '@/shared/lib/query-client';
import { ToastViewport } from '@/shared/ui/toast';

installGlobalPopStateInterceptor();

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <AppErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <RouteAwareErrorBoundary>{children}</RouteAwareErrorBoundary>
          <ToastViewport />
        </BrowserRouter>
      </QueryClientProvider>
    </AppErrorBoundary>
  );
}
