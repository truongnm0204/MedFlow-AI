import { AlertTriangle } from 'lucide-react';
import { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';

import { Button } from '@/shared/ui/button';

export interface UnsavedChangesModalProps {
  open: boolean;
  title?: string;
  description?: string;
  stayLabel?: string;
  leaveLabel?: string;
  onStay: () => void;
  onLeave: () => void;
}

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function UnsavedChangesModal({
  open,
  title = 'Thay đổi chưa được lưu',
  description = 'Bạn đang có dữ liệu nhập dở chưa lưu trên biểu mẫu. Nếu rời khỏi trang lúc này, các thay đổi vừa nhập sẽ bị mất.',
  stayLabel = 'Ở lại tiếp tục nhập',
  leaveLabel = 'Rời trang & hủy thay đổi',
  onStay,
  onLeave,
}: UnsavedChangesModalProps) {
  const titleId = useId();
  const descriptionId = useId();
  const panelRef = useRef<HTMLDivElement | null>(null);
  const stayButtonRef = useRef<HTMLButtonElement | null>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const onStayRef = useRef(onStay);

  useEffect(() => {
    onStayRef.current = onStay;
  }, [onStay]);

  useEffect(() => {
    if (!open) return;

    previousFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    // Default focus on the safe non-destructive button ("Ở lại tiếp tục nhập")
    stayButtonRef.current?.focus();

    const handleKeyDownCapture = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        event.stopImmediatePropagation();
        onStayRef.current();
        return;
      }

      if (event.key === 'Tab' && panelRef.current) {
        const focusable = Array.from(
          panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
        );
        if (focusable.length === 0) return;

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;

        if (event.shiftKey) {
          if (active === first || !panelRef.current.contains(active)) {
            event.preventDefault();
            last.focus();
          }
        } else if (active === last || !panelRef.current.contains(active)) {
          event.preventDefault();
          first.focus();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDownCapture, { capture: true });

    return () => {
      window.removeEventListener('keydown', handleKeyDownCapture, { capture: true });
      document.body.style.overflow = prevOverflow;
      const prevEl = previousFocusRef.current;
      previousFocusRef.current = null;
      if (prevEl && document.contains(prevEl)) {
        prevEl.focus();
      }
    };
  }, [open]);

  if (!open) return null;

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-navy/50 p-4 backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onStay();
        }
      }}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      aria-describedby={descriptionId}
      data-testid="unsaved-changes-modal"
    >
      <div
        ref={panelRef}
        className="w-full max-w-md rounded-2xl border border-border bg-white p-6 shadow-2xl"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="flex items-start gap-3.5">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-amber-50 text-amber-600 border border-amber-200">
            <AlertTriangle className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0 flex-1 space-y-1.5">
            <h2 id={titleId} className="text-base font-bold text-navy">
              {title}
            </h2>
            <p id={descriptionId} className="text-sm leading-relaxed text-sub">
              {description}
            </p>
          </div>
        </div>

        <div className="mt-6 flex flex-col-reverse gap-2.5 sm:flex-row sm:justify-end">
          <Button
            ref={stayButtonRef}
            type="button"
            variant="secondary"
            size="sm"
            onClick={onStay}
            className="focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            {stayLabel}
          </Button>
          <Button
            type="button"
            variant="danger"
            size="sm"
            onClick={onLeave}
            className="focus-visible:ring-2 focus-visible:ring-danger focus-visible:outline-none"
          >
            {leaveLabel}
          </Button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
