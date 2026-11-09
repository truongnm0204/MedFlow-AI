import { CalendarClock, History, ShieldAlert, SlidersHorizontal } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { AppointmentPolicyFormValues } from '../schemas/appointment-policy.schema';
import { formatPolicyUpdatedAt } from '../utils/format-policy-updated-at';

export interface AppointmentPolicySidebarProps {
  values: AppointmentPolicyFormValues;
  lastUpdatedAt: string | null;
  isDirty: boolean;
}

function safeInt(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

export function AppointmentPolicySidebar({
  values,
  lastUpdatedAt,
  isDirty,
}: AppointmentPolicySidebarProps) {
  const bookingWindowDays = safeInt(values.bookingWindowDays);
  const bookingLeadHours = safeInt(values.bookingLeadHours);
  const maxActiveBookings = safeInt(values.maxActiveBookings);
  const rescheduleMinimumHours = safeInt(values.rescheduleMinimumHours);
  const maxReschedules = safeInt(values.maxReschedules);
  const cancelMinimumHours = safeInt(values.cancelMinimumHours);
  const noShowGraceMinutes = safeInt(values.noShowGraceMinutes);
  const noShowMaxCount = safeInt(values.noShowMaxCount);
  const noShowLookbackDays = safeInt(values.noShowLookbackDays);
  const noShowBlockDays = safeInt(values.noShowBlockDays);

  const noShowSummary =
    noShowBlockDays > 0
      ? `Trễ quá ${noShowGraceMinutes} phút tính NO_SHOW; vắng ≥ ${noShowMaxCount} lần trong ${noShowLookbackDays} ngày sẽ khóa đặt lịch ${noShowBlockDays} ngày.`
      : `Trễ quá ${noShowGraceMinutes} phút tính NO_SHOW; hiện đang tắt khóa đặt lịch tự động (0 ngày).`;

  return (
    <aside className="space-y-6">
      {/* CARD 1: LIVE POLICY SUMMARY */}
      <section
        data-testid="policy-summary-card"
        className="rounded-xl border border-border bg-white p-6 shadow-sm"
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-sm font-bold text-ink">Tóm tắt quy tắc đang cấu hình</h2>
            <p className="mt-1 text-[11px] text-sub">
              Diễn giải trực tiếp các tham số hiệu lực cho luồng đặt lịch bệnh nhân (UC19 – UC22).
            </p>
          </div>
          <Badge tone={isDirty ? 'amber' : 'emerald'}>
            {isDirty ? 'Chưa lưu' : 'Đang áp dụng'}
          </Badge>
        </div>

        <div className="mt-5 space-y-3 border-t border-slate-100 pt-4 text-xs">
          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <CalendarClock className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Cửa sổ & Giới hạn đặt lịch</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">
              Mở đặt trước tối đa {bookingWindowDays} ngày, cách giờ khám ít nhất {bookingLeadHours}{' '}
              giờ (tối đa {maxActiveBookings} lịch hoạt động/hồ sơ).
            </p>
          </div>

          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <SlidersHorizontal className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Đổi lịch & Hủy lịch hẹn</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">
              Đổi trước ≥ {rescheduleMinimumHours} giờ (tối đa {maxReschedules} lần); hủy trước ≥{' '}
              {cancelMinimumHours} giờ.
            </p>
          </div>

          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <ShieldAlert className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Kiểm soát vắng mặt (No-show)</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">{noShowSummary}</p>
          </div>
        </div>
      </section>

      {/* CARD 2: VERSION & AUDIT METADATA */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-sm">
        <div className="flex items-center gap-2">
          <History className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-ink">
            Phiên bản & Nhật ký kiểm toán (hospital_policies)
          </h2>
        </div>
        <p className="mt-1 text-[11px] text-sub">
          Mỗi lần cập nhật sinh phiên bản mới trong cơ sở dữ liệu và ghi nhận nhật ký kiểm toán.
        </p>

        <div className="mt-4 space-y-3 border-t border-slate-100 pt-4 text-xs">
          <div className="rounded-lg border border-slate-200 bg-white p-3.5">
            <div className="text-[11px] font-semibold text-sub">Cập nhật gần nhất</div>
            <div className="mt-1 font-mono text-xs font-bold text-ink">
              {formatPolicyUpdatedAt(lastUpdatedAt)}
            </div>
          </div>

          <div className="rounded-lg border border-slate-100 bg-slate-50/80 p-3.5 text-[11px] leading-relaxed text-sub">
            <div className="mb-1.5 font-bold text-ink">Nguyên tắc áp dụng</div>
            <ul className="space-y-1.5">
              <li className="flex items-start gap-1.5">
                <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-400" />
                <span>
                  Lưu tạo bản ghi phiên bản mới (
                  <code className="font-mono">policy_version + 1</code>) trong bảng{' '}
                  <code className="font-mono">hospital_policies</code>.
                </span>
              </li>
              <li className="flex items-start gap-1.5">
                <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-400" />
                <span>
                  Tự động ghi sự kiện <code className="font-mono">APPOINTMENT_POLICY_UPDATE</code>{' '}
                  vào <code className="font-mono">audit_logs</code>.
                </span>
              </li>
              <li className="flex items-start gap-1.5">
                <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-400" />
                <span>
                  Áp dụng ngay cho thao tác đặt/đổi/hủy mới; không hủy ngược các lịch hẹn đã xác
                  nhận trước đó.
                </span>
              </li>
            </ul>
          </div>
        </div>
      </section>
    </aside>
  );
}
