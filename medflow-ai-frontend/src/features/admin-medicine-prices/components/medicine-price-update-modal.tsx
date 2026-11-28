import { zodResolver } from '@hookform/resolvers/zod';
import { AlertCircle, CheckCircle2, DollarSign, History, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';

import { FormField, formControlClass } from '@/shared/components/form/form-primitives';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Button } from '@/shared/ui/button';

import { useUpdateMedicinePriceMutation } from '../hooks/use-medicine-prices';
import {
  type UpdateMedicinePriceFormValues,
  updateMedicinePriceSchema,
} from '../schemas/medicine-price.schema';
import { useMedicinePriceStore } from '../store/medicine-price.store';

export function getHospitalLocalDateIso(now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(now);

  const year = parts.find((part) => part.type === 'year')?.value ?? '1970';
  const month = parts.find((part) => part.type === 'month')?.value ?? '01';
  const day = parts.find((part) => part.type === 'day')?.value ?? '01';

  return `${year}-${month}-${day}`;
}

export function MedicinePriceUpdateModal() {
  const medicine = useMedicinePriceStore((state) => state.updatePriceMedicine);
  const setUpdatePriceMedicine = useMedicinePriceStore((state) => state.setUpdatePriceMedicine);
  const setPriceHistoryMedicine = useMedicinePriceStore((state) => state.setPriceHistoryMedicine);
  const updateMutation = useUpdateMedicinePriceMutation();

  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<UpdateMedicinePriceFormValues>({
    resolver: zodResolver(updateMedicinePriceSchema),
    defaultValues: {
      price: medicine?.currentPriceAmount ?? medicine?.price ?? 0,
      decisionNumber: medicine?.decisionNumber ?? '',
      effectiveDate: getHospitalLocalDateIso(),
      validTo: medicine?.validTo ?? '',
      appliedBy: medicine?.appliedBy ?? 'Hội đồng Thuốc & Điều trị',
      note: '',
    },
  });

  useEffect(() => {
    if (medicine) {
      reset({
        price: medicine.currentPriceAmount ?? medicine.price,
        decisionNumber: medicine.decisionNumber ?? '',
        effectiveDate: getHospitalLocalDateIso(),
        validTo: medicine.validTo ?? '',
        appliedBy: medicine.appliedBy ?? 'Hội đồng Thuốc & Điều trị',
        note: `Điều chỉnh giá thuốc ${medicine.name} theo quyết định mới`,
      });
    }
  }, [medicine, reset]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setUpdatePriceMedicine(null);
      }
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('medicine-price-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [setUpdatePriceMedicine]);

  const watchedNote = useWatch({ control, name: 'note' }) ?? '';

  if (!medicine) return null;

  const onSubmit = async (values: UpdateMedicinePriceFormValues) => {
    setErrorMessage(null);
    setSuccessMessage(null);
    try {
      await updateMutation.mutateAsync({
        idOrCode: medicine.medicineCode || String(medicine.id),
        payload: {
          medicineId: medicine.id,
          medicineCode: medicine.medicineCode ?? undefined,
          price: values.price,
          priceAmount: values.price,
          effectiveDate: values.effectiveDate,
          validFrom: values.effectiveDate,
          validTo: values.validTo?.trim() || undefined,
          decisionNumber: values.decisionNumber,
          appliedBy: values.appliedBy,
          note: values.note,
        },
      });

      setSuccessMessage(
        `Cập nhật thành công giá thuốc: ${values.price.toLocaleString('vi-VN')} đ (QĐ: ${values.decisionNumber})`,
      );
      setTimeout(() => {
        setUpdatePriceMedicine(null);
      }, 700);
    } catch (err: unknown) {
      setErrorMessage(getErrorMessage(err, 'Có lỗi xảy ra khi cập nhật giá thuốc.'));
    }
  };

  const handleOpenHistory = () => {
    const current = medicine;
    setUpdatePriceMedicine(null);
    setPriceHistoryMedicine(current);
  };

  const medicineMeta = [
    medicine.activeIngredient,
    medicine.strength,
    medicine.dosageForm,
    medicine.dispensingUnit,
  ]
    .filter((value): value is string => Boolean(value))
    .join(' • ');

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="medicine-price-update-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4 backdrop-blur-xs animate-in fade-in duration-150"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) setUpdatePriceMedicine(null);
      }}
    >
      <div className="relative flex max-h-[calc(100vh-32px)] w-full max-w-xl flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-border bg-canvas px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <DollarSign className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="rounded bg-sky-100 px-2 py-0.5 font-mono text-[11px] font-bold text-primary">
                  {medicine.medicineCode || `#${medicine.id}`}
                </span>
                <span className="text-xs font-semibold text-sub">{medicine.facility}</span>
              </div>
              <h2 id="medicine-price-update-modal-title" className="text-base font-bold text-ink">
                Cập nhật giá thuốc
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={() => setUpdatePriceMedicine(null)}
            className="rounded-lg p-1.5 text-sub transition-colors hover:bg-canvas hover:text-ink"
            aria-label="Đóng modal cập nhật giá thuốc"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>

        <form
          id="medicine-price-form"
          noValidate
          onSubmit={handleSubmit(onSubmit)}
          className="flex-1 space-y-4 overflow-y-auto p-6"
        >
          <div className="rounded-xl border border-border bg-canvas p-4">
            <div className="text-sm font-bold leading-snug text-ink sm:text-base">
              {medicine.name}
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
              {medicineMeta && <span>{medicineMeta}</span>}
              <span>
                Giá hiện hành:{' '}
                <strong className="font-bold text-emerald-700">
                  {medicine.hasEffectivePrice ? medicine.priceFormatted : 'Chưa có giá'}
                </strong>
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

          <FormField
            label="Mức giá niêm yết mới (VNĐ)"
            required
            hint="Đơn giá thuốc áp dụng tại cơ sở (cho phép 0 đ với thuốc miễn phí)."
            error={errors.price?.message}
          >
            <div className="relative mt-1">
              <input
                id="medicine-price-input"
                type="number"
                step="1000"
                min="0"
                className={`${formControlClass} pr-14 font-mono text-base font-bold text-ink focus:border-primary`}
                placeholder="Nhập đơn giá mới..."
                {...register('price', { valueAsNumber: true })}
              />
              <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs font-bold text-sub">
                VNĐ
              </span>
            </div>
          </FormField>

          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              label="Số Quyết định / Căn cứ pháp lý"
              required
              hint="VD: QĐ-145/BV-KH, QĐ-2026/SYT"
              error={errors.decisionNumber?.message}
            >
              <input
                id="medicine-decision-input"
                className={`${formControlClass} font-mono`}
                placeholder="VD: QĐ-145/BV-KH"
                {...register('decisionNumber')}
              />
            </FormField>

            <FormField
              label="Cấp / Người phê duyệt giá"
              required
              hint="Đơn vị thẩm định và ban hành quyết định giá"
              error={errors.appliedBy?.message}
            >
              <input
                id="medicine-applied-by-input"
                className={formControlClass}
                placeholder="VD: Hội đồng Thuốc & Điều trị..."
                {...register('appliedBy')}
              />
            </FormField>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              label="Ngày bắt đầu có hiệu lực"
              required
              hint="Ngày bắt đầu áp dụng giá mới"
              error={errors.effectiveDate?.message}
            >
              <input
                id="medicine-effective-date-input"
                type="date"
                className={formControlClass}
                {...register('effectiveDate')}
              />
            </FormField>

            <FormField
              label="Ngày kết thúc hiệu lực (Tùy chọn)"
              hint="Để trống nếu áp dụng cho tới khi có quyết định mới"
              error={errors.validTo?.message}
            >
              <input
                id="medicine-valid-to-input"
                type="date"
                className={formControlClass}
                {...register('validTo')}
              />
            </FormField>
          </div>

          <FormField
            label="Lý do & Căn cứ điều chỉnh giá"
            hint="Tóm tắt lý do điều chỉnh theo chính sách bệnh viện (tối đa 500 ký tự)."
            error={errors.note?.message}
          >
            <textarea
              id="medicine-price-note-input"
              rows={2}
              maxLength={500}
              className={formControlClass}
              placeholder="Mô tả căn cứ chi phí, đấu thầu hoặc chính sách giá thuốc..."
              {...register('note')}
            />
          </FormField>
          <div className="text-right text-[10px] text-sub">{watchedNote.length} / 500 ký tự</div>

          <div className="flex items-start gap-2.5 rounded-lg border border-sky-200 bg-sky-50/70 p-3 text-[11px] leading-relaxed text-sky-900">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <div>
              <strong>Quy tắc lưu vết:</strong> Bản ghi giá cũ đang mở sẽ được đóng hiệu lực tự động
              và ghi nhận nhật ký kiểm toán hệ thống.
            </div>
          </div>
        </form>

        <div className="flex items-center justify-between border-t border-border bg-canvas px-6 py-3.5">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setUpdatePriceMedicine(null)}
          >
            Hủy bỏ [Esc]
          </Button>

          <Button
            id="medicine-price-submit"
            type="submit"
            form="medicine-price-form"
            size="sm"
            disabled={updateMutation.isPending}
            className="gap-2"
          >
            {updateMutation.isPending ? (
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
