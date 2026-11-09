import { zodResolver } from '@hookform/resolvers/zod';
import {
  AlertCircle,
  CheckCircle2,
  DollarSign,
  History,
  TrendingDown,
  TrendingUp,
  X,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';

import { FormField, formControlClass } from '@/shared/components/form/form-primitives';
import { Button } from '@/shared/ui/button';

import { updateServicePriceApi } from '../api/examination-service.api';
import {
  type UpdateServicePriceFormValues,
  updateServicePriceSchema,
} from '../schemas/service-price.schema';
import { useExaminationServiceStore } from '../store/examination-service.store';

function getTodayDateString(): string {
  return new Date().toISOString().split('T')[0];
}

export function ServicePriceUpdateModal() {
  const service = useExaminationServiceStore((state) => state.updatePriceService);
  const setUpdatePriceService = useExaminationServiceStore((state) => state.setUpdatePriceService);
  const setPriceHistoryService = useExaminationServiceStore(
    (state) => state.setPriceHistoryService,
  );
  const updateServicePrice = useExaminationServiceStore((state) => state.updateServicePrice);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setValue,
    control,
    reset,
    formState: { errors },
  } = useForm<UpdateServicePriceFormValues>({
    resolver: zodResolver(updateServicePriceSchema),
    defaultValues: {
      price: service?.price ?? 0,
      decisionNumber: service?.decisionNumber ?? 'QĐ-145/BV-KH',
      effectiveDate: getTodayDateString(),
      appliedBy: 'Hội đồng Giá Bệnh viện',
      note: 'Điều chỉnh định mức chi phí dịch vụ theo quyết định mới',
    },
  });

  useEffect(() => {
    if (service) {
      reset({
        price: service.price,
        decisionNumber: service.decisionNumber ?? 'QĐ-145/BV-KH',
        effectiveDate: getTodayDateString(),
        appliedBy: 'Hội đồng Giá Bệnh viện',
        note: `Điều chỉnh giá dịch vụ ${service.name} theo quyết định mới`,
      });
    }
  }, [service, reset]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setUpdatePriceService(null);
      }
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('service-price-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [setUpdatePriceService]);

  const watchedPrice = useWatch({ control, name: 'price' });
  const watchedNote = useWatch({ control, name: 'note' }) ?? '';

  if (!service) return null;

  const currentPrice = service.price;
  const numericNewPrice = Number(watchedPrice) || 0;
  const diffAmount = numericNewPrice - currentPrice;
  const diffPercent = currentPrice > 0 ? ((diffAmount / currentPrice) * 100).toFixed(1) : '0';

  const formatVnd = (num: number) => num.toLocaleString('vi-VN') + ' đ';

  const onSubmit = async (values: UpdateServicePriceFormValues) => {
    setIsSubmitting(true);
    setErrorMessage(null);
    try {
      await updateServicePriceApi(service.id, {
        price: values.price,
        decisionNumber: values.decisionNumber,
        effectiveDate: values.effectiveDate,
        appliedBy: values.appliedBy,
        note: values.note,
      });

      updateServicePrice(service.id, {
        price: values.price,
        decisionNumber: values.decisionNumber,
        effectiveDate: values.effectiveDate,
        appliedBy: values.appliedBy,
        note: values.note,
      });

      setSuccessMessage(
        `Cập nhật thành công biểu phí dịch vụ: ${formatVnd(values.price)} (QĐ: ${values.decisionNumber})`,
      );
      setTimeout(() => {
        setUpdatePriceService(null);
      }, 700);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Có lỗi xảy ra khi cập nhật giá dịch vụ.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleOpenHistory = () => {
    const s = service;
    setUpdatePriceService(null);
    setPriceHistoryService(s);
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="price-update-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4 backdrop-blur-xs animate-in fade-in duration-150"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) setUpdatePriceService(null);
      }}
    >
      <div className="relative w-full max-w-xl rounded-2xl border border-border bg-white shadow-2xl overflow-hidden flex flex-col max-h-[calc(100vh-32px)]">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border bg-slate-50/80 px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <DollarSign className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="rounded bg-sky-100 px-2 py-0.5 font-mono text-[11px] font-bold text-primary">
                  {service.code}
                </span>
                <span className="text-xs font-semibold text-sub">{service.category}</span>
              </div>
              <h2 id="price-update-modal-title" className="text-base font-bold text-ink">
                Cập nhật Biểu phí Dịch vụ (UC52)
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={() => setUpdatePriceService(null)}
            className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600 transition-colors"
            aria-label="Đóng modal cập nhật giá"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>

        {/* Content & Form */}
        <form
          id="service-price-form"
          onSubmit={handleSubmit(onSubmit)}
          className="flex-1 overflow-y-auto p-6 space-y-4"
        >
          {/* Current service summary card */}
          <div className="rounded-xl border border-border bg-slate-50/70 p-4">
            <div className="font-bold text-ink text-sm sm:text-base leading-snug">
              {service.name}
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
              <span>
                Khoa: <strong className="text-slate-700">{service.department}</strong>
              </span>
              <span>
                Mức giá hiện hành:{' '}
                <strong className="text-emerald-700 font-bold">{formatVnd(service.price)}</strong>
              </span>
              <button
                type="button"
                onClick={handleOpenHistory}
                className="inline-flex items-center gap-1 text-[11px] font-medium text-primary hover:underline"
              >
                <History className="h-3 w-3" aria-hidden="true" />
                Xem lịch sử giá
              </button>
            </div>
          </div>

          {/* Feedback messages */}
          {successMessage && (
            <div
              role="status"
              className="flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs text-emerald-800"
            >
              <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
              <span>{successMessage}</span>
            </div>
          )}

          {errorMessage && (
            <div
              role="alert"
              className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-xs text-red-800"
            >
              <AlertCircle className="h-4 w-4 shrink-0 text-red-600" aria-hidden="true" />
              <span>{errorMessage}</span>
            </div>
          )}

          {/* Price Input & Live Calculation */}
          <div className="rounded-xl border border-sky-100 bg-sky-50/50 p-4 space-y-3">
            <FormField
              label="Mức giá niêm yết mới (VNĐ)"
              required
              hint="Đơn giá thanh toán ngoại trú niêm yết áp dụng cho dịch vụ."
              error={errors.price?.message}
            >
              <div className="relative mt-1">
                <input
                  id="service-price-input"
                  type="number"
                  step="1000"
                  min="0"
                  className={`${formControlClass} pr-14 font-mono text-base font-bold text-ink focus:border-primary`}
                  placeholder="Nhập đơn giá mới..."
                  {...register('price', { valueAsNumber: true })}
                />
                <span className="absolute inset-y-0 right-3 flex items-center font-bold text-xs text-sub pointer-events-none">
                  VNĐ
                </span>
              </div>
            </FormField>

            {/* Live Difference Badge & Presets */}
            <div className="flex flex-wrap items-center justify-between gap-2 pt-1 border-t border-sky-100">
              <div className="flex items-center gap-1.5 text-xs">
                <span className="text-sub font-medium">Chênh lệch so với giá cũ:</span>
                {diffAmount > 0 ? (
                  <span className="inline-flex items-center gap-1 rounded-md bg-emerald-100 px-2 py-0.5 font-semibold text-emerald-800 text-[11px]">
                    <TrendingUp className="h-3 w-3" aria-hidden="true" />+{formatVnd(diffAmount)} (+
                    {diffPercent}%)
                  </span>
                ) : diffAmount < 0 ? (
                  <span className="inline-flex items-center gap-1 rounded-md bg-amber-100 px-2 py-0.5 font-semibold text-amber-800 text-[11px]">
                    <TrendingDown className="h-3 w-3" aria-hidden="true" />
                    {formatVnd(diffAmount)} ({diffPercent}%)
                  </span>
                ) : (
                  <span className="rounded-md bg-slate-200 px-2 py-0.5 font-medium text-slate-700 text-[11px]">
                    Không thay đổi (0%)
                  </span>
                )}
              </div>

              {/* Quick Preset Buttons */}
              <div className="flex items-center gap-1">
                <span className="text-[10px] text-slate-400">Gợi ý:</span>
                <button
                  type="button"
                  onClick={() =>
                    setValue('price', Math.round(currentPrice * 1.05), {
                      shouldValidate: true,
                      shouldDirty: true,
                    })
                  }
                  className="rounded border border-sky-200 bg-white px-2 py-0.5 text-[10px] font-semibold text-primary hover:bg-sky-50 transition-colors"
                >
                  +5%
                </button>
                <button
                  type="button"
                  onClick={() =>
                    setValue('price', Math.round(currentPrice * 1.1), {
                      shouldValidate: true,
                      shouldDirty: true,
                    })
                  }
                  className="rounded border border-sky-200 bg-white px-2 py-0.5 text-[10px] font-semibold text-primary hover:bg-sky-50 transition-colors"
                >
                  +10%
                </button>
                <button
                  type="button"
                  onClick={() =>
                    setValue('price', currentPrice + 50000, {
                      shouldValidate: true,
                      shouldDirty: true,
                    })
                  }
                  className="rounded border border-sky-200 bg-white px-2 py-0.5 text-[10px] font-semibold text-primary hover:bg-sky-50 transition-colors"
                >
                  +50k
                </button>
                <button
                  type="button"
                  onClick={() =>
                    setValue('price', currentPrice + 100000, {
                      shouldValidate: true,
                      shouldDirty: true,
                    })
                  }
                  className="rounded border border-sky-200 bg-white px-2 py-0.5 text-[10px] font-semibold text-primary hover:bg-sky-50 transition-colors"
                >
                  +100k
                </button>
              </div>
            </div>
          </div>

          {/* Decision Number and Effective Date Grid */}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              label="Số Quyết định / Căn cứ pháp lý"
              required
              hint="VD: QĐ-145/BV-KH, QĐ-2026/SYT"
              error={errors.decisionNumber?.message}
            >
              <input
                id="service-decision-input"
                className={`${formControlClass} font-mono`}
                placeholder="VD: QĐ-145/BV-KH"
                {...register('decisionNumber')}
              />
            </FormField>

            <FormField
              label="Ngày bắt đầu có hiệu lực"
              required
              hint="Ngày bắt đầu áp dụng biểu phí mới"
              error={errors.effectiveDate?.message}
            >
              <input
                id="service-effective-date-input"
                type="date"
                className={formControlClass}
                {...register('effectiveDate')}
              />
            </FormField>
          </div>

          {/* Approved By */}
          <FormField
            label="Cấp / Người phê duyệt biểu phí"
            required
            hint="Đơn vị thẩm định và ban hành quyết định giá"
            error={errors.appliedBy?.message}
          >
            <input
              id="service-applied-by-input"
              className={formControlClass}
              placeholder="VD: Hội đồng Giá Bệnh viện, Ban Giám đốc..."
              {...register('appliedBy')}
            />
          </FormField>

          {/* Reason / Note */}
          <FormField
            label="Lý do & Căn cứ điều chỉnh giá"
            hint="Tóm tắt lý do điều chỉnh theo chính sách bệnh viện (tối đa 500 ký tự)."
            error={errors.note?.message}
          >
            <textarea
              id="service-price-note-input"
              rows={2}
              maxLength={500}
              className={formControlClass}
              placeholder="Mô tả căn cứ chi phí, định mức kỹ thuật hoặc chính sách viện phí..."
              {...register('note')}
            />
          </FormField>
          <div className="text-right text-[10px] text-slate-400">
            {watchedNote.length} / 500 ký tự
          </div>

          {/* Policy compliance notice */}
          <div className="flex items-start gap-2.5 rounded-lg border border-sky-200 bg-sky-50/70 p-3 text-[11px] text-sky-900 leading-relaxed">
            <AlertCircle className="h-4 w-4 shrink-0 text-primary mt-0.5" aria-hidden="true" />
            <div>
              <strong>Quy tắc lưu vết UC52:</strong> Mức giá mới sẽ có hiệu lực kể từ ngày đã chọn.
              Mọi thay đổi biểu phí được ghi nhận tự động vào lịch sử giá dịch vụ và nhật ký kiểm
              toán hệ thống (Audit Log).
            </div>
          </div>
        </form>

        {/* Footer actions */}
        <div className="flex items-center justify-between border-t border-border bg-slate-50 px-6 py-3.5">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setUpdatePriceService(null)}
          >
            Hủy bỏ [Esc]
          </Button>

          <Button
            id="service-price-submit"
            type="submit"
            form="service-price-form"
            size="sm"
            disabled={isSubmitting}
            className="gap-2"
          >
            {isSubmitting ? (
              <span>Đang lưu...</span>
            ) : (
              <>
                <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                <span>Xác nhận & Lưu giá mới [Ctrl+S]</span>
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  );
}
