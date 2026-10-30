import { Building2, Clock, MapPin } from 'lucide-react';
import type { ExaminationService } from '../types/service-search.types';

interface ServiceDetailHeroBannerProps {
  service: ExaminationService;
}

export function ServiceDetailHeroBanner({ service }: ServiceDetailHeroBannerProps) {
  const isOnline = service.isOnlineBookingActive ?? true;

  return (
    <div className="rounded-2xl border border-sky-100 bg-sky-50/40 p-5 transition-all">
      {/* Top status badge */}
      {isOnline && (
        <div className="mb-3">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-xs font-medium text-emerald-700 shadow-xs">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-pulse" />
            Đang mở nhận lịch trực tuyến
          </span>
        </div>
      )}

      {/* Main Title and Price Row */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-slate-900">{service.name}</h2>

          {/* Department and Location */}
          <div className="mt-2.5 flex flex-wrap items-center gap-2 text-xs text-slate-600">
            <span className="inline-flex items-center gap-1.5 font-medium text-slate-700">
              <Building2 className="h-3.5 w-3.5 text-sky-600 shrink-0" />
              <span>{service.departmentName}</span>
            </span>
            <span className="text-slate-300">•</span>
            <span className="inline-flex items-center gap-1.5 text-slate-600">
              <MapPin className="h-3.5 w-3.5 text-sky-600 shrink-0" />
              <span>{service.location ?? 'Khu Khám Ngoại trú - Tầng 2, Nhà A'}</span>
            </span>
          </div>

          {/* Duration */}
          <div className="mt-2.5 flex items-center gap-1.5 text-xs text-slate-600">
            <Clock className="h-3.5 w-3.5 text-slate-400 shrink-0" />
            <span>{service.durationDescription}</span>
          </div>
        </div>

        {/* Price column */}
        <div className="sm:text-right shrink-0">
          <span className="block text-[11px] font-semibold uppercase tracking-wider text-slate-500">
            GIÁ KHÁM NIÊM YẾT
          </span>
          <div className="text-3xl font-extrabold tracking-tight text-sky-700">
            {service.priceFormatted}
          </div>
          <span className="mt-0.5 block text-[11px] text-slate-500">
            {service.priceNote ?? 'Khám ngoại trú • giá theo dịch vụ/cơ sở/ngày khám'}
          </span>
        </div>
      </div>
    </div>
  );
}
