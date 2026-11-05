import { AlertTriangle, Building2, ShieldAlert } from 'lucide-react';
import { useState } from 'react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import type {
  DeactivateDepartmentPayload,
  DeactivateDepartmentReason,
} from '../types/department-deactivate.types';
import type { DepartmentItem } from '../types/department.types';

const REASON_OPTIONS: { value: DeactivateDepartmentReason; label: string }[] = [
  { value: 'RESTRUCTURING', label: 'Tái cơ cấu / Sắp xếp lại khoa phòng' },
  { value: 'MAINTENANCE', label: 'Nâng cấp / Sửa chữa cơ sở vật chất' },
  { value: 'OPERATION_SUSPENDED', label: 'Tạm dừng hoạt động chuyên môn' },
  { value: 'OTHER', label: 'Khác' },
];

export interface DepartmentDeactivateModalProps {
  department: DepartmentItem | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (departmentId: string, payload: DeactivateDepartmentPayload) => Promise<void> | void;
  isSubmitting?: boolean;
}

/**
 * SCR-ADM-DEP-03 — Modal xác nhận ngừng hoạt động khoa phòng và kiểm tra ràng buộc.
 * Quản trị viên chỉ có thể vô hiệu hóa khi không còn buồng khám, bác sĩ hoặc lịch khám đang hoạt động.
 */
