import { useState } from 'react';
import { CheckCircle2, Edit3, FileText, UserCheck } from 'lucide-react';

import { reviewSymptomNoteSchema } from '../schemas/review-booking.schema';
import type { ReviewPatientEhrInfo } from '../types/review-booking.types';

interface ReviewPatientInfoCardProps {
  patientInfo: ReviewPatientEhrInfo;
  symptomNote: string;
  onUpdateSymptomNote: (nextNote: string) => void;
}

export function ReviewPatientInfoCard({
  patientInfo,
  symptomNote,
  onUpdateSymptomNote,
}: ReviewPatientInfoCardProps) {
  const [isEditingNote, setIsEditingNote] = useState(false);
  const [draftNote, setDraftNote] = useState(symptomNote);
  const [noteError, setNoteError] = useState<string | null>(null);

  const handleOpenEdit = () => {
    setDraftNote(symptomNote);
    setNoteError(null);
    setIsEditingNote(true);
  };

  const handleCancelEdit = () => {
    setDraftNote(symptomNote);
    setNoteError(null);
    setIsEditingNote(false);
  };

  const handleSaveEdit = () => {
    const parsed = reviewSymptomNoteSchema.safeParse({ symptomNote: draftNote });
    if (!parsed.success) {
      setNoteError(parsed.error.issues[0]?.message ?? 'Lý do khám không hợp lệ.');
      return;
    }

    onUpdateSymptomNote(parsed.data.symptomNote);
    setNoteError(null);
    setIsEditingNote(false);
  };

  return (
    <section
      aria-label="1. Thông tin người bệnh"
      className="rounded-2xl border border-border bg-white p-6 shadow-xs sm:p-7"
    >
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <div className="flex items-center gap-2.5">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-sky-100 bg-sky-50 text-primary">
            <UserCheck className="h-4 w-4" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-base font-bold tracking-tight text-ink">1. THÔNG TIN NGƯỜI BỆNH</h2>
            <p className="text-xs text-sub">
              Được trích xuất từ dữ liệu hồ sơ ngoại trú điện tử (EHR)
            </p>
          </div>
        </div>

        <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700">
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          <span>{patientInfo.relationshipBadgeLabel}</span>
        </span>
      </div>

      {/* Metadata Grid */}
      <div className="mt-5 grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            HỌ VÀ TÊN NGƯỜI BỆNH
          </p>
          <p className="mt-0.5 text-lg font-bold text-ink">{patientInfo.fullName}</p>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            MÃ ĐỊNH DANH NGƯỜI BỆNH (PID)
          </p>
          <div className="mt-1">
            <span className="inline-flex items-center rounded border border-border bg-slate-100 px-2.5 py-0.5 font-mono text-sm font-bold text-sky-800">
              {patientInfo.patientCode}
            </span>
          </div>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">NGÀY SINH & TUỔI</p>
          <p className="mt-0.5 text-sm font-semibold text-ink">
            {patientInfo.dateOfBirth}{' '}
            <span className="font-normal text-sub">{patientInfo.ageLabel}</span>
          </p>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">GIỚI TÍNH</p>
          <p className="mt-0.5 text-sm font-semibold text-ink">{patientInfo.gender}</p>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            SỐ ĐIỆN THOẠI LIÊN HỆ
          </p>
          <p className="mt-0.5 text-sm font-bold text-ink">{patientInfo.phone}</p>
        </div>

        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            SỐ THẺ CĂN CƯỚC / CCCD
          </p>
          <p className="mt-0.5 font-mono text-sm font-bold tracking-wider text-ink">
            {patientInfo.nationalId}
          </p>
        </div>

        <div className="sm:col-span-2">
          <p className="text-xs font-medium uppercase tracking-wide text-sub">
            ĐỊA CHỈ CƯ TRÚ HIỆN TẠI
          </p>
          <p className="mt-0.5 text-sm font-medium text-ink">{patientInfo.currentAddress}</p>
        </div>

        {/* Declared Symptom & Reason */}
        <div className="pt-2 sm:col-span-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-1.5">
              <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span className="text-xs font-semibold uppercase tracking-wide text-ink">
                LÝ DO KHÁM & TRIỆU CHỨNG KHAI BÁO BAN ĐẦU
              </span>
            </div>

            {!isEditingNote && (
              <button
                type="button"
                onClick={handleOpenEdit}
                className="inline-flex items-center gap-1 text-xs font-semibold text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Chỉnh sửa lý do khám</span>
              </button>
            )}
          </div>

          {isEditingNote ? (
            <div className="mt-2 space-y-2 rounded-xl border border-sky-200 bg-sky-50/40 p-3.5">
              <label htmlFor="review-symptom-note-input" className="sr-only">
                Lý do khám và triệu chứng khai báo ban đầu
              </label>
              <textarea
                id="review-symptom-note-input"
                rows={3}
                value={draftNote}
                onChange={(e) => setDraftNote(e.target.value)}
                aria-invalid={Boolean(noteError)}
                aria-describedby={noteError ? 'review-symptom-note-error' : undefined}
                placeholder="Mô tả triệu chứng, thời gian khởi phát hoặc nhu cầu tái khám..."
                className="w-full rounded-lg border border-border bg-white p-2.5 text-sm text-ink focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
              {noteError && (
                <p
                  id="review-symptom-note-error"
                  role="alert"
                  className="text-xs font-medium text-red-600"
                >
                  {noteError}
                </p>
              )}
              <div className="flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={handleCancelEdit}
                  className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-sub transition-colors hover:bg-canvas"
                >
                  Hủy
                </button>
                <button
                  type="button"
                  onClick={handleSaveEdit}
                  className="rounded-lg bg-primary px-3.5 py-1.5 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
                >
                  Lưu cập nhật
                </button>
              </div>
            </div>
          ) : (
            <div className="mt-1.5 rounded-xl border border-border bg-canvas p-4">
              <p className="text-sm leading-relaxed text-ink">“{symptomNote}”</p>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
