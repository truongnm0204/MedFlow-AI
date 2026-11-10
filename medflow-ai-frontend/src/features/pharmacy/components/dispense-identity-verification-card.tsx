import { CheckCircle2, CreditCard, FileText, UserCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { DispenseConfirmContext } from '../types/dispense-confirm.types';

interface DispenseIdentityVerificationCardProps {
  context: DispenseConfirmContext;
  isIdentityVerified: boolean;
  onToggleIdentityVerified: () => void;
}

export function DispenseIdentityVerificationCard({
  context,
  isIdentityVerified,
  onToggleIdentityVerified,
}: DispenseIdentityVerificationCardProps) {
  const { receiver } = context;

  return (
    <div className="space-y-4">
      {/* Card 1: Identity verification */}
      <Card>
        <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
          <div className="flex items-center gap-2">
            <UserCheck className="h-4 w-4 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">
              1. Xác minh Đối chiếu Định danh Người Nhận
            </h2>
          </div>
          <Badge tone="sky">STT: {context.ticketNo}</Badge>
        </CardHeader>

        <CardContent className="space-y-3 text-xs">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 rounded-xl bg-canvas p-3.5 border border-border">
            <div>
              <span className="text-sub">Họ tên bệnh nhân:</span>
              <p className="font-bold text-navy">{receiver.fullName}</p>
              <p className="font-mono text-[11px] text-sub">{receiver.patientCode}</p>
            </div>
            <div>
              <span className="text-sub">CCCD / VNeID đối chiếu:</span>
              <p className="font-mono font-bold text-navy">{receiver.identityCard}</p>
              <p className="text-[11px] text-sub">SĐT: {receiver.phone}</p>
            </div>
            <div className="sm:col-span-2 border-t border-slate-200/70 pt-2">
              <span className="text-sub">Người nhận thực tế tại quầy: </span>
              <strong className="text-navy">{receiver.receiverName}</strong>
            </div>
          </div>

          <label className="flex cursor-pointer items-center gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50/70 p-3 text-xs font-bold text-emerald-800">
            <input
              type="checkbox"
              checked={isIdentityVerified}
              onChange={onToggleIdentityVerified}
              className="h-4 w-4 rounded border-emerald-400 text-emerald-600 focus:ring-emerald-500"
            />
            <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
            <span>ĐÃ ĐỐI CHIẾU TRÙNG KHỚP 100% (VERIFIED)</span>
          </label>
        </CardContent>
      </Card>

      {/* Card 2: Prescription & Payment summary */}
      <Card>
        <CardHeader className="py-3.5">
          <div className="flex items-center gap-2">
            <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">2. Hồ sơ Đơn thuốc &amp; Gói cấp phát</h2>
          </div>
        </CardHeader>

        <CardContent className="space-y-2.5 text-xs">
          <div className="flex justify-between py-1 border-b border-slate-100">
            <span className="text-sub">Mã đơn thuốc:</span>
            <span className="font-mono font-bold text-primary">{context.rxCode}</span>
          </div>
          <div className="flex justify-between py-1 border-b border-slate-100">
            <span className="text-sub">Lượt khám ngoại trú:</span>
            <span className="font-mono font-medium text-ink">{context.visitCode}</span>
          </div>
          <div className="flex justify-between py-1 border-b border-slate-100">
            <span className="text-sub">Bác sĩ chỉ định:</span>
            <span className="font-semibold text-navy">{context.doctorName}</span>
          </div>
          <div className="flex justify-between items-center py-1">
            <span className="text-sub flex items-center gap-1">
              <CreditCard className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
              Trạng thái thanh toán:
            </span>
            <Badge tone="emerald">
              {context.totalPaidFormatted} • {context.paymentMethodLabel}
            </Badge>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
