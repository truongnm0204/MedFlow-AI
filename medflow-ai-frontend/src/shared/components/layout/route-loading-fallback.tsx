import { Loader2 } from 'lucide-react';

export function RouteLoadingFallback() {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      data-testid="route-loading-fallback"
      className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased"
    >
      <div className="flex h-16 items-center border-b border-border bg-white px-6">
        <div className="h-4 w-32 rounded bg-canvas" />
      </div>
      <div className="flex flex-1 items-center justify-center p-6">
        <div className="flex items-center gap-3 rounded-2xl border border-border bg-white px-5 py-3.5 shadow-2xs">
          <Loader2
            className="h-5 w-5 shrink-0 animate-spin text-primary motion-reduce:animate-none"
            aria-hidden="true"
          />
          <span className="text-sm font-medium text-sub">Đang tải giao diện...</span>
        </div>
      </div>
    </div>
  );
}
