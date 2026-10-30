import type { LucideIcon } from 'lucide-react';
import { CheckCircle2, Layers, PackageCheck, Search } from 'lucide-react';

import type { PharmacyQuickAction, PharmacyQuickActionIcon } from '../types/pharmacy.types';

interface PharmacyQuickActionsProps {
  actions: PharmacyQuickAction[];
  onAction?: (id: string) => void;
}

const ACCENT_STYLES: Record<PharmacyQuickAction['accent'], string> = {
  sky: 'border-sky-200 bg-sky-50 text-sky-700 group-hover:bg-primary group-hover:text-white',
  emerald:
    'border-emerald-200 bg-emerald-50 text-emerald-700 group-hover:bg-emerald-600 group-hover:text-white',
  amber:
    'border-amber-200 bg-amber-50 text-amber-700 group-hover:bg-amber-500 group-hover:text-white',
  rose: 'border-rose-200 bg-rose-50 text-rose-700 group-hover:bg-rose-600 group-hover:text-white',
};

const ICON_MAP: Record<PharmacyQuickActionIcon, LucideIcon> = {
  search: Search,
  prepare: PackageCheck,
  fefo: Layers,
  dispense: CheckCircle2,
};

export function PharmacyQuickActions({ actions, onAction }: PharmacyQuickActionsProps) {
  return (
    <div className="grid grid-cols-2 gap-3">
      {actions.map((action) => {
        const Icon = ICON_MAP[action.icon];
        return (
          <button
            key={action.id}
            type="button"
            onClick={() => onAction?.(action.id)}
            className="group rounded-2xl border border-border bg-white p-3.5 text-left transition-all hover:border-slate-300 hover:shadow-sm focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <div className="mb-2.5 flex items-center justify-between">
              <div
                className={`flex h-9 w-9 items-center justify-center rounded-xl border transition-colors ${ACCENT_STYLES[action.accent]}`}
              >
                <Icon aria-hidden="true" className="h-4 w-4" />
              </div>
              <kbd className="rounded border border-border bg-canvas px-1.5 py-0.5 font-mono text-[10px] font-bold text-sub">
                {action.shortcut}
              </kbd>
            </div>
            <div className="text-xs font-bold text-ink">{action.label}</div>
            <div className="mt-0.5 text-[11px] leading-snug text-sub">{action.description}</div>
          </button>
        );
      })}
    </div>
  );
}
