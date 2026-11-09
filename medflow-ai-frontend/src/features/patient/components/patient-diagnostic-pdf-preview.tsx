import { CheckCircle2, QrCode, ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { PatientDiagnosticResultSheet } from '../types/patient-diagnostic-result.types';

interface PatientDiagnosticPdfPreviewProps {
  sheet: PatientDiagnosticResultSheet;
}

export function PatientDiagnosticPdfPreview({ sheet }: PatientDiagnosticPdfPreviewProps) {
  return (
    <article
      aria-label="Phiếu kết quả cận lâm sàng A4"
      className="mx-auto max-w-4xl overflow-hidden rounded-2xl border border-slate-300 bg-white text-ink shadow-2xl"
    >
      {/* Hospital Letterhead */}
      <header className="border-b-2 border-navy bg-slate-50 px-8 py-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-1">
            <p className="text-xs font-bold uppercase tracking-wider text-primary">
              BỆNH VIỆN ĐA KHOA QUỐC TẾ MEDFLOW
            </p>
            <h2 className="text-xl font-black tracking-tight text-navy">
              PHIẾU KẾT QUẢ CẬN LÂM SÀNG
            </h2>
            <p className="text-xs text-sub">{sheet.performedRoom}</p>
          </div>

          <div className="flex items-center gap-3 rounded-xl border border-border bg-white px-3 py-2 text-right">
            <div>
              <p className="font-mono text-[11px] font-bold text-navy">{sheet.orderCode}</p>
              <p className="text-[10px] text-sub">Ký số: {sheet.signedAt}</p>
            </div>
            <QrCode className="h-9 w-9 text-navy" aria-hidden="true" />
          </div>
        </div>
      </header>

      <div className="space-y-6 px-8 py-6 text-xs">
        {/* Patient Metadata */}
        <section
          aria-label="Thông tin hành chính bệnh nhân"
          className="grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3"
        >
          <div>
            <span className="text-sub">Họ tên người bệnh:</span>
            <p className="text-sm font-bold text-navy">{sheet.patientName}</p>
            <p className="font-mono text-[11px] text-sub">
              {sheet.patientCode} • {sheet.gender} ({sheet.birthYear})
            </p>
          </div>
          <div>
            <span className="text-sub">Mã lượt khám &amp; Dịch vụ:</span>
            <p className="font-mono font-bold text-primary">{sheet.visitCode}</p>
            <p className="font-medium text-ink">{sheet.serviceName}</p>
          </div>
          <div>
            <span className="text-sub">Bác sĩ chỉ định &amp; Thực hiện:</span>
            <p className="font-semibold text-navy">{sheet.orderingDoctor}</p>
            <p className="text-sub">BS thực hiện: {sheet.performingDoctor}</p>
          </div>
        </section>

        {/* Diagnostic Images Grid */}
        <section aria-label="Hình ảnh nội soi đính kèm" className="space-y-3">
          <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
            1. Hình ảnh Chẩn đoán Nội soi Độ phân giải cao
          </h3>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" data-testid="patient-cls-images">
            {sheet.images.map((img) => (
              <figure
                key={img.id}
                className="overflow-hidden rounded-xl border border-border bg-slate-50 p-2"
              >
                <div
                  role="img"
                  aria-label={img.label}
                  className="flex aspect-video items-center justify-center rounded-lg bg-gradient-to-br from-slate-900 to-slate-700 font-mono text-xs text-white/80"
                >
                  {img.label}
                </div>
                <figcaption className="mt-1.5 text-[11px] text-ink">
                  <strong className="text-navy">{img.label}: </strong>
                  {img.description}
                </figcaption>
              </figure>
            ))}
          </div>
        </section>

        {/* Endoscopic Findings & Conclusion */}
        <section aria-label="Mô tả và kết luận nội soi" className="space-y-2.5">
          <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
            2. Mô tả &amp; Kết luận Chuyên khoa
          </h3>
          <ul className="list-disc space-y-1 pl-5 text-ink">
            {sheet.endoscopicFindings.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
          <div className="rounded-xl border border-sky-200 bg-sky-50/70 p-3.5 text-sm font-bold text-navy">
            Kết luận: {sheet.endoscopicConclusion}
          </div>
        </section>

        {/* CBC Lab Table */}
        <section aria-label="Bảng kết quả xét nghiệm máu" className="space-y-2.5">
          <h3 className="text-xs font-bold uppercase tracking-wider text-navy">
            3. {sheet.labSectionTitle}
          </h3>
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full border-collapse text-left text-xs">
              <thead>
                <tr className="border-b border-border bg-slate-50 text-[11px] font-bold uppercase text-sub">
                  <th scope="col" className="px-4 py-2.5">
                    Chỉ số xét nghiệm
                  </th>
                  <th scope="col" className="px-4 py-2.5 text-center">
                    Kết quả
                  </th>
                  <th scope="col" className="px-4 py-2.5 text-center">
                    Đơn vị
                  </th>
                  <th scope="col" className="px-4 py-2.5 text-center">
                    Khoảng tham chiếu
                  </th>
                  <th scope="col" className="px-4 py-2.5 text-right">
                    Đánh giá
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {sheet.labMetrics.map((m) => (
                  <tr key={m.id}>
                    <td className="px-4 py-2.5 font-semibold text-navy">{m.parameterName}</td>
                    <td className="px-4 py-2.5 text-center font-mono font-bold text-ink">
                      {m.value}
                    </td>
                    <td className="px-4 py-2.5 text-center text-sub">{m.unit}</td>
                    <td className="px-4 py-2.5 text-center font-mono text-sub">
                      {m.referenceRange}
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <Badge tone={m.status === 'NORMAL' ? 'emerald' : 'rose'}>
                        {m.status === 'NORMAL' ? 'Bình thường' : 'Bất thường'}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        {/* Advice & Signature */}
        <footer className="flex flex-wrap items-end justify-between gap-4 border-t border-border pt-4">
          <div className="max-w-lg rounded-xl border border-amber-200 bg-amber-50 p-3 text-amber-900">
            <p className="font-bold">Lời dặn của Bác sĩ:</p>
            <p className="mt-0.5 leading-relaxed">{sheet.physicianAdvice}</p>
          </div>

          <div className="text-center space-y-1 rounded-xl border border-emerald-200 bg-emerald-50/50 px-5 py-3">
            <div className="flex items-center justify-center gap-1 font-bold text-emerald-700">
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              <span>ĐÃ KÝ SỐ ĐIỆN TỬ HỢP LỆ</span>
            </div>
            <p className="font-bold text-navy">{sheet.performingDoctor}</p>
            <p className="text-[10px] text-sub flex items-center justify-center gap-1">
              <CheckCircle2 className="h-3 w-3 text-emerald-600" aria-hidden="true" />
              Xác thực MedFlow CA • {sheet.signedAt}
            </p>
          </div>
        </footer>
      </div>
    </article>
  );
}
