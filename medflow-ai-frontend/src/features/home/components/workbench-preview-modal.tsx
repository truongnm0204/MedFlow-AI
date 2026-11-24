import { X } from 'lucide-react';
import { useEffect } from 'react';

import { ClinicalSidebar } from '@/shared/components/layout/clinical-sidebar';
import { ClinicalStatusBar } from '@/shared/components/layout/patient-footer';

interface WorkbenchPreviewModalProps {
  open: boolean;
  onClose: () => void;
}

/** Modal xem trước HIS Workbench shell (sidebar + status bar). */
export function WorkbenchPreviewModal({ open, onClose }: WorkbenchPreviewModalProps) {
  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="workbench-preview-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-xs"
    >
      <div className="flex max-h-[90vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl">
        {/* Modal Header */}
        <div className="flex h-14 items-center justify-between border-b border-border bg-canvas px-6">
          <div className="flex items-center gap-2">
            <span className="h-2.5 w-2.5 rounded-full bg-primary" aria-hidden="true" />
            <h3 id="workbench-preview-modal-title" className="text-sm font-bold text-ink">
              Xem Trước: Bàn Làm Việc Bác Sĩ & Linh Kiện Left Sidebar (Trắng Xanh Y Tế)
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng cửa sổ xem trước"
            className="inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-sub transition-colors hover:bg-slate-200/70 hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <X className="h-4 w-4" aria-hidden="true" />
            <span>Đóng</span>
          </button>
        </div>

        {/* Modal Body: Sidebar + Workbench Demo Content */}
        <div className="flex flex-1 overflow-hidden">
          <ClinicalSidebar />
          <div className="flex-1 space-y-4 overflow-y-auto bg-canvas p-6">
            <div className="space-y-2 rounded-xl border border-border bg-white p-4 shadow-2xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-xs font-bold text-ink">Bàn Khám Lâm Sàng Ngoại Trú</span>
                <span className="rounded border border-sky-200 bg-sky-50 px-2 py-0.5 font-mono text-xs font-semibold text-primary">
                  Bố cục chuẩn HIS Workbench
                </span>
              </div>
              <p className="text-xs text-sub">
                Giao diện bàn làm việc Bác sĩ hoàn toàn theo phong cách Trắng Xanh y tế, không dùng
                màu tối, hỗ trợ chuyển đổi nhanh hàng đợi khám, bệnh án EMR, kê đơn và chỉ định cận
                lâm sàng.
              </p>
            </div>
          </div>
        </div>

        {/* Modal Footer: Clinical Status Bar */}
        <ClinicalStatusBar />
      </div>
    </div>
  );
}
