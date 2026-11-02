import { Check } from 'lucide-react';

import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

interface QueueVisitTimelineProps {
  data: AppointmentQueueStatus;
}

export function QueueVisitTimeline({ data }: QueueVisitTimelineProps) {
  return (
    <section
      aria-label="Trạng thái lượt khám"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div>
          <h2 className="text-sm font-bold text-ink uppercase">Trạng thái lượt khám (Visit)</h2>
          <p className="mt-0.5 font-mono text-xs text-sub">
            Mã ca khám ngoại trú: #{data.visitCode}
          </p>
        </div>

        <span className="inline-flex items-center rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-bold text-primary uppercase">
          VISIT: {data.statusLabel}
        </span>
      </div>

      <ol className="mt-5 grid grid-cols-1 gap-4 sm:grid-cols-3">
        {data.visitSteps.map((step, index) => {
          const isDone = step.state === 'done';
          const isCurrent = step.state === 'current';

          return (
            <li key={step.key} className="relative flex flex-col gap-2">
              <div className="flex items-center gap-2">
                {isDone ? (
                  <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-emerald-500 text-white">
                    <Check className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" />
                  </div>
                ) : isCurrent ? (
                  <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full border-2 border-primary bg-sky-50 ring-4 ring-sky-50">
                    <span className="h-2.5 w-2.5 rounded-full bg-primary" aria-hidden="true" />
                  </div>
                ) : (
                  <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-border bg-canvas text-xs font-bold text-sub">
                    {index + 1}
                  </div>
                )}

                {index < data.visitSteps.length - 1 && (
                  <div
                    aria-hidden="true"
                    className={`hidden h-0.5 flex-1 sm:block ${
                      isDone ? 'bg-emerald-500' : 'bg-border'
                    }`}
                  />
                )}
              </div>

              <div>
                <p className="text-xs font-bold text-ink">{step.title}</p>
                {step.statusLabel && (
                  <p className="mt-1">
                    {isDone ? (
                      <span className="text-xs font-bold text-emerald-600">{step.statusLabel}</span>
                    ) : (
                      <span className="inline-block rounded-md bg-slate-100 px-2 py-0.5 text-[10px] font-bold text-sub uppercase">
                        {step.statusLabel}
                      </span>
                    )}
                  </p>
                )}
                <p className="mt-1 text-[11px] leading-relaxed text-sub">{step.note}</p>
              </div>
            </li>
          );
        })}
      </ol>

      <p className="mt-5 border-t border-border pt-3 text-xs text-sub">
        Cận lâm sàng và kê đơn là các bước tùy chọn: chỉ hiển thị khi có chỉ định tương ứng; nếu
        không, bỏ qua và tiếp tục đến hoàn thành lượt khám.
      </p>
    </section>
  );
}
