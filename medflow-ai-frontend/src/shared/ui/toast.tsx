/* oxlint-disable react/only-export-components */
import { useEffect } from 'react';
import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react';
import { create } from 'zustand';

export type ToastTone = 'success' | 'error' | 'info';

export interface ToastItem {
  id: string;
  tone: ToastTone;
  title?: string;
  message: string;
  durationMs?: number;
}

interface ToastState {
  toasts: ToastItem[];
  push: (item: Omit<ToastItem, 'id'> & { id?: string }) => string;
  dismiss: (id: string) => void;
  clear: () => void;
}

let toastSeq = 0;

export const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  push: (item) => {
    toastSeq += 1;
    const id = item.id ?? `toast-${Date.now()}-${toastSeq}`;
    const nextItem: ToastItem = {
      id,
      tone: item.tone,
      title: item.title,
      message: item.message,
      durationMs: item.durationMs ?? 4500,
    };
    set((state) => ({
      toasts: [...state.toasts.slice(-4), nextItem],
    }));
    return id;
  },
  dismiss: (id) =>
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id),
    })),
  clear: () => set({ toasts: [] }),
}));

export const toast = {
  success: (message: string, title?: string, durationMs?: number) =>
    useToastStore.getState().push({ tone: 'success', title, message, durationMs }),
  error: (message: string, title?: string, durationMs?: number) =>
    useToastStore.getState().push({ tone: 'error', title, message, durationMs }),
  info: (message: string, title?: string, durationMs?: number) =>
    useToastStore.getState().push({ tone: 'info', title, message, durationMs }),
  dismiss: (id: string) => useToastStore.getState().dismiss(id),
  clear: () => useToastStore.getState().clear(),
};

const TONE_STYLES: Record<
  ToastTone,
  {
    container: string;
    iconColor: string;
    titleColor: string;
    defaultTitle: string;
  }
> = {
  success: {
    container: 'border-emerald-200 bg-white text-ink',
    iconColor: 'text-emerald-600',
    titleColor: 'text-emerald-950',
    defaultTitle: 'Thành công',
  },
  error: {
    container: 'border-rose-200 bg-white text-ink',
    iconColor: 'text-danger',
    titleColor: 'text-rose-950',
    defaultTitle: 'Thông báo lỗi',
  },
  info: {
    container: 'border-sky-200 bg-white text-ink',
    iconColor: 'text-primary',
    titleColor: 'text-navy',
    defaultTitle: 'Thông báo',
  },
};

function ToastCard({ item, onDismiss }: { item: ToastItem; onDismiss: (id: string) => void }) {
  useEffect(() => {
    const duration = item.durationMs ?? 4500;
    if (duration <= 0) return undefined;
    const timer = window.setTimeout(() => {
      onDismiss(item.id);
    }, duration);
    return () => window.clearTimeout(timer);
  }, [item.id, item.durationMs, onDismiss]);

  const style = TONE_STYLES[item.tone];
  const Icon = item.tone === 'success' ? CheckCircle2 : item.tone === 'error' ? AlertCircle : Info;

  return (
    <div
      role={item.tone === 'error' ? 'alert' : 'status'}
      aria-live={item.tone === 'error' ? 'assertive' : 'polite'}
      data-testid={`toast-${item.tone}`}
      className={`pointer-events-auto flex items-start gap-3 rounded-2xl border p-4 shadow-lg transition-all duration-150 ${style.container}`}
    >
      <Icon className={`mt-0.5 h-5 w-5 shrink-0 ${style.iconColor}`} aria-hidden="true" />
      <div className="min-w-0 flex-1 space-y-0.5">
        <p className={`text-xs font-bold sm:text-sm ${style.titleColor}`}>
          {item.title ?? style.defaultTitle}
        </p>
        <p className="text-xs leading-relaxed text-sub sm:text-sm">{item.message}</p>
      </div>
      <button
        type="button"
        onClick={() => onDismiss(item.id)}
        aria-label="Đóng thông báo"
        className="rounded-lg p-1 text-sub transition-colors duration-150 hover:bg-canvas hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
      >
        <X className="h-4 w-4" aria-hidden="true" />
      </button>
    </div>
  );
}

export function ToastViewport() {
  const toasts = useToastStore((s) => s.toasts);
  const dismiss = useToastStore((s) => s.dismiss);

  if (toasts.length === 0) return null;

  return (
    <div
      aria-label="Thông báo hệ thống"
      className="pointer-events-none fixed top-4 right-4 z-50 flex w-full max-w-sm flex-col gap-2.5 px-4 sm:px-0"
    >
      {toasts.map((item) => (
        <ToastCard key={item.id} item={item} onDismiss={dismiss} />
      ))}
    </div>
  );
}
