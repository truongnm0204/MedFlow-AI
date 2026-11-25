import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AppProviders } from '@/app/providers/app-providers';

import { AppErrorBoundary, RouteAwareErrorBoundary } from '../app-error-boundary';

describe('AppErrorBoundary & RouteAwareErrorBoundary (#254)', () => {
  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('catches render crashes in Outer standalone mode without provider dependencies and never leaks PHI, tokens, or stack traces', () => {
    let shouldCrash = true;

    function CrashWithSensitivePhi() {
      if (shouldCrash) {
        throw new Error(
          'Render crash for BN Nguyễn Văn An (PAT-2026-9999) Chẩn đoán I20.9 Bearer eyJhbGciOiJIUzI1NiJ9.supersecret',
        );
      }
      return <div>Màn hình đã khôi phục an toàn</div>;
    }

    // Mounted completely outside QueryClientProvider & BrowserRouter
    render(
      <AppErrorBoundary>
        <CrashWithSensitivePhi />
      </AppErrorBoundary>,
    );

    const fallback = screen.getByTestId('app-error-boundary-fallback');
    expect(fallback).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Đã xảy ra sự cố hiển thị trên màn hình này' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Thử tải lại' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Quay về trang chính' })).toBeInTheDocument();

    // Strictly verify ZERO PHI, token, raw error.message, or stack trace in DOM
    const domText = document.body.textContent ?? '';
    expect(domText).not.toContain('Nguyễn Văn An');
    expect(domText).not.toContain('PAT-2026-9999');
    expect(domText).not.toContain('I20.9');
    expect(domText).not.toContain('eyJhbGci');
    expect(domText).not.toContain('supersecret');
    expect(domText).not.toContain('CrashWithSensitivePhi');

    // Retry remounts child without full browser reload
    shouldCrash = false;
    fireEvent.click(screen.getByRole('button', { name: 'Thử tải lại' }));
    expect(screen.getByText('Màn hình đã khôi phục an toàn')).toBeInTheDocument();
  });

  it('resets errored React Query caches on retry and navigates to "/" via router in RouteAwareErrorBoundary', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    // Seed an errored query in cache to verify retry resets errored queries
    await expect(
      queryClient.fetchQuery({
        queryKey: ['broken-clinical-query'],
        queryFn: async () => {
          throw new Error('Upstream 500');
        },
      }),
    ).rejects.toThrow();

    expect(queryClient.getQueryState(['broken-clinical-query'])?.status).toBe('error');
    const resetSpy = vi.spyOn(queryClient, 'resetQueries');

    let shouldCrash = true;
    function FlakyRouteScreen() {
      if (shouldCrash) {
        throw new Error('Unexpected null reading clinical view');
      }
      return <div>Trang Lâm sàng Khôi phục</div>;
    }

    const first = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/doctor/consultation']}>
          <RouteAwareErrorBoundary>
            <Routes>
              <Route path="/" element={<div>Trang Chủ MedFlow</div>} />
              <Route path="/doctor/consultation" element={<FlakyRouteScreen />} />
            </Routes>
          </RouteAwareErrorBoundary>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('app-error-boundary-fallback')).toBeInTheDocument();

    // Retry resets errored queries and remounts route screen
    shouldCrash = false;
    fireEvent.click(screen.getByRole('button', { name: 'Thử tải lại' }));
    expect(resetSpy).toHaveBeenCalled();
    expect(await screen.findByText('Trang Chủ MedFlow').catch(() => null)).toBeNull();
    expect(await screen.findByText('Trang Lâm sàng Khôi phục')).toBeInTheDocument();
    first.unmount();

    // Trigger crash in a fresh route mount, then click "Quay về trang chính" -> navigates to '/' and clears error state
    shouldCrash = true;
    const second = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/doctor/consultation']}>
          <RouteAwareErrorBoundary>
            <Routes>
              <Route path="/" element={<div>Trang Chủ MedFlow</div>} />
              <Route path="/doctor/consultation" element={<FlakyRouteScreen />} />
            </Routes>
          </RouteAwareErrorBoundary>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Quay về trang chính' }));

    await waitFor(() => {
      expect(screen.getByText('Trang Chủ MedFlow')).toBeInTheDocument();
    });
    second.unmount();
  });

  it('protects AppProviders from White Screen of Death', () => {
    window.history.replaceState({ idx: 0 }, '', '/');

    function ExplodingApp(): ReactNode {
      throw new Error('Top-level render explosion');
    }

    render(
      <AppProviders>
        <ExplodingApp />
      </AppProviders>,
    );

    expect(screen.getByTestId('app-error-boundary-fallback')).toBeInTheDocument();
  });
});
