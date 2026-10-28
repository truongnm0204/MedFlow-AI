import type { DoctorQuickAction } from '../types/doctor.types';

interface DoctorQuickActionsProps {
  actions: DoctorQuickAction[];
  onAction?: (id: string) => void;
}

const ACCENT_STYLES: Record<DoctorQuickAction['accent'], string> = {
  sky: 'bg-sky-50 text-sky-700 border-sky-200 group-hover:bg-sky-600 group-hover:text-white',
  emerald:
    'bg-emerald-50 text-emerald-700 border-emerald-200 group-hover:bg-emerald-600 group-hover:text-white',
  amber:
    'bg-amber-50 text-amber-700 border-amber-200 group-hover:bg-amber-500 group-hover:text-white',
  rose: 'bg-rose-50 text-rose-700 border-rose-200 group-hover:bg-rose-600 group-hover:text-white',
};

export function DoctorQuickActions({ actions, onAction }: DoctorQuickActionsProps) {
  return (
    <div className="grid grid-cols-2 gap-3">
      {actions.map((action) => (
        <button
          key={action.id}
          type="button"
          onClick={() => onAction?.(action.id)}
          className="group text-left p-4 rounded-2xl border border-slate-200 bg-white hover:border-slate-300 hover:shadow-sm transition-all"
        >
          <div className="flex items-center justify-between mb-3">
            <div
              className={`w-10 h-10 rounded-xl border flex items-center justify-center transition-colors ${ACCENT_STYLES[action.accent]}`}
            >
              <svg
                aria-hidden="true"
                className="w-5 h-5"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d={action.icon}
                />
              </svg>
            </div>
            <kbd className="px-1.5 py-0.5 text-[10px] font-mono font-bold text-slate-500 bg-slate-100 border border-slate-200 rounded">
              {action.shortcut}
            </kbd>
          </div>
          <div className="text-sm font-bold text-slate-800">{action.label}</div>
          <div className="text-[11px] text-slate-500 mt-0.5 leading-snug">{action.description}</div>
        </button>
      ))}
    </div>
  );
}
