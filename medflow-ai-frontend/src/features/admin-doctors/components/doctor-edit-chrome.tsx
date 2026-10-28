import { Save, UserCog } from 'lucide-react';
import { Button } from '@/shared/ui/button';

import type { Doctor } from '../types/doctor.types';

export function DoctorEditHeader({
  doctor,
  isActive,
  onClose,
}: {
  doctor: Doctor;
  isActive: boolean;
  onClose: () => void;
}) {
  return (
    <header className="flex items-center justify-between border-b border-border bg-slate-50 px-6 py-4">
      <div className="flex min-w-0 items-center gap-3">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-lg border border-sky-100 bg-sky-50">
          <UserCog className="h-5 w-5 text-primary" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="doctor-edit-title" className="text-base font-bold">
              Hồ sơ Bác sĩ - {doctor.name}
            </h2>
            <span className="rounded border border-sky-200 bg-sky-50 px-2 py-0.5 font-mono text-[10px] font-bold text-primary">
              {doctor.id}
            </span>
            <span
              className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${isActive ? 'border-emerald-200 bg-emerald-50 text-emerald-700' : 'border-amber-200 bg-amber-50 text-amber-700'}`}
            >
              {isActive ? '● Active' : '● Tạm ngừng'}
            </span>
          </div>
          <p className="mt-0.5 text-[11px] text-sub">
            Mã màn hình: SCR-ADM-DOC-03 • Quản lý thông tin học vị, phân công chuyên khoa và buồng
            khám tiếp nhận.
          </p>
        </div>
      </div>
      <button
        type="button"
        aria-label="Đóng chỉnh sửa"
        className="ml-3 text-xl text-slate-400 hover:text-ink"
        onClick={onClose}
      >
        ×
      </button>
    </header>
  );
}

export function DoctorEditFooter({
  onClose,
  onSuspend,
}: {
  onClose: () => void;
  onSuspend: () => void;
}) {
  return (
    <footer className="flex flex-wrap items-center justify-between gap-2 border-t border-border bg-slate-50 px-6 py-4">
      <button
        type="button"
        className="rounded border border-rose-200 px-3 py-2 text-[11px] font-semibold text-rose-600"
        onClick={onSuspend}
      >
        ⊖ Tạm ngừng nhận lịch
      </button>
      <div className="flex gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onClose}>
          Hủy bỏ [Esc]
        </Button>
        <Button id="doctor-edit-submit" type="submit" size="sm">
          <Save className="h-4 w-4" aria-hidden="true" />
          Lưu hồ sơ Bác sĩ [Ctrl+S]
        </Button>
      </div>
    </footer>
  );
}
