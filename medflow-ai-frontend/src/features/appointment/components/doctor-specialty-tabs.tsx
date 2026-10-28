import { SPECIALTY_TABS } from '../data/doctor-search-mock';

interface DoctorSpecialtyTabsProps {
  selectedSpecialty: string;
  onSelectSpecialty: (specialtyId: string) => void;
}

export function DoctorSpecialtyTabs({
  selectedSpecialty,
  onSelectSpecialty,
}: DoctorSpecialtyTabsProps) {
  return (
    <div className="flex flex-wrap items-center gap-2 overflow-x-auto py-1 scrollbar-none">
      {SPECIALTY_TABS.map((tab) => {
        const isActive = selectedSpecialty === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            onClick={() => onSelectSpecialty(tab.id)}
            className={`whitespace-nowrap rounded-lg px-3.5 py-1.5 text-xs transition-all ${
              isActive
                ? 'bg-sky-600 font-semibold text-white shadow-sm'
                : 'bg-slate-100 font-medium text-slate-700 hover:bg-slate-200/80 hover:text-slate-900'
            }`}
          >
            {tab.name}
          </button>
        );
      })}
    </div>
  );
}
