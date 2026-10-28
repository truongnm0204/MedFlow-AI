import { Award, FileCheck, GraduationCap } from 'lucide-react';

import type { EducationItem } from '../types/doctor-detail.types';

interface DoctorEducationListProps {
  education: EducationItem[];
}

function getEducationIcon(type: EducationItem['type']) {
  switch (type) {
    case 'degree':
      return <GraduationCap className="h-4 w-4 text-sky-600" />;
    case 'specialist':
      return <Award className="h-4 w-4 text-sky-600" />;
    case 'certificate':
      return <FileCheck className="h-4 w-4 text-sky-600" />;
    default:
      return <GraduationCap className="h-4 w-4 text-sky-600" />;
  }
}

export function DoctorEducationList({ education }: DoctorEducationListProps) {
  return (
    <div className="space-y-3">
      {/* Section Header */}
      <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-slate-800">
        <GraduationCap className="h-4 w-4 text-sky-600" />
        <span>QUÁ TRÌNH ĐÀO TẠO & BẰNG CẤP</span>
      </div>

      {/* Cards List */}
      <div className="space-y-2.5">
        {education.map((item) => (
          <div
            key={item.id}
            className="flex items-start justify-between gap-3 rounded-xl border border-slate-100 bg-slate-50/70 p-3.5 transition-all hover:bg-slate-50 sm:p-4"
          >
            <div className="flex items-start gap-3">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-sky-100/70 text-sky-600 shadow-2xs">
                {getEducationIcon(item.type)}
              </div>

              <div className="space-y-0.5">
                <h4 className="text-sm font-semibold text-slate-900 leading-snug">{item.title}</h4>
                <p className="text-xs text-slate-500 leading-relaxed">{item.institution}</p>
              </div>
            </div>

            {item.badge && (
              <span className="shrink-0 rounded-md bg-sky-50 px-2.5 py-1 text-[11px] font-semibold text-sky-700">
                {item.badge}
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
