import { AlertCircle, CheckCircle2, ShieldCheck } from 'lucide-react';

interface ConflictStatus {
  checked: boolean;
  hasConflict: boolean;
  message: string;
}

interface SlotCreateSidebarProps {
  onCheckConflict: () => void;
  conflictStatus: ConflictStatus | null;
  onViewBookings: () => void;
}

export function SlotCreateSidebar({
  onCheckConflict,
  conflictStatus,
  onViewBookings,
}: SlotCreateSidebarProps) {
  return (
    <aside className="space-y-6" aria-label="Kiểm soát tài nguyên và sức chứa">
      {/* Card 3: Xác nhận tài nguyên */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-xs">
        <h2 className="text-base font-bold text-ink">Xác nhận tài nguyên</h2>
        <p className="mt-1 text-xs text-sub">Dịch vụ, thời gian và tài nguyên của slot.</p>

        <div className="mt-3">
          <span className="inline-flex items-center gap-1.5 rounded-md bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-700">
            <ShieldCheck className="h-3.5 w-3.5 text-slate-500" aria-hidden="true" />
            <span>Quản lý bởi · UC53</span>
          </span>
        </div>

        <div className="mt-5 rounded-lg border border-border/80 bg-slate-50/60 p-4">
          <h3 className="text-xs font-semibold text-ink">Điều kiện mở slot</h3>
          <ul className="mt-2.5 space-y-2 text-xs text-sub">
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-sky-500" aria-hidden="true" />
              <span>Bác sĩ · nếu phân công</span>
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-sky-500" aria-hidden="true" />
              <span>Dịch vụ · được phép đặt</span>
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-sky-500" aria-hidden="true" />
              <span>Phòng · nếu phân công, không trùng lịch</span>
            </li>
          </ul>
        </div>

        <div className="mt-4">
          <button
            type="button"
            onClick={onCheckConflict}
            className="w-full rounded-lg border border-border bg-white py-2 px-3 text-xs font-semibold text-ink shadow-2xs hover:bg-slate-50 hover:border-slate-300 transition-colors cursor-pointer"
          >
            Kiểm tra xung đột
          </button>
        </div>

        {conflictStatus && conflictStatus.checked && (
          <div
            role="status"
            className={`mt-3 rounded-lg border p-3 text-xs leading-relaxed ${
              conflictStatus.hasConflict
                ? 'border-amber-200 bg-amber-50 text-amber-900'
                : 'border-emerald-200 bg-emerald-50 text-emerald-900'
            }`}
          >
            <div className="flex items-start gap-2">
              {conflictStatus.hasConflict ? (
                <AlertCircle
                  className="h-4 w-4 text-amber-600 shrink-0 mt-0.5"
                  aria-hidden="true"
                />
              ) : (
                <CheckCircle2
                  className="h-4 w-4 text-emerald-600 shrink-0 mt-0.5"
                  aria-hidden="true"
                />
              )}
              <span>{conflictStatus.message}</span>
            </div>
          </div>
        )}

        <p className="mt-3 text-[11px] text-slate-400">
          Không mở slot khi tài nguyên không hợp lệ.
        </p>
      </section>

      {/* Card 4: Sức chứa & đóng slot */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-xs">
        <h2 className="text-base font-bold text-ink">Sức chứa & đóng slot</h2>
        <p className="mt-1 text-xs text-sub">
          Đóng slot ngăn lượt đặt mới, không tự hủy lượt đã có.
        </p>

        <div className="mt-3 text-xs text-slate-500">
          <span>Giữ nguyên lượt đặt hiện có</span>
        </div>

        <div className="mt-4 rounded-lg border border-border/80 bg-slate-50/60 p-4">
          <h3 className="text-xs font-semibold text-ink">Kiểm soát thay đổi</h3>
          <ul className="mt-2.5 space-y-2 text-xs text-sub">
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-slate-400" aria-hidden="true" />
              <span>Không giảm dưới lượt đã đặt</span>
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-slate-400" aria-hidden="true" />
              <span>Đóng slot · chặn lượt mới</span>
            </li>
            <li className="flex items-center gap-2">
              <span className="h-1.5 w-1.5 rounded-full bg-slate-400" aria-hidden="true" />
              <span>Lượt hiện có · giữ nguyên</span>
            </li>
          </ul>
        </div>

        <div className="mt-4">
          <button
            type="button"
            onClick={onViewBookings}
            className="w-full rounded-lg border border-border bg-white py-2 px-3 text-xs font-semibold text-ink shadow-2xs hover:bg-slate-50 hover:border-slate-300 transition-colors cursor-pointer"
          >
            Xem lượt đã đặt
          </button>
        </div>

        <p className="mt-3 text-[11px] text-slate-400">
          Rà soát số lượt đặt trước khi thay đổi sức chứa.
        </p>
      </section>
    </aside>
  );
}
