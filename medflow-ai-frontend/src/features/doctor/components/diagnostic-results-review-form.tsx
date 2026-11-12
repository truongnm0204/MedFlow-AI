import { CheckCircle2 } from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Card, CardContent } from '@/shared/ui/card';

import {
  type ReviewDiagnosticResultsInput,
  reviewDiagnosticResultsSchema,
} from '../schemas/review-diagnostic-results.schema';

export interface DiagnosticResultsReviewFormProps {
  readyOrderIds: number[];
  isSubmitting: boolean;
  onSubmit: (input: ReviewDiagnosticResultsInput) => Promise<boolean>;
}

export function DiagnosticResultsReviewForm({
  readyOrderIds,
  isSubmitting,
  onSubmit,
}: DiagnosticResultsReviewFormProps) {
  const [value, setValue] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = reviewDiagnosticResultsSchema.safeParse({
      orderIds: readyOrderIds,
      clinicalInterpretation: value,
    });
    if (!parsed.success) {
      setValidationError(
        parsed.error.issues[0]?.message ?? 'Dữ liệu xác nhận xem xét không hợp lệ.',
      );
      return;
    }

    setValidationError(null);
    const accepted = await onSubmit(parsed.data);
    if (accepted) setValue('');
  };

  return (
    <Card>
      <CardContent>
        <form onSubmit={(event) => void handleSubmit(event)} className="space-y-3">
          <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
            XÁC NHẬN ĐÃ XEM XÉT KẾT QUẢ CLS
          </h2>
          <div className="flex flex-col gap-2">
            <label
              htmlFor="diagnostic-review-interpretation"
              className="text-xs font-semibold text-navy"
            >
              Nhận định kết quả cận lâm sàng (không bắt buộc)
            </label>
            <textarea
              id="diagnostic-review-interpretation"
              rows={4}
              value={value}
              onChange={(event) => setValue(event.target.value)}
              className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm placeholder:text-sub focus:outline-none focus:ring-2 focus:ring-primary/40"
            />
            <p className="text-right text-[11px] text-sub">{value.length}/2000</p>
          </div>
          {validationError ? (
            <p
              role="alert"
              data-testid="diagnostic-review-validation-error"
              className="text-xs text-rose-700"
            >
              {validationError}
            </p>
          ) : null}
          <button
            type="submit"
            disabled={isSubmitting}
            className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark disabled:opacity-50"
          >
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>
              {isSubmitting
                ? 'Đang ghi nhận...'
                : `Xác nhận đã xem xét ${readyOrderIds.length} kết quả`}
            </span>
          </button>
        </form>
      </CardContent>
    </Card>
  );
}
