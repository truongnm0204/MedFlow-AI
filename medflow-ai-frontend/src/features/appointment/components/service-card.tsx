import { Clock, ArrowRight } from 'lucide-react';
import type { ExaminationService } from '../types/service-search.types';

interface ServiceCardProps {
  service: ExaminationService;
  onViewDetail: (service: ExaminationService) => void;
  onSelectService: (service: ExaminationService) => void;
}

export function ServiceCard({ service, onViewDetail, onSelectService }: ServiceCardProps) {
  const renderBadge = () => {
    if (!service.badgeLabel) return null;

    if (service.badgeType === 'online') {
      return (
        <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-medium bg-sky-50 text-sky-700 border border-sky-100">
          <span className="w-1.5 h-1.5 rounded-full bg-sky-500 animate-pulse" />
          {service.badgeLabel}
        </span>
      );
    }

    if (service.badgeType === 'endoscopy') {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-[11px] font-medium bg-purple-50 text-purple-700 border border-purple-100">
          {service.badgeLabel}
        </span>
      );
    }

    if (service.badgeType === 'pediatric') {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-[11px] font-medium bg-emerald-50 text-emerald-700 border border-emerald-100">
          {service.badgeLabel}
        </span>
      );
    }

    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-[11px] font-medium bg-slate-50 text-slate-700 border border-slate-200">
        {service.badgeLabel}
      </span>
    );
  };

  return (
    <div className="flex flex-col justify-between bg-white rounded-2xl border border-slate-200/80 p-5 shadow-sm hover:shadow-md hover:border-sky-200 transition-all">
      <div>
        {/* Header row: Code & Dept + Badge */}
        <div className="flex items-start justify-between gap-2 mb-3">
          <span className="text-xs font-semibold text-slate-500 bg-slate-50 border border-slate-100 px-2 py-0.5 rounded-md">
            {service.serviceCode} • {service.departmentName}
          </span>
          {renderBadge()}
        </div>

        {/* Service Name */}
        <h3 className="text-base font-bold text-slate-900 mb-2 leading-snug hover:text-sky-700 transition-colors">
          {service.name}
        </h3>

        {/* Duration */}
        <div className="flex items-center gap-1.5 text-xs text-slate-600 mb-3">
          <Clock className="w-3.5 h-3.5 text-slate-400 shrink-0" />
          <span>
            Thời lượng dự kiến:{' '}
            <strong className="text-slate-800">{service.durationDescription}</strong>
          </span>
        </div>

        {/* Description */}
        <p className="text-xs text-slate-500 line-clamp-2 leading-relaxed mb-3">
          {service.description}
        </p>

        {/* Tags */}
        {service.tags.length > 0 && (
          <div className="flex flex-wrap gap-1.5 mb-4">
            {service.tags.map((tag, idx) => (
              <span
                key={idx}
                className="text-[11px] font-medium bg-slate-100 text-slate-600 px-2.5 py-0.5 rounded-md"
              >
                {tag}
              </span>
            ))}
          </div>
        )}
      </div>

      <div>
        {/* Price section */}
        <div className="pt-3 border-t border-slate-100 mb-4">
          <span className="block text-[11px] text-slate-500">Giá niêm yết trực tiếp:</span>
          <div className="flex items-baseline gap-1.5 mt-0.5 flex-wrap">
            <span className="text-lg font-bold text-sky-700">{service.priceFormatted}</span>
            {service.priceNote && (
              <span className="text-[11px] text-slate-400">{service.priceNote}</span>
            )}
          </div>
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onViewDetail(service)}
            className="flex-1 py-2 px-3 text-xs font-medium text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 hover:text-slate-900 rounded-xl transition-colors text-center"
          >
            Chi tiết dịch vụ
          </button>
          <button
            type="button"
            onClick={() => onSelectService(service)}
            className="flex-1 py-2 px-3 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 active:bg-sky-800 rounded-xl transition-colors flex items-center justify-center gap-1 shadow-sm"
          >
            <span>Chọn dịch vụ</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  );
}
