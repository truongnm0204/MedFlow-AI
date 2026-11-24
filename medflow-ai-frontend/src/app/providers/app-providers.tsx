import { QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { BrowserRouter } from 'react-router-dom';

import { queryClient } from '@/shared/lib/query-client';
import { ToastViewport } from '@/shared/ui/toast';

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {children}
        <ToastViewport />
      </BrowserRouter>
    </QueryClientProvider>
  );
}
