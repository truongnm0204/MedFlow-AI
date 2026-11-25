import {
  AlertTriangle,
  Calendar,
  CheckCircle2,
  ChevronRight,
  Clock,
  FileCheck2,
  FlaskConical,
  Pill,
  Stethoscope,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import type { PatientMedicalRecordDetail } from '../types/patient-records.types';
import { formatEmrDateTimeVn, getPrescriptionStatusMeta } from '../utils/emr-format';

export interface MedicalRecordClinicalSectionsProps {
  detail: PatientMedicalRecordDetail;
}

export function MedicalRecordClinicalSections({ detail }: MedicalRecordClinicalSectionsProps) {
  return (
    <div className="space-y-6">
      <section className="rounded-2xl border border-border bg-white p-5 shadow-xs">
        <div className="flex items-center gap-2">
          <Stethoscope className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">Thông tin khám & Kết luận bác sĩ</h2>
        </div>

        <dl className="mt-4 grid grid-cols-1 gap-4 text-xs sm:grid-cols-2">
          <div className="rounded-xl border border-border bg-canvas p-3.5">
            <dt className="font-semibold text-navy">Lý do khám / Triệu chứng chính</dt>
            <dd className="mt-1 leading-relaxed text-sub">
              {detail.presentingComplaint ?? detail.reasonForVisit ?? 'Không ghi nhận'}
            </dd>
          </div>
          <div className="rounded-xl border border-border bg-canvas p-3.5">
            <dt className="font-semibold text-navy">Tiền sử & Dị ứng liên quan</dt>
            <dd className="mt-1 leading-relaxed text-sub">
              {detail.relevantHistoryAllergies ?? 'Chưa ghi nhận tiền sử dị ứng'}
            </dd>
          </div>
          {detail.symptomHistory && (
            <div className="rounded-xl border border-border bg-canvas p-3.5">
              <dt className="font-semibold text-navy">Bệnh sử</dt>
              <dd className="mt-1 leading-relaxed text-sub">{detail.symptomHistory}</dd>
            </div>
          )}
          <div className="rounded-xl border border-border bg-canvas p-3.5">
            <dt className="font-semibold text-navy">Khám thực thể lâm sàng</dt>
            <dd className="mt-1 leading-relaxed text-sub">
              {detail.clinicalFindings ?? 'Không có ghi chú bất thường'}
            </dd>
          </div>
          {detail.clinicalProgressionNote && (
            <div className="rounded-xl border border-border bg-canvas p-3.5">
              <dt className="font-semibold text-navy">Diễn biến lâm sàng</dt>
              <dd className="mt-1 leading-relaxed text-sub">{detail.clinicalProgressionNote}</dd>
            </div>
          )}
          {detail.clinicalInterpretation && (
            <div className="rounded-xl border border-border bg-canvas p-3.5">
              <dt className="font-semibold text-navy">Phân tích kết quả cận lâm sàng</dt>
              <dd className="mt-1 leading-relaxed text-sub">{detail.clinicalInterpretation}</dd>
            </div>
          )}
        </dl>

        <div className="mt-4 rounded-xl border border-sky-200 bg-sky-50/70 p-4 space-y-2">
          <p className="text-xs font-bold uppercase tracking-wide text-primary">
            Kết luận lâm sàng & Hướng điều trị
          </p>
          <p className="text-sm font-medium leading-relaxed text-ink">
            {detail.clinicalConclusion ?? 'Đã hoàn tất khám lâm sàng.'}
          </p>
          {detail.treatmentPlan && (
            <p className="text-xs leading-relaxed text-sub">
              <strong className="text-navy">Hướng xử trí / phác đồ:</strong> {detail.treatmentPlan}
            </p>
          )}
        </div>

        {(detail.dischargeInstructions ||
          detail.followUpInstructions ||
          detail.followUpDate ||
          detail.warningSigns) && (
          <div
            className="mt-4 grid grid-cols-1 gap-3 text-xs sm:grid-cols-2"
            data-testid="emr-dedicated-care-instructions"
          >
            {detail.dischargeInstructions && (
              <div className="rounded-xl border border-border bg-canvas p-3.5">
                <p className="font-semibold text-navy">Hướng dẫn chăm sóc & dùng thuốc tại nhà</p>
                <p className="mt-1 leading-relaxed text-sub">{detail.dischargeInstructions}</p>
              </div>
            )}
            {(detail.followUpInstructions || detail.followUpDate) && (
              <div className="rounded-xl border border-border bg-canvas p-3.5">
                <p className="flex items-center gap-1.5 font-semibold text-navy">
                  <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                  <span>Hẹn tái khám</span>
                  {detail.followUpDate && (
                    <span className="rounded bg-primary/10 px-2 py-0.5 font-mono text-[11px] font-bold text-primary">
                      {detail.followUpDate}
                    </span>
                  )}
                </p>
                {detail.followUpInstructions && (
                  <p className="mt-1 leading-relaxed text-sub">{detail.followUpInstructions}</p>
                )}
              </div>
            )}
            {detail.warningSigns && (
              <div className="rounded-xl border border-amber-200 bg-amber-50/70 p-3.5 sm:col-span-2">
                <p className="flex items-center gap-1.5 font-semibold text-amber-900">
                  <AlertTriangle className="h-3.5 w-3.5 text-amber-600" aria-hidden="true" />
                  <span>Dấu hiệu cảnh báo cần tái khám ngay</span>
                </p>
                <p className="mt-1 leading-relaxed text-amber-900">{detail.warningSigns}</p>
              </div>
            )}
          </div>
        )}
      </section>

      <section className="rounded-2xl border border-border bg-white p-5 shadow-xs">
        <div className="flex items-center gap-2">
          <FileCheck2 className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">
            Chẩn đoán ICD-10 ({detail.diagnoses.length})
          </h2>
        </div>

        {detail.diagnoses.length === 0 ? (
          <p className="mt-3 text-xs text-sub">Chưa ghi nhận mã chẩn đoán ICD-10.</p>
        ) : (
          <ul className="mt-3.5 divide-y divide-border">
            {detail.diagnoses.map((dx) => (
              <li
                key={dx.diagnosisId}
                className="flex flex-wrap items-start justify-between gap-3 py-3 first:pt-0 last:pb-0"
              >
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded-md bg-primary/10 px-2 py-0.5 font-mono text-xs font-bold text-primary">
                      {dx.icd10Code}
                    </span>
                    <span className="text-xs font-bold text-ink">{dx.icd10NameVi}</span>
                    <span
                      className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                        dx.isPrimary ? 'bg-emerald-50 text-emerald-700' : 'bg-canvas text-sub'
                      }`}
                    >
                      {dx.isPrimary ? 'Bệnh chính' : 'Bệnh kèm theo'}
                    </span>
                  </div>
                  {dx.clinicalDiagnosis && (
                    <p className="mt-1 text-xs text-sub">
                      Chẩn đoán lâm sàng: {dx.clinicalDiagnosis}
                    </p>
                  )}
                </div>
                {dx.confirmedAt && (
                  <span className="text-[11px] text-sub">
                    Xác nhận: {formatEmrDateTimeVn(dx.confirmedAt)}
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="rounded-2xl border border-border bg-white p-5 shadow-xs">
        <div className="flex items-center gap-2">
          <FlaskConical className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-bold text-navy">
            Tóm tắt Chỉ định Cận lâm sàng ({detail.diagnosticOrders.length})
          </h2>
        </div>

        {detail.diagnosticOrders.length === 0 ? (
          <p className="mt-3 text-xs text-sub">
            Lượt khám này không phát sinh chỉ định cận lâm sàng.
          </p>
        ) : (
          <div className="mt-3.5 space-y-3">
            {detail.diagnosticOrders.map((order) => (
              <div
                key={order.orderId}
                className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-border bg-canvas p-4"
              >
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-xs font-bold text-navy">{order.serviceName}</span>
                    <span className="font-mono text-xs font-semibold text-primary">
                      ({order.orderCode})
                    </span>
                    <span
                      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${
                        order.isResultPublished
                          ? 'bg-emerald-50 text-emerald-700'
                          : 'bg-amber-50 text-amber-700'
                      }`}
                    >
                      {order.isResultPublished ? (
                        <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                      ) : (
                        <Clock className="h-3 w-3" aria-hidden="true" />
                      )}
                      <span>
                        {order.isResultPublished
                          ? 'Đã công bố kết quả'
                          : order.status === 'PERFORMED_AWAITING_RESULT'
                            ? 'Đã lấy mẫu — chờ ký duyệt'
                            : 'Đang chờ ký duyệt'}
                      </span>
                    </span>
                  </div>
                  {order.conclusionSummary && (
                    <p className="text-xs text-sub">Kết luận CLS: {order.conclusionSummary}</p>
                  )}
                </div>

                {order.isResultPublished ? (
                  <Link
                    to={`/emr/diagnostic-result/${encodeURIComponent(order.orderCode)}`}
                    className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-primary transition-colors hover:bg-sky-50 focus-visible:ring-2 focus-visible:ring-primary"
                  >
                    <span>Xem kết quả CLS</span>
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </Link>
                ) : (
                  <span
                    data-testid={`emr-diagnostic-unpublished-${order.orderCode}`}
                    className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-medium text-sub"
                  >
                    <Clock className="h-3.5 w-3.5 text-amber-600" aria-hidden="true" />
                    <span>Chưa phát hành kết quả</span>
                  </span>
                )}
              </div>
            ))}
          </div>
        )}
      </section>

      <section className="rounded-2xl border border-border bg-white p-5 shadow-xs">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">
              Đơn thuốc ngoại trú ({detail.prescriptions.length})
            </h2>
          </div>
          {detail.isSelfProfile && (
            <Link
              to="/patient/prescriptions"
              className="text-xs font-semibold text-primary hover:underline"
            >
              Tất cả đơn thuốc của tôi
            </Link>
          )}
        </div>

        {detail.prescriptions.length === 0 ? (
          <p className="mt-3 text-xs text-sub">
            Lượt khám này không phát sinh đơn thuốc ngoại trú.
          </p>
        ) : (
          <div className="mt-3.5 space-y-4">
            {detail.prescriptions.map((rx) => {
              const statusMeta = getPrescriptionStatusMeta(rx.status);
              const rxTarget = rx.prescriptionCode ?? String(rx.prescriptionId);
              return (
                <div
                  key={rx.prescriptionId}
                  className="rounded-xl border border-border bg-canvas p-4"
                >
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs font-bold text-navy">
                        {rx.prescriptionCode ?? `RX-${rx.prescriptionId}`}
                      </span>
                      <span
                        className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${statusMeta.className}`}
                      >
                        {statusMeta.label}
                      </span>
                      {rx.issuedAt && (
                        <span className="text-xs text-sub">
                          • Kê lúc {formatEmrDateTimeVn(rx.issuedAt)}
                        </span>
                      )}
                    </div>

                    {rx.canViewPrescriptionDetail && (
                      <Link
                        to={`/emr/prescriptions/${encodeURIComponent(rxTarget)}`}
                        className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-3.5 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
                      >
                        <span>Xem chi tiết đơn thuốc</span>
                        <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    )}
                  </div>

                  <ul className="mt-3 divide-y divide-border rounded-xl border border-border bg-white px-3.5">
                    {rx.items.map((med) => (
                      <li
                        key={med.itemId}
                        className="flex flex-wrap items-start justify-between gap-2 py-2.5 text-xs"
                      >
                        <div>
                          <p className="font-semibold text-ink">
                            {med.medicineName}
                            {med.strength ? ` (${med.strength})` : ''}
                          </p>
                          <p className="mt-0.5 text-sub">
                            {[med.doseScheduleText, med.frequency, med.administrationRoute]
                              .filter(Boolean)
                              .join(' • ') || 'Dùng theo chỉ định bác sĩ'}
                            {med.instructions ? ` — ${med.instructions}` : ''}
                          </p>
                        </div>
                        <span className="font-mono font-bold text-navy">
                          {med.prescribedQuantity} {med.dispensingUnit ?? 'Đơn vị'}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              );
            })}
          </div>
        )}
      </section>
    </div>
  );
}
