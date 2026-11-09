import { AlertCircle, CreditCard, QrCode } from 'lucide-react';

import type { PaymentMethod, WalkInService } from '../types/walk-in-visit.types';

interface WalkInPaymentSectionProps {
  paymentMethod: PaymentMethod;
  onPaymentMethodChange: (method: PaymentMethod) => void;
  service: WalkInService | null;
}

export function WalkInPaymentSection({
  paymentMethod,
  onPaymentMethodChange,
  service,
}: WalkInPaymentSectionProps) {
  const priceDisplay = service ? service.priceFormatted : '250.000 VNĐ';

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-5">
      {/* Policy banner (Filter-Slot Protocol) */}
      <div className="p-4 rounded-xl border border-amber-200 bg-amber-50/80 text-xs text-amber-900 flex items-start gap-3">
        <AlertCircle className="w-4 h-4 text-amber-700 shrink-0 mt-0.5" aria-hidden="true" />
        <div className="space-y-1">
          <span className="font-bold">
            Chính sách điều phối vãng lai MedFlow (Filter-Slot Protocol):
          </span>
          <p className="leading-relaxed opacity-90">
            Lượt khám vãng lai sẽ được tự động xếp vào các slot đệm dự phòng còn trống hoặc xen kẽ
            giữa các lượt hẹn, đảm bảo không làm gián đoạn thời gian khám của bệnh nhân đã đặt
            trước.
          </p>
        </div>
      </div>

      {/* Payment method selection */}
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-xs font-bold text-slate-800">
            Hình thức thu phí khám ban đầu ({priceDisplay})
          </label>
          <span className="text-[11px] text-slate-500 font-medium">100% Khám tự nguyện</span>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
          {/* VietQR option */}
          <button
            type="button"
            onClick={() => onPaymentMethodChange('VIETQR')}
            className={`p-4 rounded-xl border text-left transition-all flex items-start gap-3.5 ${
              paymentMethod === 'VIETQR'
                ? 'border-primary bg-sky-50/60 ring-2 ring-primary shadow-xs'
                : 'border-slate-200 bg-white hover:border-slate-300'
            }`}
          >
            <div className="w-10 h-10 rounded-xl bg-sky-100 text-sky-700 border border-sky-200 flex items-center justify-center shrink-0">
              <QrCode className="w-5 h-5" aria-hidden="true" />
            </div>
            <div>
              <div className="text-xs font-bold text-slate-900 flex items-center gap-1.5">
                <span>Quét mã VietQR tại quầy lễ tân</span>
                <span className="text-[10px] bg-emerald-100 text-emerald-800 px-1.5 py-0.5 rounded font-semibold">
                  Đề xuất
                </span>
              </div>
              <p className="text-[11px] text-slate-500 mt-0.5">
                Bệnh nhân quét mã thanh toán tức thì tại màn hình quầy
              </p>
            </div>
          </button>

          {/* Cashier desk option */}
          <button
            type="button"
            onClick={() => onPaymentMethodChange('CASHIER')}
            className={`p-4 rounded-xl border text-left transition-all flex items-start gap-3.5 ${
              paymentMethod === 'CASHIER'
                ? 'border-primary bg-sky-50/60 ring-2 ring-primary shadow-xs'
                : 'border-slate-200 bg-white hover:border-slate-300'
            }`}
          >
            <div className="w-10 h-10 rounded-xl bg-slate-100 text-slate-700 border border-slate-200 flex items-center justify-center shrink-0">
              <CreditCard className="w-5 h-5" aria-hidden="true" />
            </div>
            <div>
              <div className="text-xs font-bold text-slate-900">
                Chuyển sang Quầy thu ngân tầng 1
              </div>
              <p className="text-[11px] text-slate-500 mt-0.5">
                Thanh toán tiền mặt, thẻ POS hoặc bảo lãnh tại quầy tài chính
              </p>
            </div>
          </button>
        </div>
      </div>
    </div>
  );
}
