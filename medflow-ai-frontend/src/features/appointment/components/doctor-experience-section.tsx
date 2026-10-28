import { Stethoscope } from 'lucide-react';

interface DoctorExperienceSectionProps {
  biography: string;
  hashtags: string[];
}

export function DoctorExperienceSection({ biography, hashtags }: DoctorExperienceSectionProps) {
  return (
    <div className="space-y-3">
      {/* Section Header */}
      <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-slate-800">
        <Stethoscope className="h-4 w-4 text-sky-600" />
        <span>KINH NGHIỆM & LĨNH VỰC CHUYÊN SÂU</span>
      </div>

      {/* Narrative Paragraph */}
      <p className="text-xs text-slate-600 leading-relaxed text-justify sm:text-sm">{biography}</p>

      {/* Hashtag Pills */}
      <div className="flex flex-wrap items-center gap-2 pt-1">
        {hashtags.map((tag) => {
          const isFeatured = tag.toLowerCase().includes('nbi');
          return (
            <span
              key={tag}
              className={`rounded-lg px-3 py-1 text-xs font-medium transition-all ${
                isFeatured
                  ? 'border border-sky-300 bg-sky-50 font-semibold text-sky-700 shadow-2xs'
                  : 'bg-slate-100/90 text-slate-600 hover:bg-slate-200/80 hover:text-slate-900'
              }`}
            >
              {tag}
            </span>
          );
        })}
      </div>
    </div>
  );
}
