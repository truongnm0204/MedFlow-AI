import { ClipboardList } from 'lucide-react';

interface BookingReasonSectionProps {
  symptomNote: string;
  onChangeSymptomNote: (value: string) => void;
  error?: string | null;
}

export function BookingReasonSection({
  symptomNote,
  onChangeSymptomNote,
  error,
}: BookingReasonSectionProps) {
  return (
    <section
      aria-labelledby="section-reason-heading"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="mb-4 flex items-start gap-3">
        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
          <ClipboardList className="h-4 w-4" aria-hidden="true" />
        </div>
        <div>
          <h2 id="section-reason-heading" className="text-sm font-bold text-ink sm:text-base">
            4. Lý do khám / Triệu chứng ban đầu
          </h2>
          <p className="text-xs text-sub">Mô tả ngắn lý do khám và các triệu chứng hiện tại.</p>
        </div>
      </div>

      <div className="space-y-2">
        <div className="flex items-center gap-1">
          <label htmlFor="booking-symptom-note" className="block text-xs font-bold text-ink">
            Lý do khám / Triệu chứng ban đầu
          </label>
          <span className="text-xs font-bold text-danger" aria-hidden="true">
            *
          </span>
        </div>
        <textarea
          id="booking-symptom-note"
          rows={3}
          required
          aria-required="true"
          aria-invalid={Boolean(error)}
          maxLength={500}
          value={symptomNote}
          onChange={(e) => onChangeSymptomNote(e.target.value)}
          placeholder="Nhập lý do khám hoặc triệu chứng bạn đang gặp (bắt buộc)..."
          className={`w-full rounded-xl border bg-canvas/60 px-4 py-3 text-xs text-ink placeholder:text-sub/70 focus:bg-white focus:outline-none focus:ring-2 sm:text-sm ${
            error
              ? 'border-danger focus:border-danger focus:ring-danger/20'
              : 'border-border focus:border-primary focus:ring-primary/20'
          }`}
        />
        {error ? (
          <p role="alert" className="text-xs font-semibold text-danger">
            {error}
          </p>
        ) : (
          <p className="text-[11px] text-sub">
            Vui lòng nhập triệu chứng hoặc lý do khám (tối thiểu 3 ký tự) trước khi tiếp tục.
          </p>
        )}
      </div>
    </section>
  );
}
