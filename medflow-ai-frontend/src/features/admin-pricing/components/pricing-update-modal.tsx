import { AlertCircle, CheckCircle2, DollarSign, TrendingDown, TrendingUp, X } from 'lucide-react';
import { useEffect, useState } from 'react';

import { Spinner } from '@/shared/ui';

import type {
  ServicePricingItem,
  ServicePricingStatus,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

interface PricingUpdateModalProps {
  item: ServicePricingItem | null;
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (id: string, payload: UpdateServicePricePayload) => Promise<void>;
  isSubmitting?: boolean;
}

export function PricingUpdateModal({
  item,
  isOpen,
  onClose,
  onSubmit,
  isSubmitting = false,
}: PricingUpdateModalProps) {
  if (!isOpen || !item) return null;

  return (
    <PricingUpdateModalContent
      key={item.id}
      item={item}
      onClose={onClose}
      onSubmit={onSubmit}
      isSubmitting={isSubmitting}
    />
  );
}

function PricingUpdateModalContent({
  item,
  onClose,
  onSubmit,
  isSubmitting = false,
}: Omit<PricingUpdateModalProps, 'isOpen'> & { item: ServicePricingItem }) {
  const [newPrice, setNewPrice] = useState<number>(item.currentPrice);
  const [effectiveDate, setEffectiveDate] = useState<string>(() =>
    new Date().toISOString().slice(0, 10),
  );
  const [decisionNumber, setDecisionNumber] = useState<string>('QĐ-145/BV-KH');
  const [appliedBy, setAppliedBy] = useState<string>('Hội đồng Giá Bệnh viện');
  const [status, setStatus] = useState<ServicePricingStatus>(item.status);
  const [note, setNote] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !isSubmitting) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isSubmitting, onClose]);

  const currentPrice = item.currentPrice;
  const priceDiff = newPrice - currentPrice;
  const percentDiff = currentPrice > 0 ? Math.round((priceDiff / currentPrice) * 100) : 0;

  const handleQuickPercent = (pct: number) => {
    const calculated = Math.round((currentPrice * (1 + pct / 100)) / 1000) * 1000;
    setNewPrice(calculated);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    if (newPrice <= 0) {
      setErrorMessage('Mức giá mới phải lớn hơn 0 VNĐ.');
      return;
    }

    if (!effectiveDate) {
      setErrorMessage('Vui lòng chọn ngày bắt đầu hiệu lực.');
      return;
    }

    if (!decisionNumber.trim()) {
      setErrorMessage('Vui lòng nhập số quyết định ban hành mức giá.');
      return;
    }

    try {
      await onSubmit(item.id, {
        newPrice,
        effectiveDate,
        decisionNumber: decisionNumber.trim(),
        appliedBy,
        status,
        note:
          note.trim() ||
          `Điều chỉnh biểu phí từ ${item.formattedPrice} sang ${newPrice.toLocaleString('vi-VN')} đ`,
      });
      onClose();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Đã xảy ra lỗi khi cập nhật mức giá.');
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="pricing-update-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-xl rounded-xl border border-border bg-white shadow-2xl animate-in fade-in zoom-in-95 duration-150">
        {/* Modal Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-2.5">
            <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
              <DollarSign className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-primary">
                  {item.code}
                </span>
                <span className="rounded bg-sky-50 px-1.5 py-0.5 text-[9px] font-bold text-primary">
                  UC52 / Issue #207
                </span>
              </div>
              <h2 id="pricing-update-title" className="text-sm font-bold text-ink">
                Cập Nhật Mức Giá & Quyết Định Áp Dụng
              </h2>
            </div>
          </div>
          <button
            type="button"
            disabled={isSubmitting}
            onClick={onClose}
            className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 transition-colors disabled:opacity-50"
            aria-label="Đóng hộp thoại cập nhật giá"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Modal Form */}
        <form onSubmit={handleSubmit}>
          <div className="max-h-[75vh] overflow-y-auto p-6 space-y-4">
            {/* Current Service Context Card */}
            <div className="rounded-xl border border-slate-200 bg-slate-50/80 p-3.5 text-xs">
              <div className="font-bold text-ink text-xs">{item.name}</div>
              <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sub text-[11px]">
                <span>
                  Khoa: <strong className="text-slate-800">{item.department}</strong>
                </span>
                <span>
                  Phân loại: <strong className="text-slate-800">{item.category}</strong>
                </span>
                <span>
                  Đơn giá hiện hành:{' '}
                  <strong className="text-emerald-700 font-bold">{item.formattedPrice}</strong>
                </span>
              </div>
              <div className="mt-1 text-[10px] text-slate-500 font-mono">
                QĐ hiện hành: {item.decisionNumber} · Áp dụng từ: {item.effectiveDate}
              </div>
            </div>

            {/* Error Message */}
            {errorMessage && (
              <div
                role="alert"
                className="flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800"
              >
                <AlertCircle className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
                <span>{errorMessage}</span>
              </div>
            )}

            {/* Field: New Price */}
            <div>
              <label htmlFor="new-price-input" className="block text-xs font-bold text-ink mb-1.5">
                Mức giá đề xuất mới (VNĐ) <span className="text-rose-500">*</span>
              </label>
              <div className="relative">
                <input
                  id="new-price-input"
                  type="number"
                  step={1000}
                  min={1000}
                  required
                  value={newPrice || ''}
                  onChange={(e) => setNewPrice(Number(e.target.value))}
                  placeholder="Nhập đơn giá mới..."
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 text-sm font-bold text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                />
                <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs font-semibold text-slate-400">
                  VNĐ
                </span>
              </div>

              {/* Price Delta Indicator & Quick Presets */}
              <div className="mt-2 flex flex-wrap items-center justify-between gap-2 text-xs">
                <div className="flex items-center gap-1.5 text-[11px]">
                  {priceDiff !== 0 ? (
                    percentDiff > 0 ? (
                      <span className="inline-flex items-center gap-0.5 font-bold text-rose-600">
                        <TrendingUp className="h-3.5 w-3.5" aria-hidden="true" />
                        Tăng +{percentDiff}% (+{priceDiff.toLocaleString('vi-VN')} đ)
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-0.5 font-bold text-emerald-600">
                        <TrendingDown className="h-3.5 w-3.5" aria-hidden="true" />
                        Giảm {percentDiff}% ({priceDiff.toLocaleString('vi-VN')} đ)
                      </span>
                    )
                  ) : (
                    <span className="text-slate-400">Chưa thay đổi so với giá hiện tại</span>
                  )}
                </div>

                {/* Quick percentage adjustment chips */}
                <div className="flex items-center gap-1">
                  <span className="text-[10px] text-slate-400">Điều chỉnh nhanh:</span>
                  {[5, 10, 15, 20].map((pct) => (
                    <button
                      key={pct}
                      type="button"
                      onClick={() => handleQuickPercent(pct)}
                      className="rounded border border-border bg-slate-50 px-1.5 py-0.5 text-[10px] font-semibold text-slate-600 hover:bg-slate-100 hover:text-ink"
                    >
                      +{pct}%
                    </button>
                  ))}
                </div>
              </div>
            </div>

            {/* Field: Effective Date & Decision Number Grid */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label
                  htmlFor="effective-date-input"
                  className="block text-xs font-bold text-ink mb-1.5"
                >
                  Ngày hiệu lực <span className="text-rose-500">*</span>
                </label>
                <div className="relative">
                  <input
                    id="effective-date-input"
                    type="date"
                    required
                    value={effectiveDate}
                    onChange={(e) => setEffectiveDate(e.target.value)}
                    className="h-9 w-full rounded-lg border border-border bg-white px-3 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                  />
                </div>
              </div>

              <div>
                <label
                  htmlFor="decision-number-input"
                  className="block text-xs font-bold text-ink mb-1.5"
                >
                  Số Quyết định ban hành <span className="text-rose-500">*</span>
                </label>
                <input
                  id="decision-number-input"
                  type="text"
                  required
                  value={decisionNumber}
                  onChange={(e) => setDecisionNumber(e.target.value)}
                  placeholder="Ví dụ: QĐ-145/BV-KH"
                  className="h-9 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                />
              </div>
            </div>

            {/* Field: Approving Authority & Status Grid */}
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label
                  htmlFor="applied-by-select"
                  className="block text-xs font-bold text-ink mb-1.5"
                >
                  Cơ quan / Cấp phê duyệt
                </label>
                <select
                  id="applied-by-select"
                  value={appliedBy}
                  onChange={(e) => setAppliedBy(e.target.value)}
                  className="h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                >
                  <option value="Hội đồng Giá Bệnh viện">Hội đồng Giá Bệnh viện</option>
                  <option value="Ban Giám đốc">Ban Giám đốc</option>
                  <option value="Hội đồng Quản lý Cơ sở">Hội đồng Quản lý Cơ sở</option>
                  <option value="Sở Y tế / Cơ quan chủ quản">Sở Y tế / Cơ quan chủ quản</option>
                </select>
              </div>

              <div>
                <label htmlFor="status-select" className="block text-xs font-bold text-ink mb-1.5">
                  Trạng thái sau cập nhật
                </label>
                <select
                  id="status-select"
                  value={status}
                  onChange={(e) => setStatus(e.target.value as ServicePricingStatus)}
                  className="h-9 w-full rounded-lg border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                >
                  <option value="ACTIVE">Đang áp dụng (Hiệu lực ngay)</option>
                  <option value="SUSPENDED">Tạm dừng niêm yết</option>
                </select>
              </div>
            </div>

            {/* Field: Note / Reason */}
            <div>
              <label htmlFor="note-textarea" className="block text-xs font-bold text-ink mb-1.5">
                Ghi chú điều chỉnh / Căn cứ thẩm định
              </label>
              <textarea
                id="note-textarea"
                rows={2}
                value={note}
                onChange={(e) => setNote(e.target.value)}
                placeholder="Nhập căn cứ thẩm định hoặc lý do biến động giá..."
                className="w-full rounded-lg border border-border bg-white p-2.5 text-xs text-ink focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
              />
            </div>

            {/* UC52 Legal Audit Notice */}
            <div className="flex items-start gap-2 rounded-lg border border-sky-100 bg-sky-50/70 p-3 text-[11px] text-sky-900">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-primary mt-0.5" aria-hidden="true" />
              <span>
                <strong>Kiểm toán viện phí UC52:</strong> Mức giá hiện tại sẽ được tự động lưu trữ
                vào dòng thời gian lịch sử giá để phục vụ công tác đối soát, thanh quyết toán BHYT
                và báo cáo tài chính.
              </span>
            </div>
          </div>

          {/* Modal Footer */}
          <div className="flex items-center justify-end gap-2.5 border-t border-border bg-slate-50 px-6 py-3.5 rounded-b-xl">
            <button
              type="button"
              disabled={isSubmitting}
              onClick={onClose}
              className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 transition-colors disabled:opacity-50"
            >
              Hủy bỏ
            </button>
            <button
              type="submit"
              disabled={isSubmitting || newPrice <= 0}
              className="inline-flex items-center gap-1.5 rounded-lg bg-emerald-600 px-4 py-2 text-xs font-semibold text-white shadow-xs hover:bg-emerald-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 disabled:opacity-50"
            >
              {isSubmitting ? (
                <>
                  <Spinner className="h-3.5 w-3.5 text-white" />
                  <span>Đang lưu...</span>
                </>
              ) : (
                <>
                  <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Xác nhận cập nhật giá</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
