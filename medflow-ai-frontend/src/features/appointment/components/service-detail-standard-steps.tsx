import { ListOrdered } from 'lucide-react';
import { DEFAULT_STANDARD_STEPS } from '../data/service-search-mock';
import type { ExaminationService } from '../types/service-search.types';

interface ServiceDetailStandardStepsProps {
  service: ExaminationService;
}

export function ServiceDetailStandardSteps({ service }: ServiceDetailStandardStepsProps) {
  const steps =
    service.standardSteps && service.standardSteps.length > 0
      ? service.standardSteps
      : DEFAULT_STANDARD_STEPS;

  return (
    <div className="space-y-2.5">
      <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-slate-800">
        <ListOrdered className="h-4 w-4 text-sky-600 shrink-0" />
        <span>QUY TRÌNH THĂM KHÁM TIÊU CHUẨN ({steps.length} BƯỚC)</span>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {steps.map((step) => (
          <div
            key={step.stepNumber}
            className="flex items-start gap-3 rounded-xl border border-slate-200/80 bg-white p-3.5 shadow-xs transition-colors hover:border-sky-200"
          >
            <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-sky-50 text-xs font-bold text-sky-700 border border-sky-100">
              {step.stepNumber}
            </div>
            <div className="space-y-1">
              <h4 className="text-xs font-bold text-slate-900 leading-tight">{step.title}</h4>
              <p className="text-[11px] leading-relaxed text-slate-600">{step.description}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
