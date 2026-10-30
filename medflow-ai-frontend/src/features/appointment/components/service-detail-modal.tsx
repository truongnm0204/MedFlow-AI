import { ArrowRight, BriefcaseMedical, X } from 'lucide-react';
import type { ExaminationService } from '../types/service-search.types';
import { ServiceDetailHeroBanner } from './service-detail-hero-banner';
import { ServiceDetailPurpose } from './service-detail-purpose';
import { ServiceDetailStandardSteps } from './service-detail-standard-steps';

interface ServiceDetailModalProps {
  service: ExaminationService | null;
  isOpen: boolean;
  onClose: () => void;
  onSelectService: (service: ExaminationService) => void;
}

export function ServiceDetailModal({
  service,
  isOpen,
  onClose,
  onSelectService,
}: ServiceDetailModalProps) {
  if (!isOpen || !service) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="service-detail-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <div className="relative flex max-h-[90vh] w-full max-w-2xl flex-col rounded-3xl bg-white shadow-2xl border border-slate-100 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4.5 bg-white">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-sky-600 border border-sky-100 shadow-xs">
              <BriefcaseMedical className="h-5 w-5" />
            </div>
            <div>
              <h3 id="service-detail-title" className="text-base font-bold text-slate-900">
                Thông tin Chi tiết Dịch vụ Khám
              </h3>
              <p className="text-xs text-slate-500">
                Thông tin niêm yết, chỉ định và quy trình tiếp đón ngoại trú tự nguyện
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng cửa sổ"
            className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600 transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Scrollable Body Content */}
        <div className="flex-1 space-y-5 overflow-y-auto p-6">
          {/* Hero Banner: Name, Dept, Location, Duration & Price */}
          <ServiceDetailHeroBanner service={service} />

          {/* Section 1: Purpose & Target audience */}
          <ServiceDetailPurpose service={service} />

          {/* Section 2: Standard Examination 4 Steps */}
          <ServiceDetailStandardSteps service={service} />
        </div>

        {/* Footer */}
        <div className="flex flex-col gap-3 border-t border-slate-100 bg-white px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
          <span className="text-xs text-slate-400">Đặt lịch theo khung giờ do cơ sở khai báo.</span>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 transition-colors"
            >
              Đóng / Quay lại danh mục
            </button>
            <button
              type="button"
              onClick={() => {
                onSelectService(service);
                onClose();
              }}
              className="inline-flex items-center gap-1.5 rounded-xl bg-sky-600 px-5 py-2.5 text-xs font-semibold text-white shadow-sm hover:bg-sky-700 active:bg-sky-800 transition-colors"
            >
              <span>Xem lịch & Đặt dịch vụ này</span>
              <ArrowRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
