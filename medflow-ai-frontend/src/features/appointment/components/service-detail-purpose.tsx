import { CheckCircle2 } from 'lucide-react';
import type { ExaminationService } from '../types/service-search.types';

interface ServiceDetailPurposeProps {
  service: ExaminationService;
}

export function ServiceDetailPurpose({ service }: ServiceDetailPurposeProps) {
  const highlightSymptoms = service.highlightSymptoms ?? [];

  const renderDescriptionWithHighlights = (text: string) => {
    if (highlightSymptoms.length === 0) {
      return text;
    }

    // Escape regex special chars
    const escapedTerms = highlightSymptoms.map((term) =>
      term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
    );
    const regex = new RegExp(`(${escapedTerms.join('|')})`, 'gi');
    const parts = text.split(regex);

    return parts.map((part, index) => {
      const isMatch = highlightSymptoms.some((term) => term.toLowerCase() === part.toLowerCase());
      if (isMatch) {
        return (
          <strong key={index} className="font-bold text-slate-900">
            {part}
          </strong>
        );
      }
      return part;
    });
  };

  const purposeText =
    service.purposeSummary ??
    `Thăm khám, chẩn đoán phân biệt và tư vấn phác đồ điều trị chuyên khoa đối với các trường hợp: ${
      highlightSymptoms.join(', ') || service.description
    }.`;

  const suitableText =
    service.suitableFor ??
    'Phù hợp cho mọi người bệnh có nhu cầu khám trực tiếp cùng Bác sĩ Chuyên khoa cấp cao hoặc Chuyên gia đầu ngành tại phòng khám ngoại trú riêng biệt, không cần xếp hàng chờ đợi lâu.';

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-slate-800">
        <CheckCircle2 className="h-4 w-4 text-sky-600 shrink-0" />
        <span>MỤC ĐÍCH KHÁM & ĐỐI TƯỢNG CHỈ ĐỊNH</span>
      </div>

      <div className="space-y-3 rounded-xl border border-slate-100 bg-slate-50/80 p-4 text-xs leading-relaxed text-slate-700">
        <p>{renderDescriptionWithHighlights(purposeText)}</p>
        <p className="text-slate-600">{suitableText}</p>
      </div>
    </div>
  );
}
