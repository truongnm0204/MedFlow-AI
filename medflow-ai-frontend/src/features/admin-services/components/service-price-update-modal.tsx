import {
  AlertCircle,
  CheckCircle2,
  DollarSign,
  TrendingDown,
  TrendingUp,
  X,
} from 'lucide-react';
import { useEffect, useState } from 'react';

import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ExaminationService, ServiceStatus } from '../types/examination-service.types';

interface ServicePriceUpdateModalProps {
  service: ExaminationService | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: (updated: ExaminationService) => void;
}

export function ServicePriceUpdateModal({
  service,
  isOpen,
  onClose,
  onSuccess,
}: ServicePriceUpdateModalProps) {
  if (!isOpen || !service) return null;

  return (
    <ServicePriceUpdateModalContent
      key={service.id}
      service={service}
      onClose={onClose}
      onSuccess={onSuccess}
    />
  );
}

function ServicePriceUpdateModalContent({
  service,
  onClose,
  onSuccess,
}: Omit<ServicePriceUpdateModalProps, 'isOpen'> & { service: ExaminationService }) {
  const updateService = useExaminationServiceStore((state) => state.updateService);

  const [newPrice, setNewPrice] = useState<number>(service.price);
  const [effectiveDate, setEffectiveDate] = useState<string>(() =>
    new Date().toISOString().slice(0, 10),
  );
  const [decisionNumber, setDecisionNumber] = useState<string>('QĐ-145/BV-KH');
  const [appliedBy, setAppliedBy] = useState<string>('Hội đồng Giá Bệnh viện');
  const [status, setStatus] = useState<ServiceStatus>(service.status);
  const [note, setNote] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const currentPrice = service.price;
  const priceDiff = newPrice - currentPrice;
  const percentDiff =
    currentPrice > 0 ? Math.round((priceDiff / currentPrice) * 100) : 0;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (newPrice <= 0) {
      setErrorMessage('Mức giá mới phải lớn hơn 0 VNĐ.');
      return;
    }
    if (!decisionNumber.trim()) {
      setErrorMessage('Vui lòng nhập số quyết định ban hành mức giá.');
      return;
    }

    const newHistoryEntry = {
      date: effectiveDate,
      price: newPrice,
      decisionNumber: decisionNumber.trim(),
      note: note.trim() || `Cập nhật biểu phí sang ${newPrice.toLocaleString('vi-VN')} đ`,
      appliedBy,
    };

    const updatedData: Partial<ExaminationService> = {
      price: newPrice,
      effectiveDate,
      decisionNumber: decisionNumber.trim(),
      status,
      priceHistory: [newHistoryEntry, ...(service.priceHistory || [])],
    };

    updateService(service.id, updatedData);
    onSuccess?.({ ...service, ...updatedData });
    onClose();
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="price-update-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-lg rounded-xl border border-border bg-white shadow-xl animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between border-b border-border px-5 py-3.5">
          <div className="flex items-center gap-2">
            <DollarSign className="h-5 w-5 text-emerald-600" aria-hidden="true" />
            <div>
              <span className="font-mono text-[10px] font-bold text-primary">{service.code}</span>
              <h2 id="price-update-title" className="text-sm font-bold text-ink">
                Cập Nhật Mức Giá & Quyết Định Áp Dụng (UC52)
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại cập nhật giá"
            className="rounded p-1 text-slate-400 hover:text-slate-600"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-5 space-y-3.5 text-xs">
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
            <div className="font-bold text-ink">{service.name}</div>
            <div className="mt-1 flex gap-4 text-sub text-[11px]">
              <span>Khoa: <b>{service.department}</b></span>
              <span>Giá hiện tại: <b className="text-emerald-700">{service.price.toLocaleString('vi-VN')} đ</b></span>
            </div>
          </div>

          {errorMessage && (
            <div className="flex items-center gap-1.5 rounded-lg border border-rose-200 bg-rose-50 p-2.5 text-rose-800 text-xs">
              <AlertCircle className="h-4 w-4 shrink-0 text-rose-600" />
              <span>{errorMessage}</span>
            </div>
          )}

          <div>
            <label htmlFor="new-price-input" className="block font-bold text-ink mb-1">
              Mức giá đề xuất mới (VNĐ) *
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
                className="h-9 w-full rounded-lg border border-border bg-white px-3 font-bold text-ink focus:border-primary focus:outline-none"
              />
              <span className="absolute right-3 top-1/2 -translate-y-1/2 text-[11px] text-slate-400 font-semibold">
                VNĐ
              </span>
            </div>

            <div className="mt-1.5 flex items-center justify-between text-[11px]">
              {priceDiff !== 0 ? (
                percentDiff > 0 ? (
                  <span className="flex items-center gap-0.5 font-bold text-rose-600">
                    <TrendingUp className="h-3 w-3" /> Tăng +{percentDiff}% (+{priceDiff.toLocaleString('vi-VN')} đ)
                  </span>
                ) : (
                  <span className="flex items-center gap-0.5 font-bold text-emerald-600">
                    <TrendingDown className="h-3 w-3" /> Giảm {percentDiff}% ({priceDiff.toLocaleString('vi-VN')} đ)
                  </span>
                )
              ) : (
                <span className="text-slate-400">Chưa thay đổi so với giá hiện tại</span>
              )}
              <div className="flex items-center gap-1">
                {[5, 10, 15, 20].map((pct) => (
                  <button
                    key={pct}
                    type="button"
                    onClick={() => setNewPrice(Math.round((currentPrice * (1 + pct / 100)) / 1000) * 1000)}
                    className="rounded border border-border bg-slate-50 px-1.5 py-0.5 text-[10px] font-semibold text-slate-600 hover:bg-slate-100"
                  >
                    +{pct}%
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="effective-date-input" className="block font-bold text-ink mb-1">
                Ngày hiệu lực *
              </label>
              <input
                id="effective-date-input"
                type="date"
                required
                value={effectiveDate}
                onChange={(e) => setEffectiveDate(e.target.value)}
                className="h-8 w-full rounded-md border border-border bg-white px-2.5 text-xs text-ink focus:border-primary focus:outline-none"
              />
            </div>
            <div>
              <label htmlFor="decision-number-input" className="block font-bold text-ink mb-1">
                Số Quyết định ban hành *
              </label>
              <input
                id="decision-number-input"
                type="text"
                required
                value={decisionNumber}
                onChange={(e) => setDecisionNumber(e.target.value)}
                placeholder="Ví dụ: QĐ-145/BV-KH"
                className="h-8 w-full rounded-md border border-border bg-white px-2.5 font-mono text-xs text-ink focus:border-primary focus:outline-none"
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label htmlFor="applied-by-select" className="block font-bold text-ink mb-1">
                Cấp phê duyệt
              </label>
              <select
                id="applied-by-select"
                value={appliedBy}
                onChange={(e) => setAppliedBy(e.target.value)}
                className="h-8 w-full rounded-md border border-border bg-white px-2 text-xs text-ink focus:border-primary focus:outline-none"
              >
                <option value="Hội đồng Giá Bệnh viện">Hội đồng Giá Bệnh viện</option>
                <option value="Ban Giám đốc">Ban Giám đốc</option>
                <option value="Hội đồng Quản lý Cơ sở">Hội đồng Quản lý Cơ sở</option>
              </select>
            </div>
            <div>
              <label htmlFor="status-select" className="block font-bold text-ink mb-1">
                Trạng thái áp dụng
              </label>
              <select
                id="status-select"
                value={status}
                onChange={(e) => setStatus(e.target.value as ServiceStatus)}
                className="h-8 w-full rounded-md border border-border bg-white px-2 text-xs text-ink focus:border-primary focus:outline-none"
              >
                <option value="ACTIVE">Đang áp dụng</option>
                <option value="SUSPENDED">Tạm dừng niêm yết</option>
              </select>
            </div>
          </div>

          <div>
            <label htmlFor="note-textarea" className="block font-bold text-ink mb-1">
              Ghi chú điều chỉnh / Căn cứ
            </label>
            <textarea
              id="note-textarea"
              rows={2}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Nhập lý do điều chỉnh hoặc căn cứ thẩm định..."
              className="w-full rounded-md border border-border bg-white p-2 text-xs text-ink focus:border-primary focus:outline-none"
            />
          </div>

          <div className="flex justify-end gap-2 border-t border-border pt-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
            >
              Hủy bỏ
            </button>
            <button
              type="submit"
              disabled={newPrice <= 0}
              className="inline-flex items-center gap-1.5 rounded-lg bg-emerald-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700 disabled:opacity-50"
            >
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Xác nhận cập nhật giá</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
