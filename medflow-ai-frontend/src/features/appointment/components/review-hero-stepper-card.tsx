import { Check, ClipboardCheck } from 'lucide-react';

import type { ReviewBookingMode } from '../types/review-booking.types';

interface ReviewHeroStepperCardProps {
  mode?: ReviewBookingMode;
}

interface ReviewStepItem {
  stepNumber: number;
  topLabel: string;
  title: string;
  subLabel: string;
  status: 'completed' | 'current' | 'upcoming';
}

export function ReviewHeroStepperCard({ mode = 'doctor' }: ReviewHeroStepperCardProps) {
  const steps: ReviewStepItem[] = [
    {
      stepNumber: 1,
      topLabel: 'BƯỚC 1',
      title: mode === 'service' ? 'Dịch vụ khám' : 'Khoa & Bác sĩ',
      subLabel: 'Đã hoàn thành',
      status: 'completed',
    },
    {
      stepNumber: 2,
      topLabel: 'BƯỚC 2',
      title: mode === 'service' ? 'Ngày & Ca khám' : 'Ngày & Giờ khám',
      subLabel: 'Đã hoàn thành',
      status: 'completed',
    },
    {
      stepNumber: 3,
      topLabel: 'HIỆN TẠI',
      title: 'Soát xét thông tin',
      subLabel: 'Kiểm tra & Xác nhận',
      status: 'current',
    },
    {
      stepNumber: 4,
      topLabel: 'BƯỚC 4',
      title: 'Hoàn tất & Cấp STT',
      subLabel: 'Chờ xác nhận',
      status: 'upcoming',
    },
  ];

  return (
    <section
      aria-label="Tiêu đề và tiến trình soát xét đặt lịch"
      className="relative overflow-hidden rounded-2xl border border-border bg-white p-6 shadow-xs sm:p-7"
    >
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -right-8 -top-8 h-44 w-44 rounded-full bg-sky-50 blur-md"
      />

      <div className="relative space-y-2">
        <div className="inline-flex items-center gap-1.5 rounded-md border border-sky-200/80 bg-sky-50 px-3 py-1 text-xs font-bold uppercase tracking-wider text-primary-dark">
          <ClipboardCheck className="h-3.5 w-3.5" aria-hidden="true" />
          <span>BƯỚC CHUẨN BỊ CUỐI CÙNG</span>
        </div>

        <h1 className="text-2xl font-extrabold tracking-tight text-ink sm:text-3xl">
          Soát xét Thông tin Đặt khám Ngoại trú
        </h1>

        <p className="max-w-2xl text-sm leading-relaxed text-sub sm:text-base">
          Vui lòng kiểm tra kỹ lưỡng các thông tin bên dưới trước khi xác nhận tạo lịch hẹn và nhận
          số thứ tự giữ trước tại buồng khám.
        </p>
      </div>

      <div className="relative mt-6 border-t border-slate-100 pt-6">
        <ol
          aria-label="Tiến trình đặt lịch khám 4 bước"
          className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
        >
          {steps.map((step) => {
            const isCompleted = step.status === 'completed';
            const isCurrent = step.status === 'current';

            return (
              <li
                key={step.stepNumber}
                aria-current={isCurrent ? 'step' : undefined}
                className={`flex items-center gap-3 rounded-xl px-2.5 py-2 transition-colors ${
                  isCurrent ? 'border border-sky-200 bg-sky-50/80' : ''
                }`}
              >
                <div
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
                    isCompleted
                      ? 'border border-emerald-300 bg-emerald-100 text-emerald-700'
                      : isCurrent
                        ? 'bg-primary text-white shadow-xs ring-4 ring-sky-100'
                        : 'bg-slate-200 text-sub'
                  }`}
                >
                  {isCompleted ? (
                    <Check className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" />
                  ) : (
                    <span>{step.stepNumber}</span>
                  )}
                </div>

                <div className="min-w-0 flex-1">
                  <p
                    className={`text-[11px] font-bold uppercase tracking-wider ${
                      isCompleted
                        ? 'text-emerald-700'
                        : isCurrent
                          ? 'text-primary-dark'
                          : 'text-sub'
                    }`}
                  >
                    {step.topLabel}
                  </p>
                  <p
                    className={`truncate text-xs font-bold ${
                      isCurrent ? 'text-navy' : isCompleted ? 'text-ink' : 'text-sub'
                    }`}
                  >
                    {step.title}
                  </p>
                  <p
                    className={`text-[10px] ${
                      isCompleted
                        ? 'text-emerald-700'
                        : isCurrent
                          ? 'font-medium text-primary-dark'
                          : 'text-sub'
                    }`}
                  >
                    {step.subLabel}
                  </p>
                </div>
              </li>
            );
          })}
        </ol>
      </div>
    </section>
  );
}
