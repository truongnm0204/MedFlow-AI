import { ClinicalSidebar } from '@/shared/components/layout/clinical-sidebar';
import { ClinicalStatusBar } from '@/shared/components/layout/patient-footer';

interface WorkbenchPreviewModalProps {
  open: boolean;
  onClose: () => void;
}

/** Modal xem trước HIS Workbench shell (sidebar + status bar). */
export function WorkbenchPreviewModal({ open, onClose }: WorkbenchPreviewModalProps) {
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-slate-900/50 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl border border-slate-200 shadow-2xl w-full max-w-6xl max-h-[90vh] flex flex-col overflow-hidden">
        {/* Modal Header */}
        <div className="h-14 px-6 border-b border-slate-200 flex items-center justify-between bg-slate-50">
          <div className="flex items-center gap-2">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" />
            <h3 className="text-sm font-bold text-slate-900">
              Xem Trước: Bàn Làm Việc Bác Sĩ & Linh Kiện Left Sidebar (Trắng Xanh Y Tế)
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1.5 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-200 transition-colors"
          >
            ✕ Đóng
          </button>
        </div>

        {/* Modal Body: Sidebar + Workbench Demo Content */}
        <div className="flex-1 flex overflow-hidden">
          <ClinicalSidebar />
          <div className="flex-1 p-6 bg-slate-50 overflow-y-auto space-y-4">
            <div className="p-4 rounded-xl bg-white border border-slate-200 shadow-2xs space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-slate-900">
                  Bàn Khám Lâm Sàng P.102 • BS. CKII Nguyễn Văn Hùng
                </span>
                <span className="text-xs font-mono text-emerald-700 font-semibold bg-emerald-50 px-2 py-0.5 rounded border border-emerald-200">
                  Đang Khám: Ca #08 (Nguyễn Văn An)
                </span>
              </div>
              <p className="text-xs text-slate-500">
                Giao diện bàn làm việc Bác sĩ hoàn toàn theo phong cách Trắng Xanh y tế, không dùng
                màu tối, hỗ trợ chuyển đổi nhanh hàng đợi khám, bệnh án EMR, kê đơn F2 và chỉ định
                cận lâm sàng.
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
