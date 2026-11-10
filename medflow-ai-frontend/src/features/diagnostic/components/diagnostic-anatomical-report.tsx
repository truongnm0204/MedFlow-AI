import { CheckSquare, ClipboardCheck, FileText, Square } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { DiagnosticResultDetailView } from '../types/diagnostic-result-view.types';

interface DiagnosticAnatomicalReportProps {
  detail: DiagnosticResultDetailView;
}

export function DiagnosticAnatomicalReport({ detail }: DiagnosticAnatomicalReportProps) {
  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
          <div className="flex items-center gap-2">
            <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">
              Mô tả Chi tiết Tổn thương theo Vị trí Giải phẫu
            </h2>
          </div>
          <span className="text-[11px] font-medium text-sub">Đã ký số lúc {detail.signedAt}</span>
        </CardHeader>
        <CardContent className="space-y-4">
          {detail.anatomicalSections.map((sec) => (
            <section
              key={sec.id}
              aria-label={sec.organName}
              className="rounded-xl border border-border bg-white p-4 space-y-2.5"
            >
              <div className="flex items-center justify-between gap-2 border-b border-slate-100 pb-2">
                <h3 className="text-xs font-bold text-navy">{sec.organName}</h3>
                <Badge tone={sec.statusTone}>{sec.statusLabel}</Badge>
              </div>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                {sec.findings.map((f) => (
                  <div key={f.label} className="rounded-lg bg-canvas p-2.5 text-xs space-y-0.5">
                    <p className="font-semibold text-navy">{f.label}</p>
                    <p className="leading-relaxed text-ink">{f.detail}</p>
                  </div>
                ))}
              </div>
            </section>
          ))}

          {/* Procedure flags */}
          <div className="flex flex-wrap gap-4 rounded-xl border border-border bg-slate-50 px-4 py-3 text-xs">
            {detail.procedureChecklist.map((item) => (
              <div key={item.id} className="flex items-center gap-1.5 font-medium text-navy">
                {item.checked ? (
                  <CheckSquare className="h-4 w-4 text-primary" aria-hidden="true" />
                ) : (
                  <Square className="h-4 w-4 text-sub" aria-hidden="true" />
                )}
                <span>{item.label}</span>
              </div>
            ))}
          </div>

          {/* Clinical conclusion & ICD-10 */}
          <section
            aria-label="Kết luận chẩn đoán và lời dặn bác sĩ"
            className="rounded-xl border border-sky-200 bg-sky-50/70 p-4 space-y-3"
          >
            <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-navy">
              <ClipboardCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>Kết luận Chẩn đoán &amp; Lời dặn Bác sĩ</span>
            </div>

            <div className="rounded-lg border border-sky-200 bg-white p-3 text-sm font-bold text-navy">
              {detail.clinicalConclusion}
            </div>

            <div className="space-y-1.5">
              <p className="text-[11px] font-bold uppercase text-sub">Mã bệnh lý ICD-10 gợi ý</p>
              <div className="flex flex-wrap gap-2">
                {detail.icd10Tags.map((tag) => (
                  <span
                    key={tag.code}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-sky-200 bg-white px-2.5 py-1 text-xs font-medium text-navy"
                  >
                    <span className="font-mono font-bold text-primary">{tag.code}</span>
                    <span>- {tag.name}</span>
                  </span>
                ))}
              </div>
            </div>

            <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
              <span className="font-bold">Lời dặn chuyên khoa sau thủ thuật: </span>
              {detail.followUpAdvice}
            </div>
          </section>
        </CardContent>
      </Card>
    </div>
  );
}
