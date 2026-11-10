import { CalendarClock, ShieldAlert, Siren, Stethoscope, Trash2 } from 'lucide-react';

import { Alert } from '@/shared/ui/alert';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type {
  DiagnosisConclusionFieldErrors,
  DiagnosisConclusionFormValues,
  Icd10Option,
} from '../types/diagnosis-conclusion.types';
import { DiagnosisIcd10Selector } from './diagnosis-icd10-selector';

export interface DiagnosisConclusionPanelProps {
  values: DiagnosisConclusionFormValues;
  errors: DiagnosisConclusionFieldErrors;
  catalog: Icd10Option[];
  allergies: string;
  onChange: (patch: Partial<DiagnosisConclusionFormValues>) => void;
}

const TEXTAREA_CLASS =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20';
const INPUT_CLASS =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20';

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} role="alert" className="mt-1 text-xs font-medium text-rose-600">
      {message}
    </p>
  );
}

export function DiagnosisConclusionPanel({
  values,
  errors,
  catalog,
  allergies,
  onChange,
}: DiagnosisConclusionPanelProps) {
  const findOption = (code: string) => catalog.find((option) => option.code === code);
  const primary = findOption(values.primaryIcd10Code);
  const selectedCodes = [
    ...(values.primaryIcd10Code ? [values.primaryIcd10Code] : []),
    ...values.secondaryIcd10Codes,
  ];

  return (
    <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
      <div className="space-y-6">
        <Card>
          <CardHeader className="py-3.5">
            <h2 className="text-sm font-bold text-navy">2. XÁC LẬP CHẨN ĐOÁN BỆNH ICD-10</h2>
          </CardHeader>
          <CardContent className="space-y-5">
            <DiagnosisIcd10Selector
              id="diagnosis-primary-search"
              label="Chọn bệnh chính (ICD-10)"
              options={catalog}
              disabledCodes={selectedCodes}
              error={errors.primaryIcd10Code}
              onSelect={(option) => onChange({ primaryIcd10Code: option.code })}
            />

            <section
              aria-label="Bệnh chính"
              className="rounded-xl border border-primary/30 bg-canvas p-4"
            >
              <p className="text-[11px] font-bold uppercase tracking-wide text-primary">
                BỆNH CHÍNH
              </p>
              {primary ? (
                <div className="mt-1 flex items-start justify-between gap-3">
                  <p className="text-sm font-bold text-navy" data-testid="primary-diagnosis">
                    {primary.code} - {primary.nameVi} ({primary.nameEn})
                  </p>
                  <button
                    type="button"
                    aria-label={`Bỏ bệnh chính ${primary.code}`}
                    onClick={() => onChange({ primaryIcd10Code: '' })}
                    className="rounded-lg p-1.5 text-sub transition-colors hover:bg-white hover:text-rose-600"
                  >
                    <Trash2 className="h-4 w-4" aria-hidden="true" />
                  </button>
                </div>
              ) : (
                <p className="mt-1 text-xs text-sub">Chưa chọn chẩn đoán bệnh chính.</p>
              )}
            </section>

            <DiagnosisIcd10Selector
              id="diagnosis-secondary-search"
              label="Thêm bệnh kèm theo (ICD-10)"
              options={catalog}
              disabledCodes={selectedCodes}
              error={errors.secondaryIcd10Codes}
              onSelect={(option) =>
                onChange({ secondaryIcd10Codes: [...values.secondaryIcd10Codes, option.code] })
              }
            />

            <section aria-label="Bệnh kèm theo">
              <p className="text-[11px] font-bold uppercase tracking-wide text-navy">
                BỆNH KÈM THEO / THỨ PHÁT
              </p>
              {values.secondaryIcd10Codes.length === 0 ? (
                <p className="mt-1 text-xs text-sub">Chưa có bệnh kèm theo.</p>
              ) : (
                <ul className="mt-2 space-y-2">
                  {values.secondaryIcd10Codes.map((code) => {
                    const option = findOption(code);
                    return (
                      <li
                        key={code}
                        className="flex items-center justify-between gap-3 rounded-lg border border-border bg-white px-3 py-2"
                      >
                        <span className="text-sm text-ink">
                          <span className="font-bold text-navy">{code}</span>
                          {option ? ` - ${option.nameVi}` : ''}
                        </span>
                        <button
                          type="button"
                          aria-label={`Xóa bệnh kèm theo ${code}`}
                          onClick={() =>
                            onChange({
                              secondaryIcd10Codes: values.secondaryIcd10Codes.filter(
                                (item) => item !== code,
                              ),
                            })
                          }
                          className="rounded-lg p-1.5 text-sub transition-colors hover:bg-canvas hover:text-rose-600"
                        >
                          <Trash2 className="h-4 w-4" aria-hidden="true" />
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </section>

            <div>
              <label
                htmlFor="diagnosis-differential-notes"
                className="mb-1 block text-xs font-semibold text-navy"
              >
                Chẩn đoán phân biệt & ghi chú của bác sĩ
              </label>
              <textarea
                id="diagnosis-differential-notes"
                rows={3}
                value={values.differentialNotes}
                aria-invalid={errors.differentialNotes ? true : undefined}
                aria-describedby={errors.differentialNotes ? 'differential-notes-error' : undefined}
                onChange={(event) => onChange({ differentialNotes: event.target.value })}
                className={TEXTAREA_CLASS}
              />
              <FieldError id="differential-notes-error" message={errors.differentialNotes} />
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="space-y-6">
        <Card>
          <CardHeader className="py-3.5">
            <h2 className="text-sm font-bold text-navy">
              3. KẾT LUẬN LÂM SÀNG & ĐỊNH HƯỚNG ĐIỀU TRỊ
            </h2>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <label
                htmlFor="diagnosis-clinical-conclusion"
                className="mb-1 block text-xs font-semibold text-navy"
              >
                Kết luận lâm sàng (bắt buộc, tối thiểu 10 ký tự)
              </label>
              <textarea
                id="diagnosis-clinical-conclusion"
                rows={5}
                value={values.clinicalConclusion}
                aria-invalid={errors.clinicalConclusion ? true : undefined}
                aria-describedby={
                  errors.clinicalConclusion ? 'clinical-conclusion-error' : undefined
                }
                onChange={(event) => onChange({ clinicalConclusion: event.target.value })}
                className={TEXTAREA_CLASS}
              />
              <FieldError id="clinical-conclusion-error" message={errors.clinicalConclusion} />
            </div>

            <section
              aria-label="Định hướng phác đồ điều trị và cảnh báo an toàn"
              className="rounded-xl border border-amber-200 bg-amber-50 p-4"
            >
              <div className="flex items-center gap-2 text-amber-800">
                <Stethoscope className="h-4 w-4" aria-hidden="true" />
                <p className="text-[11px] font-bold uppercase tracking-wide">
                  ĐỊNH HƯỚNG PHÁC ĐỒ ĐIỀU TRỊ & CẢNH BÁO AN TOÀN
                </p>
              </div>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs text-amber-800">
                <li>Ưu tiên phác đồ diệt HP và bảo vệ niêm mạc dạ dày theo hướng dẫn hiện hành.</li>
                <li>Tránh các thuốc giảm đau kháng viêm không steroid (NSAIDs).</li>
                <li>Kiểm tra tương tác thuốc và chống chỉ định trước khi kê đơn.</li>
              </ul>
            </section>

            <div
              role="note"
              className="flex items-start gap-2.5 rounded-xl border border-rose-300 bg-rose-50 p-4 text-rose-700"
            >
              <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div className="text-xs">
                <p className="font-bold uppercase">LƯU Ý CỰC KỲ QUAN TRỌNG VỀ DỊ ỨNG</p>
                <p className="mt-1">
                  Bệnh nhân dị ứng: <strong>{allergies}</strong>. Tuyệt đối không kê đơn các thuốc
                  thuộc nhóm này.
                </p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="py-3.5">
            <h2 className="text-sm font-bold text-navy">
              4. DẶN DÒ Y KHOA & LỊCH HẸN TÁI KHÁM NGOẠI TRÚ
            </h2>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <label
                htmlFor="diagnosis-lifestyle"
                className="mb-1 block text-xs font-semibold text-navy"
              >
                Dặn dò sinh hoạt & chế độ ăn uống
              </label>
              <textarea
                id="diagnosis-lifestyle"
                rows={3}
                value={values.lifestyleInstructions}
                aria-invalid={errors.lifestyleInstructions ? true : undefined}
                aria-describedby={errors.lifestyleInstructions ? 'lifestyle-error' : undefined}
                onChange={(event) => onChange({ lifestyleInstructions: event.target.value })}
                className={TEXTAREA_CLASS}
              />
              <FieldError id="lifestyle-error" message={errors.lifestyleInstructions} />
            </div>

            <div>
              <label
                htmlFor="diagnosis-warning-signs"
                className="mb-1 flex items-center gap-1.5 text-xs font-semibold text-rose-700"
              >
                <Siren className="h-3.5 w-3.5" aria-hidden="true" />
                Dấu hiệu cần đến cấp cứu ngay
              </label>
              <textarea
                id="diagnosis-warning-signs"
                rows={3}
                value={values.warningSigns}
                aria-invalid={errors.warningSigns ? true : undefined}
                aria-describedby={errors.warningSigns ? 'warning-signs-error' : undefined}
                onChange={(event) => onChange({ warningSigns: event.target.value })}
                className={TEXTAREA_CLASS}
              />
              <FieldError id="warning-signs-error" message={errors.warningSigns} />
            </div>

            <div className="rounded-xl border border-border p-4">
              <label className="flex items-center gap-2 text-sm font-semibold text-navy">
                <input
                  type="checkbox"
                  role="switch"
                  checked={values.followUp.enabled}
                  onChange={(event) =>
                    onChange({ followUp: { ...values.followUp, enabled: event.target.checked } })
                  }
                  className="h-4 w-4 accent-primary"
                />
                <CalendarClock className="h-4 w-4 text-primary" aria-hidden="true" />
                Hẹn tái khám
              </label>

              {values.followUp.enabled && (
                <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <div>
                    <label
                      htmlFor="diagnosis-followup-date"
                      className="mb-1 block text-xs font-semibold text-navy"
                    >
                      Ngày tái khám
                    </label>
                    <input
                      id="diagnosis-followup-date"
                      type="date"
                      value={values.followUp.date}
                      aria-invalid={errors.followUpDate ? true : undefined}
                      onChange={(event) =>
                        onChange({ followUp: { ...values.followUp, date: event.target.value } })
                      }
                      className={INPUT_CLASS}
                    />
                    <FieldError id="followup-date-error" message={errors.followUpDate} />
                  </div>
                  <div>
                    <label
                      htmlFor="diagnosis-followup-time"
                      className="mb-1 block text-xs font-semibold text-navy"
                    >
                      Giờ tái khám
                    </label>
                    <input
                      id="diagnosis-followup-time"
                      type="time"
                      value={values.followUp.time}
                      aria-invalid={errors.followUpTime ? true : undefined}
                      onChange={(event) =>
                        onChange({ followUp: { ...values.followUp, time: event.target.value } })
                      }
                      className={INPUT_CLASS}
                    />
                    <FieldError id="followup-time-error" message={errors.followUpTime} />
                  </div>
                  <div>
                    <label
                      htmlFor="diagnosis-followup-room"
                      className="mb-1 block text-xs font-semibold text-navy"
                    >
                      Phòng khám
                    </label>
                    <input
                      id="diagnosis-followup-room"
                      type="text"
                      value={values.followUp.room}
                      aria-invalid={errors.followUpRoom ? true : undefined}
                      onChange={(event) =>
                        onChange({ followUp: { ...values.followUp, room: event.target.value } })
                      }
                      className={INPUT_CLASS}
                    />
                    <FieldError id="followup-room-error" message={errors.followUpRoom} />
                  </div>
                </div>
              )}
            </div>

            <Alert tone="info">
              Dặn dò và lịch tái khám sẽ được in kèm kết luận phát hành cho bệnh nhân.
            </Alert>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
