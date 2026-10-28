import { X, Clock, CheckCircle2, AlertCircle, ArrowRight } from 'lucide-react';
import type { ExaminationService } from '../types/service-search.types';

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
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <div className="relative w-full max-w-lg bg-white rounded-2xl shadow-xl border border-slate-100 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-slate-100">
          <div>
            <span className="text-[11px] font-semibold text-slate-500 uppercase tracking-wider bg-slate-50 px-2 py-0.5 rounded border border-slate-200">
              {service.serviceCode} • {service.departmentName}
            </span>
            <h2 className="text-base font-bold text-slate-900 mt-1.5">{service.name}</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng cửa sổ"
            className="p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body content */}
        <div className="p-5 space-y-4 max-h-[75vh] overflow-y-auto">
          {/* Key specs */}
          <div className="grid grid-cols-2 gap-3 p-3 bg-slate-50 rounded-xl">
            <div className="flex items-center gap-2">
              <Clock className="w-4 h-4 text-sky-600" />
              <div>
                <span className="text-[10px] text-slate-400 block uppercase">Thời lượng</span>
                <span className="text-xs font-semibold text-slate-800">
                  {service.durationDescription}
                </span>
              </div>
            </div>
            <div>
              <span className="text-[10px] text-slate-400 block uppercase">Đơn giá niêm yết</span>
              <span className="text-sm font-bold text-sky-700">{service.priceFormatted}</span>
            </div>
          </div>

          {/* Description */}
          <div>
            <h4 className="text-xs font-semibold text-slate-700 mb-1">Mô tả dịch vụ</h4>
            <p className="text-xs text-slate-600 leading-relaxed bg-white border border-slate-100 p-3 rounded-lg">
              {service.description}
            </p>
          </div>

          {/* Target Audience */}
          {service.targetAudience && (
            <div>
              <h4 className="text-xs font-semibold text-slate-700 mb-1 flex items-center gap-1.5">
                <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                <span>Đối tượng chỉ định phù hợp</span>
              </h4>
              <p className="text-xs text-slate-600 leading-relaxed pl-5">
                {service.targetAudience}
              </p>
            </div>
          )}

          {/* Preparation Notes */}
          {service.preparationNotes && service.preparationNotes.length > 0 && (
            <div className="p-3 bg-amber-50/70 border border-amber-200/60 rounded-xl">
              <h4 className="text-xs font-semibold text-amber-900 mb-1.5 flex items-center gap-1.5">
                <AlertCircle className="w-3.5 h-3.5 text-amber-600" />
                <span>Lưu ý chuẩn bị trước khi khám</span>
              </h4>
              <ul className="text-xs text-amber-800 space-y-1 list-disc list-inside">
                {service.preparationNotes.map((note, idx) => (
                  <li key={idx}>{note}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Tags */}
          {service.tags.length > 0 && (
            <div>
              <h4 className="text-xs font-semibold text-slate-700 mb-1.5">Đặc điểm chuyên môn</h4>
              <div className="flex flex-wrap gap-1.5">
                {service.tags.map((tag, idx) => (
                  <span
                    key={idx}
                    className="text-[11px] font-medium bg-slate-100 text-slate-700 px-2.5 py-0.5 rounded-md"
                  >
                    {tag}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Footer actions */}
        <div className="p-4 border-t border-slate-100 flex items-center justify-end gap-2 bg-slate-50/50">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-xs font-medium text-slate-700 hover:bg-slate-100 rounded-xl transition-colors"
          >
            Đóng
          </button>
          <button
            type="button"
            onClick={() => {
              onSelectService(service);
              onClose();
            }}
            className="px-4 py-2 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-xl flex items-center gap-1.5 shadow-sm transition-colors"
          >
            <span>Chọn dịch vụ & Tiếp tục</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  );
}
