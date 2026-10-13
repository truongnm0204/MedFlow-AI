import type { ReactNode } from 'react';

export function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="rounded border border-border bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] font-bold text-slate-600">
      {children}
    </kbd>
  );
}
