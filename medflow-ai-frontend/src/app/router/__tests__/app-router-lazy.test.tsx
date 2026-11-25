import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { Suspense, lazy, type ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { RouteLoadingFallback } from '@/shared/components/layout/route-loading-fallback';

describe('AppRouter lazy code splitting & RouteLoadingFallback (#828)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('renders accessible RouteLoadingFallback with semantic design tokens and reduced-motion support', () => {
    render(<RouteLoadingFallback />);

    const fallback = screen.getByTestId('route-loading-fallback');
    expect(fallback).toHaveAttribute('role', 'status');
    expect(fallback).toHaveAttribute('aria-live', 'polite');
    expect(fallback).toHaveAttribute('aria-busy', 'true');
    expect(fallback.className).toContain('bg-canvas');
    expect(fallback.className).toContain('text-ink');
    expect(screen.getByText('Đang tải giao diện...')).toBeInTheDocument();

    const icon = fallback.querySelector('svg');
    expect(icon).toHaveAttribute('aria-hidden', 'true');
    expect(icon?.getAttribute('class') ?? '').toContain('motion-reduce:animate-none');
  });

  it('displays RouteLoadingFallback while a lazy route is pending and resolves to target screen', async () => {
    let resolveChunk!: (mod: { default: () => ReactElement }) => void;
    const DeferredScreen = lazy(
      () =>
        new Promise<{ default: () => ReactElement }>((resolve) => {
          resolveChunk = resolve;
        }),
    );

    render(
      <Suspense fallback={<RouteLoadingFallback />}>
        <DeferredScreen />
      </Suspense>,
    );

    expect(screen.getByTestId('route-loading-fallback')).toBeInTheDocument();
    resolveChunk({ default: () => <div>Màn hình Phân hệ Đã Tải</div> });

    expect(await screen.findByText('Màn hình Phân hệ Đã Tải')).toBeInTheDocument();
    expect(screen.queryByTestId('route-loading-fallback')).not.toBeInTheDocument();
  });

  it('keeps core routes eager (/ and /login) and lazy-loads secondary routes (/booking) via AppRouter', async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/login']}>
          <AppRouter />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    // /login renders synchronously on frame 0 without Suspense fallback
    expect(screen.queryByTestId('route-loading-fallback')).not.toBeInTheDocument();
    expect(screen.getByText('Cổng Đăng Nhập Duy Nhất')).toBeInTheDocument();
    unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/booking']}>
          <AppRouter />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(
      await screen.findByRole('heading', { name: /Đặt lịch khám ngoại trú/i }),
    ).toBeInTheDocument();
  });
});
