import type { QuickAction } from '../types/reception.types';

interface QuickActionsPanelProps {
  actions: QuickAction[];
  onAction?: (id: string) => void;
}

const ACCENT_STYLES: Record<QuickAction['accent'], string> = {
  sky: 'bg-sky-50 text-sky-700 border-sky-200 group-hover:bg-sky-600 group-hover:text-white',
  emerald:
    'bg-emerald-50 text-emerald-700 border-emerald-200 group-hover:bg-emerald-600 group-hover:text-white',
  amber:
    'bg-amber-50 text-amber-700 border-amber-200 group-hover:bg-amber-500 group-hover:text-white',
  rose: 'bg-rose-50 text-rose-700 border-rose-200 group-hover:bg-rose-600 group-hover:text-white',
};

export function QuickActionsPanel({ actions, onAction }: QuickActionsPanelProps) {
  return (
    <div className="grid grid-cols-2 gap-3">
      {actions.map((action) => (
        <button
          key={action.id}
          type="button"
          onClick={() => onAction?.(action.id)}
          className="group text-left p-4 rounded-2xl border border-slate-200 bg-white hover:border-slate-300 hover:shadow-sm transition-all"
        >
          <div
            className={`w-10 h-10 rounded-xl border flex items-center justify-center mb-3 transition-colors ${ACCENT_STYLES[action.accent]}`}
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d={action.icon} />
            </svg>
          </div>
          <div className="text-sm font-bold text-slate-800">{action.label}</div>
          <div className="text-[11px] text-slate-500 mt-0.5 leading-snug">{action.description}</div>
        </button>
      ))}
    </div>
  );
}