export function DepartmentDeactivateModal({
  department,
  open,
  onClose,
  onConfirm,
  isSubmitting = false,
}: DepartmentDeactivateModalProps) {
  const [reason, setReason] = useState<DeactivateDepartmentReason>('RESTRUCTURING');
  const [note, setNote] = useState('');
  const [internalSubmitting, setInternalSubmitting] = useState(false);

  if (!open || !department) return null;

  const activeRooms = department.activeRoomsCount ?? 0;
  const activeDoctors = department.activeDoctorsCount ?? 0;
  const pendingAppointments = department.pendingAppointmentsCount ?? 0;
  const isBlocked = Boolean(
    department.hasActiveDependencies ||
    activeRooms > 0 ||
    activeDoctors > 0 ||
    pendingAppointments > 0,
  );
  const loading = isSubmitting || internalSubmitting;

  const handleConfirm = async () => {
    if (isBlocked || loading) return;
    try {
      setInternalSubmitting(true);
      await onConfirm(department.id, {
        departmentId: department.id,
        reason,
        note: note.trim() || undefined,
      });
    } finally {
      setInternalSubmitting(false);
    }
  };

  const blockerDetails: string[] = [];
  if (activeRooms > 0) blockerDetails.push(`${activeRooms} buồng khám`);
  if (activeDoctors > 0) blockerDetails.push(`${activeDoctors} bác sĩ`);
  if (pendingAppointments > 0) blockerDetails.push(`${pendingAppointments} lượt khám`);

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-[560px]">
      <div className="p-6 sm:p-7">
        {/* Header với icon cảnh báo và tiêu đề */}
        <div className="flex items-center gap-3.5">
          <span
            className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border ${
              isBlocked
                ? 'border-rose-200 bg-rose-50 text-rose-700'
                : 'border-amber-200 bg-amber-50 text-amber-700'
            }`}
            aria-hidden="true"
          >
            {isBlocked ? (
              <ShieldAlert className="h-5 w-5" />
            ) : (
              <AlertTriangle className="h-5 w-5" />
            )}
          </span>
          <div>
            <h2 id="deactivate-department-title" className="text-xl font-bold text-ink">
              Ngừng hoạt động khoa phòng?
            </h2>
            <p className="text-xs text-sub">SCR-ADM-DEP-03 · Kiểm tra điều kiện vô hiệu hóa</p>
          </div>
        </div>

        {/* Thông tin định danh khoa phòng: Tên · Mã · Cơ sở */}
        <div className="mt-4 flex items-center gap-2 rounded-lg border border-border bg-canvas px-3.5 py-2.5 text-xs">
          <Building2 className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
          <div className="min-w-0 flex-1">
            <span className="font-semibold text-ink">{department.name}</span>
            <span className="mx-2 text-slate-300">|</span>
            <span className="font-mono font-medium text-slate-700">{department.code}</span>
            <span className="mx-2 text-slate-300">|</span>
            <span className="text-sub">{department.facility}</span>
          </div>
        </div>

        {/* Mô tả giải thích tác động */}
        <p id="deactivate-department-desc" className="mt-3 text-xs leading-relaxed text-sub">
          Khoa phòng sẽ chuyển sang trạng thái <strong>Tạm ngừng</strong>. Các dịch vụ khám và buồng
          khám trực thuộc sẽ không thể tiếp nhận lượt khám mới. Dữ liệu bệnh án, chỉ định và hồ sơ
          khám trước đây vẫn được lưu trữ nguyên vẹn.
        </p>

        {/* Khung cảnh báo điều kiện tiên quyết / ràng buộc phụ thuộc */}
        <div
          className={`mt-4 rounded-xl border p-4 text-xs ${
            isBlocked
              ? 'border-rose-200 bg-rose-50/80 text-rose-900'
              : 'border-emerald-200 bg-emerald-50/70 text-emerald-900'
          }`}
          role={isBlocked ? 'alert' : 'status'}
        >
          <p className="font-bold">
            {isBlocked
              ? 'Không thể ngừng hoạt động (Còn ràng buộc đang vận hành)'
              : 'Điều kiện kiểm tra hợp lệ'}
          </p>
          <p className="mt-1.5 leading-relaxed">
            {isBlocked
              ? `Khoa phòng đang có ${blockerDetails.join(', ')} đang hoạt động. Vui lòng chuyển hoặc ngừng các buồng khám và nhân sự trực thuộc trước khi ngừng hoạt động khoa phòng.`
              : 'Khoa phòng hiện không có buồng khám, nhân sự hoặc lịch khám nào đang hoạt động. Bạn có thể tiến hành ngừng hoạt động an toàn.'}
          </p>
        </div>

        {/* Form chọn lý do khi đủ điều kiện */}
        {!isBlocked && (
          <div className="mt-4 space-y-3">
            <div>
              <label
                htmlFor="deactivate-reason-select"
                className="block text-xs font-semibold text-navy mb-1"
              >
                Lý do ngừng hoạt động <span className="text-red-500">*</span>
              </label>
              <select
                id="deactivate-reason-select"
                value={reason}
                onChange={(e) => setReason(e.target.value as DeactivateDepartmentReason)}
                disabled={loading}
                className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink outline-none focus:border-primary focus:ring-1 focus:ring-primary"
              >
                {REASON_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label
                htmlFor="deactivate-note-input"
                className="block text-xs font-semibold text-navy mb-1"
              >
                Ghi chú bổ sung (tùy chọn)
              </label>
              <textarea
                id="deactivate-note-input"
                value={note}
                onChange={(e) => setNote(e.target.value)}
                disabled={loading}
                placeholder="Nhập chi tiết biên bản họp hoặc kế hoạch tái cơ cấu..."
                rows={2}
                className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink outline-none focus:border-primary focus:ring-1 focus:ring-primary placeholder:text-slate-400"
              />
            </div>
          </div>
        )}

        {/* Ghi chú bảo toàn dữ liệu lịch sử */}
        <p className="mt-3.5 text-xs text-sub leading-normal">
          Xóa cứng không áp dụng: hồ sơ và mọi liên kết lịch sử được bảo toàn để phục vụ tra cứu và
          kiểm toán y tế.
        </p>

        {/* Nút thao tác chân modal */}
        <div className="mt-6 flex items-center justify-end gap-3 pt-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose} disabled={loading}>
            Hủy bỏ
          </Button>
          <Button
            type="button"
            variant={isBlocked ? 'secondary' : 'danger'}
            size="sm"
            onClick={handleConfirm}
            disabled={isBlocked || loading}
            aria-disabled={isBlocked || loading}
            loading={loading}
          >
            {loading ? 'Đang xử lý...' : 'Ngừng hoạt động'}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
