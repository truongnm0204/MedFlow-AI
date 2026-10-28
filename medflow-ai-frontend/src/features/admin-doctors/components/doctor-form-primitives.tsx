import type { ReactNode } from 'react';

interface DoctorFieldProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: ReactNode;
}

export const doctorControlClass =
  'mt-1.5 w-full rounded-md border border-slate-300 bg-white px-3 py-2.5 text-xs text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20';

export function DoctorField({ label, required = false, hint, error, children }: DoctorFieldProps) {
  return (
    <label className="block text-[11px] font-semibold text-ink">
      <span>
        {label} {required && <span className="text-rose-500">*</span>}
      </span>
      {children}
      {hint && <span className="mt-1 block text-[10px] font-normal text-slate-400">{hint}</span>}
      {error && (
        <span role="alert" className="mt-1 block text-[10px] font-medium text-rose-600">
          {error}
        </span>
      )}
    </label>
  );
}

export function DoctorCard({
  title,
  note,
  children,
}: {
  title: string;
  note?: string;
  children: ReactNode;
}) {
  return (
    <section className="rounded-xl border border-border bg-white p-6 shadow-sm">
      <div className="mb-6 flex items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <h2 className="flex items-center gap-2 text-sm font-bold tracking-wide text-ink">
          <span className="h-4 w-1 rounded bg-primary" />
          {title}
        </h2>
        {note && <span className="text-[10px] text-slate-400">{note}</span>}
      </div>
      {children}
    </section>
  );
}
