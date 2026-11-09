import {
  Activity,
  ChevronDown,
  ChevronUp,
  ClipboardList,
  FileCheck2,
  FlaskConical,
  History,
  MessageSquarePlus,
  Pill,
  Stethoscope,
} from 'lucide-react';
import { useState } from 'react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { MedicalHistoryVisitEntryDto } from '../types/medical-history.types';
import {
  getDiagnosticOrderStatusMeta,
  getPrescriptionStatusMeta,
  getRecordStatusMeta,
  getVisitStatusMeta,
} from '../utils/medical-history-meta';

export interface MedicalHistoryVisitCardProps {
  visit: MedicalHistoryVisitEntryDto;
  defaultExpanded?: boolean;
}

function formatDateTimeShort(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function MedicalHistoryVisitCard({
  visit,
  defaultExpanded = false,
}: MedicalHistoryVisitCardProps) {
  const [isExpanded, setIsExpanded] = useState(defaultExpanded);
  const [showRevisions, setShowRevisions] = useState(false);

  const visitStatusMeta = getVisitStatusMeta(visit.status);
  const recordStatusMeta = visit.medicalRecord
    ? getRecordStatusMeta(visit.medicalRecord.status)
    : null;

  const vitalSigns = visit.medicalRecord?.vitalSigns;
  const bpDisplay =
    vitalSigns?.bloodPressureLabel ||
    (vitalSigns?.bpSystolicMmhg && vitalSigns?.bpDiastolicMmhg
      ? `${vitalSigns.bpSystolicMmhg}/${vitalSigns.bpDiastolicMmhg} mmHg`
      : '—');

  return (
    <Card className={visit.isCurrentVisit ? 'border-sky-300 ring-1 ring-sky-100' : ''}>
      <CardHeader className="flex-col items-stretch gap-3 bg-canvas/60">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
          <div className="flex flex-wrap items-center gap-2 min-w-0">
            <span className="font-mono text-xs font-bold text-navy bg-white border border-border rounded-md px-2 py-0.5">
              {visit.visitDate}
            </span>
            <span className="font-mono text-sm font-bold text-primary">{visit.visitCode}</span>
            <Badge tone={visitStatusMeta.tone}>{visitStatusMeta.label}</Badge>
            <Badge tone="slate">
              {visit.visitType?.toUpperCase() === 'SCHEDULED' ? 'Hẹn trước' : 'Walk-in'}
            </Badge>
            {visit.isCurrentVisit && <Badge tone="emerald">Lượt khám hiện tại</Badge>}
            <Badge tone={visit.isAssignedToCurrentDoctor ? 'sky' : 'slate'}>
              {visit.isAssignedToCurrentDoctor ? 'BS phụ trách' : 'Cùng cơ sở y tế'}
            </Badge>
          </div>

          <button
            type="button"
            onClick={() => setIsExpanded((prev) => !prev)}
            aria-expanded={isExpanded}
            aria-label={`Chi tiết lượt khám ${visit.visitCode}`}
            className="inline-flex items-center justify-center gap-1.5 self-start sm:self-auto rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <span>{isExpanded ? 'Thu gọn' : 'Mở chi tiết'}</span>
            {isExpanded ? (
              <ChevronUp className="h-4 w-4" aria-hidden="true" />
            ) : (
              <ChevronDown className="h-4 w-4" aria-hidden="true" />
            )}
          </button>
        </div>

        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-sub">
          <span>
            Chuyên khoa: <strong className="text-navy">{visit.specialtyName}</strong> (
            {visit.departmentName})
          </span>
          {visit.roomCode && (
            <span>
              Phòng: <strong className="font-mono text-navy">{visit.roomCode}</strong>
            </span>
          )}
          <span>
            Bác sĩ khám: <strong className="text-navy">{visit.doctorName}</strong>
          </span>
          {visit.reasonForVisit && (
            <span className="text-ink">
              Lý do khám: <strong>{visit.reasonForVisit}</strong>
            </span>
          )}
        </div>
      </CardHeader>

      {isExpanded && (
        <CardContent className="space-y-5 p-5">
          {/* 1. Khám lâm sàng & Sinh hiệu */}
          <section className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
                <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                  1. Khám lâm sàng & Sinh hiệu
                </h3>
              </div>
              {visit.medicalRecord && recordStatusMeta && (
                <div className="flex flex-wrap items-center gap-2 text-xs">
                  <Badge tone={recordStatusMeta.tone}>{recordStatusMeta.label}</Badge>
                  <span className="font-mono text-sub">
                    Phiên bản #{visit.medicalRecord.latestRevisionNo}
                  </span>
                  {visit.medicalRecord.editedByDoctorName && (
                    <span className="text-sub">
                      • Cập nhật bởi{' '}
                      <strong className="text-navy">
                        {visit.medicalRecord.editedByDoctorName}
                      </strong>
                    </span>
                  )}
                </div>
              )}
            </div>

            {!visit.medicalRecord ? (
              <div className="rounded-lg border border-dashed border-border bg-canvas px-4 py-3 text-xs text-sub">
                Chưa có bản ghi bệnh án lâm sàng cho lượt khám này.
              </div>
            ) : (
              <div className="space-y-3">
                {vitalSigns && (
                  <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2.5">
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">Huyết áp</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {bpDisplay}
                      </div>
                    </div>
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">Mạch</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {vitalSigns.pulseBpm !== null ? `${vitalSigns.pulseBpm} lần/phút` : '—'}
                      </div>
                    </div>
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">Nhiệt độ</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {vitalSigns.temperatureC !== null ? `${vitalSigns.temperatureC} °C` : '—'}
                      </div>
                    </div>
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">SpO2</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {vitalSigns.spo2Percent !== null ? `${vitalSigns.spo2Percent}%` : '—'}
                      </div>
                    </div>
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">Cân nặng / Cao</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {vitalSigns.weightKg ?? '—'} kg / {vitalSigns.heightCm ?? '—'} cm
                      </div>
                    </div>
                    <div className="rounded-lg border border-border bg-canvas p-2.5">
                      <div className="text-[11px] text-sub">BMI</div>
                      <div className="text-xs font-bold font-mono text-navy mt-0.5">
                        {vitalSigns.bmi !== null ? vitalSigns.bmi : '—'}
                      </div>
                    </div>
                  </div>
                )}

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
                  <div className="rounded-lg border border-border p-3 space-y-1.5">
                    <div>
                      <span className="font-semibold text-navy">Triệu chứng chính: </span>
                      <span className="text-ink">
                        {visit.medicalRecord.presentingComplaint || 'Không ghi nhận'}
                      </span>
                    </div>
                    <div>
                      <span className="font-semibold text-navy">Bệnh sử: </span>
                      <span className="text-ink">
                        {visit.medicalRecord.symptomHistory || 'Không ghi nhận'}
                      </span>
                    </div>
                    {visit.medicalRecord.relevantHistoryAllergies && (
                      <div>
                        <span className="font-semibold text-navy">
                          Tiền sử / Dị ứng liên quan:{' '}
                        </span>
                        <span className="text-rose-700 font-medium">
                          {visit.medicalRecord.relevantHistoryAllergies}
                        </span>
                      </div>
                    )}
                  </div>

                  <div className="rounded-lg border border-border p-3 space-y-1.5">
                    <div>
                      <span className="font-semibold text-navy">Khám thực thể: </span>
                      <span className="text-ink">
                        {visit.medicalRecord.clinicalFindings || 'Không ghi nhận'}
                      </span>
                    </div>
                    <div>
                      <span className="font-semibold text-navy">Kết luận lâm sàng: </span>
                      <span className="text-navy font-semibold">
                        {visit.medicalRecord.clinicalConclusion || 'Chưa có kết luận'}
                      </span>
                    </div>
                    {visit.medicalRecord.amendmentReason && (
                      <div className="text-sub">
                        <span className="font-semibold text-navy">Lý do cập nhật: </span>
                        {visit.medicalRecord.amendmentReason}
                      </div>
                    )}
                  </div>
                </div>

                {visit.medicalRecord.revisions.length > 0 && (
                  <div className="space-y-2">
                    <button
                      type="button"
                      onClick={() => setShowRevisions((prev) => !prev)}
                      aria-expanded={showRevisions}
                      className="inline-flex items-center gap-1.5 text-xs font-semibold text-primary hover:underline"
                    >
                      <History className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>
                        Lịch sử phiên bản bệnh án ({visit.medicalRecord.revisions.length} phiên bản)
                      </span>
                    </button>

                    {showRevisions && (
                      <div className="rounded-lg border border-border bg-canvas p-3 space-y-2">
                        {visit.medicalRecord.revisions.map((rev) => (
                          <div
                            key={rev.revisionId}
                            className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-1 border-b border-border last:border-b-0 pb-2 last:pb-0 text-xs"
                          >
                            <div>
                              <span className="font-mono font-bold text-navy">
                                Phiên bản #{rev.revisionNo}
                              </span>{' '}
                              <span className="text-sub">
                                ({rev.editedByDoctorName || 'Bác sĩ'} •{' '}
                                {formatDateTimeShort(rev.recordedAt)})
                              </span>
                              {rev.clinicalConclusion && (
                                <p className="text-ink mt-0.5">{rev.clinicalConclusion}</p>
                              )}
                            </div>
                            {rev.amendmentReason && (
                              <span className="text-[11px] text-sub italic">
                                Lý do: {rev.amendmentReason}
                              </span>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                )}

                {visit.medicalRecord.addenda.length > 0 && (
                  <div className="rounded-lg border border-amber-200 bg-amber-50/70 p-3 space-y-2">
                    <div className="flex items-center gap-1.5 text-xs font-bold text-amber-900">
                      <MessageSquarePlus className="h-4 w-4 text-amber-700" aria-hidden="true" />
                      <span>
                        Phụ lục đính chính lâm sàng ({visit.medicalRecord.addenda.length})
                      </span>
                    </div>
                    {visit.medicalRecord.addenda.map((addendum) => {
                      const addendumNote = addendum.note ?? addendum.Note;
                      return (
                        <div
                          key={addendum.addendumId}
                          className="rounded-md bg-white/80 border border-amber-200 p-2.5 text-xs space-y-1"
                        >
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="font-semibold text-navy">
                              {addendum.reason || 'Bổ sung hồ sơ bệnh án'}
                            </span>
                            <span className="text-[11px] text-sub font-mono">
                              {addendum.authorDoctorName || 'Bác sĩ'} •{' '}
                              {formatDateTimeShort(addendum.recordedAt)}
                            </span>
                          </div>
                          {addendumNote && <p className="text-ink">{addendumNote}</p>}
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            )}
          </section>

          {/* 2. Chẩn đoán ICD-10 */}
          <section className="space-y-2.5">
            <div className="flex items-center gap-2">
              <ClipboardList className="h-4 w-4 text-primary" aria-hidden="true" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                2. Chẩn đoán ICD-10 ({visit.diagnoses.length})
              </h3>
            </div>

            {visit.diagnoses.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border bg-canvas px-4 py-3 text-xs text-sub">
                Chưa ghi nhận chẩn đoán ICD-10.
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
                {visit.diagnoses.map((diag) => (
                  <div
                    key={diag.diagnosisId}
                    className="rounded-lg border border-border bg-white p-3 flex flex-col justify-between gap-1.5 text-xs"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="rounded bg-sky-50 border border-sky-200 px-2 py-0.5 font-mono font-bold text-primary">
                          {diag.icd10Code}
                        </span>
                        <span className="font-bold text-navy">{diag.icd10NameVi}</span>
                      </div>
                      <Badge tone={diag.isPrimary ? 'emerald' : 'slate'}>
                        {diag.isPrimary ? 'Chẩn đoán chính' : 'Bệnh kèm theo'}
                      </Badge>
                    </div>
                    {diag.clinicalDiagnosis && (
                      <div className="text-ink">
                        <span className="text-sub">Chẩn đoán lâm sàng: </span>
                        {diag.clinicalDiagnosis}
                      </div>
                    )}
                    <div className="flex flex-wrap items-center justify-between text-[11px] text-sub pt-1 border-t border-border">
                      <span>Trạng thái: {diag.status}</span>
                      {diag.recordedByDoctorName && <span>BS: {diag.recordedByDoctorName}</span>}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* 3. Chỉ định & Kết quả Cận lâm sàng */}
          <section className="space-y-2.5">
            <div className="flex items-center gap-2">
              <FlaskConical className="h-4 w-4 text-primary" aria-hidden="true" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                3. Chỉ định & Kết quả Cận lâm sàng ({visit.diagnosticOrders.length})
              </h3>
            </div>

            {visit.diagnosticOrders.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border bg-canvas px-4 py-3 text-xs text-sub">
                Không có chỉ định cận lâm sàng.
              </div>
            ) : (
              <div className="space-y-2.5">
                {visit.diagnosticOrders.map((order) => {
                  const orderStatusMeta = getDiagnosticOrderStatusMeta(
                    order.status,
                    order.isResultFinalized,
                  );
                  return (
                    <div
                      key={order.orderId}
                      className="rounded-lg border border-border bg-white p-3 space-y-2 text-xs"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-mono font-bold text-navy">{order.orderCode}</span>
                          {order.serviceCode && (
                            <span className="font-mono text-[11px] text-sub">
                              [{order.serviceCode}]
                            </span>
                          )}
                          <span className="font-bold text-ink">{order.serviceName}</span>
                          {order.requiredForConclusion && (
                            <Badge tone="amber">Bắt buộc cho kết luận</Badge>
                          )}
                        </div>
                        <div className="flex items-center gap-2">
                          <Badge tone={order.priority === 'URGENT' ? 'rose' : 'slate'}>
                            {order.priority === 'URGENT' ? 'Khẩn' : 'Thường quy'}
                          </Badge>
                          <Badge tone={orderStatusMeta.tone}>{orderStatusMeta.label}</Badge>
                        </div>
                      </div>

                      {order.instruction && (
                        <div className="text-sub">
                          <span className="font-semibold text-navy">Chỉ dẫn: </span>
                          {order.instruction}
                        </div>
                      )}

                      {(order.resultText || order.conclusion) && (
                        <div className="rounded-md bg-canvas border border-border p-2.5 space-y-1">
                          {order.resultText && (
                            <div>
                              <span className="font-semibold text-navy">Mô tả kết quả: </span>
                              <span className="text-ink">{order.resultText}</span>
                            </div>
                          )}
                          {order.conclusion && (
                            <div className="flex items-center gap-1.5">
                              <FileCheck2
                                className="h-3.5 w-3.5 text-emerald-600 shrink-0"
                                aria-hidden="true"
                              />
                              <span className="font-semibold text-navy">Kết luận CLS: </span>
                              <span className="font-semibold text-emerald-800">
                                {order.conclusion}
                              </span>
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </section>

          {/* 4. Đơn thuốc ngoại trú */}
          <section className="space-y-2.5">
            <div className="flex items-center gap-2">
              <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
                4. Đơn thuốc ngoại trú ({visit.prescriptions.length})
              </h3>
            </div>

            {visit.prescriptions.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border bg-canvas px-4 py-3 text-xs text-sub">
                Không có đơn thuốc ngoại trú.
              </div>
            ) : (
              <div className="space-y-3">
                {visit.prescriptions.map((rx) => {
                  const rxStatusMeta = getPrescriptionStatusMeta(rx.status);
                  return (
                    <div
                      key={rx.prescriptionId}
                      className="rounded-lg border border-border bg-white overflow-hidden text-xs"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2 bg-canvas px-3.5 py-2.5 border-b border-border">
                        <div className="flex flex-wrap items-center gap-2">
                          <Activity className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                          <span className="font-mono font-bold text-navy">
                            {rx.prescriptionCode}
                          </span>
                          <Badge tone={rxStatusMeta.tone}>{rxStatusMeta.label}</Badge>
                        </div>
                        <div className="text-[11px] text-sub">
                          {rx.prescribingDoctorName && (
                            <span>BS kê đơn: {rx.prescribingDoctorName} • </span>
                          )}
                          <span>Ngày kê: {formatDateTimeShort(rx.issuedAt)}</span>
                        </div>
                      </div>

                      {rx.items.length > 0 && (
                        <div className="overflow-x-auto">
                          <table className="w-full text-left border-collapse">
                            <thead>
                              <tr className="border-b border-border bg-canvas/40 text-[11px] font-semibold text-sub">
                                <th className="py-2 px-3">Thuốc / Hoạt chất</th>
                                <th className="py-2 px-3">Số lượng</th>
                                <th className="py-2 px-3">Liều dùng & Đường dùng</th>
                                <th className="py-2 px-3">Hướng dẫn</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-border">
                              {rx.items.map((item) => (
                                <tr key={item.itemId}>
                                  <td className="py-2.5 px-3">
                                    <div className="font-bold text-navy">{item.medicineName}</div>
                                    <div className="text-[11px] text-sub">
                                      {item.medicineCode && (
                                        <span className="font-mono mr-1.5">
                                          [{item.medicineCode}]
                                        </span>
                                      )}
                                      {item.activeIngredient}
                                      {item.strength ? ` (${item.strength})` : ''}
                                    </div>
                                  </td>
                                  <td className="py-2.5 px-3 font-mono font-semibold text-navy whitespace-nowrap">
                                    {item.prescribedQuantity ?? '—'} {item.dispensingUnit || ''}
                                  </td>
                                  <td className="py-2.5 px-3">
                                    <div className="text-ink font-medium">
                                      {item.doseScheduleText || item.frequency || 'Theo chỉ định'}
                                    </div>
                                    <div className="text-[11px] text-sub">
                                      {item.administrationRoute || 'Uống'}
                                      {item.durationDays ? ` • ${item.durationDays} ngày` : ''}
                                    </div>
                                  </td>
                                  <td className="py-2.5 px-3 text-sub">
                                    {item.instructions || '—'}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </section>
        </CardContent>
      )}
    </Card>
  );
}
