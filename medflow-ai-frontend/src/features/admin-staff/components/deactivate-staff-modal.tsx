import { AlertTriangle, CheckCircle2, ShieldAlert } from 'lucide-react';
import { useEffect, useId, useState } from 'react';

import type {
  DeactivateStaffPayload,
  DeactivateStaffReason,
  StaffTargetSummary,
} from '../types/deactivate-staff.types';

const DEACTIVATE_REASON_OPTIONS: { value: DeactivateStaffReason; label: string }[] = [
  { value: 'RESIGNED', label: 'Nghỉ việc / Chấm dứt hợp đồng' },
  { value: 'TRANSFERRED', label: 'Chuyển cơ sở y tế khác' },
  { value: 'SUSPENDED', label: 'Tạm đình chỉ công tác' },
  { value: 'OTHER', label: 'Khác' },
];

interface DeactivateStaffModalProps {
  staff: StaffTargetSummary;
  onCancel: () => void;
  onConfirm: (payload: DeactivateStaffPayload) => void;
}

export function DeactivateStaffModal({ staff, onCancel, onConfirm }: DeactivateStaffModalProps) {
  const titleId = useId();
  const [reason, setReason] = useState<DeactivateStaffReason>('RESIGNED');
  const [note, setNote] = useState('');
  const blocked = staff.pendingAssignments > 0;

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onCancel]);

  const submit = () => {
    if (blocked) return;
    const trimmed = note.trim();
    onConfirm({ staffId: staff.id, reason, ...(trimmed ? { note: trimmed } : {}) });
  };

  const info: [string, string, boolean][] = [
    ['Mã nhân sự', staff.staffCode, true],
    ['Họ và tên', staff.fullName, false],
    ['Vai trò', staff.role, false],
    ['Khoa công tác', staff.department, false],
    ['Email', staff.email, false],
  ];

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4"
    >
      <div className="max-h-full w-full max-w-lg overflow-y-auto rounded-xl border border-border bg-white shadow-xl">
        <div className="flex items-start gap-3 border-b border-border px-6 py-4">
          <span className="rounded-full bg-red-50 p-2 text-red-600">
            <AlertTriangle className="h-5 w-5" aria-hidden="true" />
          </span>
          <h2 id={titleId} className="text-base font-bold text-ink">
            Ngừng hoạt động tài khoản nhân sự?
          </h2>
        </div>

        <div className="space-y-4 p-6 text-sm">
          <dl className="grid grid-cols-2 gap-3 rounded-lg border border-border bg-canvas p-4">
            {info.map(([label, value, mono]) => (
              <div key={label}>
                <dt className="text-xs text-sub">{label}</dt>
                <dd className={`font-semibold text-ink ${mono ? 'font-mono' : ''}`}>{value}</dd>
              </div>
            ))}
          </dl>

          <section aria-label="Kiểm tra phụ thuộc vận hành" className="space-y-2">
            <h3 className="text-xs font-bold uppercase text-navy">Kiểm tra phụ thuộc vận hành</h3>
            <p className="flex gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
              <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
              Bác sĩ / Nhân sự sẽ không nhận thêm lịch hoặc phân công mới. Lịch sử khám, hồ sơ bệnh
              án, chỉ định và đơn thuốc vẫn được lưu.
            </p>
            {blocked ? (
              <p
                role="alert"
                className="flex gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-xs font-semibold text-red-700"
              >
                <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                Số ca khám / lịch hẹn đang phụ trách: {staff.pendingAssignments} ca (Cần chuyển giao
                trước khi vô hiệu hóa)
              </p>
            ) : (
              <p className="flex gap-2 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-xs font-semibold text-emerald-700">
                <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden="true" />
                Số ca khám / lịch hẹn đang phụ trách: 0 ca (Đủ điều kiện vô hiệu hóa)
              </p>
            )}
          </section>

          <fieldset className="space-y-2">
            <legend className="text-xs font-bold text-ink">Lý do vô hiệu hóa</legend>
            {DEACTIVATE_REASON_OPTIONS.map((option) => (
              <label key={option.value} className="flex items-center gap-2 text-sm text-ink">
                <input
                  type="radio"
                  name="deactivate-reason"
                  value={option.value}
                  checked={reason === option.value}
                  onChange={() => setReason(option.value)}
                />
                {option.label}
              </label>
            ))}
          </fieldset>

          <div>
            <label htmlFor="deactivate-note" className="text-xs font-bold text-ink">
              Ghi chú lý do (tùy chọn)
            </label>
            <textarea
              id="deactivate-note"
              value={note}
              onChange={(event) => setNote(event.target.value)}
              rows={3}
              className="mt-1 w-full rounded-lg border border-border p-2 text-sm text-ink"
            />
          </div>
        </div>

        <div className="flex justify-end gap-3 border-t border-border bg-canvas px-6 py-3">
          <button
            type="button"
            onClick={onCancel}
            aria-label="Hủy bỏ"
            className="rounded-lg border border-border bg-white px-4 py-2 text-sm font-semibold text-ink hover:bg-slate-50"
          >
            Hủy bỏ
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={blocked}
            aria-label="Xác nhận ngừng hoạt động"
            className="rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Xác nhận ngừng hoạt động
          </button>
        </div>
      </div>
    </div>
  );
}
