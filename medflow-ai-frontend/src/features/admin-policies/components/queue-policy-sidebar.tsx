import { CalendarClock, History, Hourglass, ShieldCheck, TriangleAlert, Users } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { QueuePolicyFormValues } from '../schemas/queue-policy.schema';
import { formatPolicyUpdatedAt } from '../utils/format-policy-updated-at';

export interface QueuePolicySidebarProps {
  values: QueuePolicyFormValues;
  lastUpdatedAt: string | null;
  isDirty: boolean;
}

function safeInt(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

export function QueuePolicySidebar({ values, lastUpdatedAt, isDirty }: QueuePolicySidebarProps) {
  const grace = safeInt(values.graceMinutes);
  const expected = safeInt(values.expectedDurationMinutes);
  const buffer = safeInt(values.safetyBufferMinutes);
  const priority = safeInt(values.walkinPriorityRatio?.priority);
  const normal = safeInt(values.walkinPriorityRatio?.normal);
  const publicNumberImmutable = Boolean(values.publicNumberImmutable);
  const manualPriorityRequiresVerification = Boolean(values.manualPriorityRequiresVerification);

  return (
    <aside className="space-y-6">
      {/* CARD 1: LIVE POLICY SUMMARY */}
      <section
        data-testid="queue-policy-summary-card"
        className="rounded-xl border border-border bg-white p-6 shadow-sm"
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-sm font-bold text-ink">Tóm tắt quy tắc đang cấu hình</h2>
            <p className="mt-1 text-[11px] text-sub">
              Diễn giải trực tiếp các tham số hiệu lực cho luồng điều phối hàng đợi khám (UC55).
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
              <span>Bảo vệ lịch hẹn</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">
              {`Đến trễ tối đa ${grace} phút vẫn giữ quyền bảo vệ lịch hẹn; quá ngưỡng chuyển LATE.`}
            </p>
          </div>

          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <Hourglass className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Safe Backfill</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">
              {`Chỉ chèn bệnh nhân đến sớm khi còn ≥ ${expected + buffer} phút (${expected} phút khám + ${buffer} phút dự phòng) trước lịch hẹn được bảo vệ kế tiếp.`}
            </p>
          </div>

          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <Users className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Công bằng</span>
            </div>
            <p className="mt-1.5 text-[11px] leading-relaxed text-sub">
              {`Sau tối đa ${priority} lượt ưu tiên/khám lại sẽ phục vụ ${normal} lượt thường.`}
            </p>
          </div>

          <div className="rounded-lg border border-slate-200 bg-slate-50/70 p-3.5">
            <div className="flex items-center gap-2 font-bold text-ink">
              <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Quy tắc</span>
            </div>
            <div className="mt-1.5 space-y-1 text-[11px] leading-relaxed">
              {publicNumberImmutable ? (
                <p className="text-sub">
                  Số thứ tự công khai giữ nguyên; chỉ tính lại thứ tự phục vụ.
                </p>
              ) : (
                <p className="flex items-center gap-1.5 font-medium text-amber-700">
                  <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span>Cảnh báo: cho phép thay đổi số thứ tự công khai sau khi cấp.</span>
                </p>
              )}
              {manualPriorityRequiresVerification ? (
                <p className="text-sub">Gán ưu tiên bắt buộc xác minh và ghi audit.</p>
              ) : (
                <p className="flex items-center gap-1.5 font-medium text-amber-700">
                  <TriangleAlert className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span>Cảnh báo: nhân viên có thể gán ưu tiên không cần xác minh.</span>
                </p>
              )}
            </div>
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
                  Lưu tạo phiên bản mới (policy_version + 1) trong hospital_policies (policy_type =
                  QUEUE).
                </span>
              </li>
              <li className="flex items-start gap-1.5">
                <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-400" />
                <span>Ghi sự kiện QUEUE_POLICY_UPDATE vào audit_logs.</span>
              </li>
              <li className="flex items-start gap-1.5">
                <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-slate-400" />
                <span>Áp dụng cho lượt điều phối tiếp theo; không đổi queueNumber đã cấp.</span>
              </li>
            </ul>
          </div>
        </div>
      </section>
    </aside>
  );
}
