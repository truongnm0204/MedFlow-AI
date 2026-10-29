import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react';

export const formControlClass =
  'mt-1.5 w-full rounded-md border border-slate-300 bg-white px-3 py-2.5 text-xs text-ink outline-none transition-colors focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:bg-slate-50 disabled:text-slate-400';

export interface FormFieldProps {
  label: string;
  required?: boolean;
  topNote?: string;
  hint?: ReactNode;
  error?: string;
  children: ReactNode;
  className?: string;
}

export function FormField({
  label,
  required = false,
  topNote,
  hint,
  error,
  children,
  className = '',
}: FormFieldProps) {
  return (
    <label className={`block text-[11px] font-semibold text-ink ${className}`}>
      <span className="flex items-center justify-between">
        <span>
          {label} {required && <span className="text-rose-500">*</span>}
        </span>
        {topNote && <span className="text-[10px] font-normal text-slate-400">{topNote}</span>}
      </span>
      {children}
      {hint && <span className="mt-1 block text-[10px] font-normal text-slate-500">{hint}</span>}
      {error && (
        <span role="alert" className="mt-1 block text-[10px] font-medium text-rose-600">
          {error}
        </span>
      )}
    </label>
  );
}

export interface FormSectionCardProps {
  stepNumber?: number | string;
  title: string;
  badge?: string;
  note?: string;
  children: ReactNode;
  className?: string;
}

export function FormSectionCard({
  stepNumber,
  title,
  badge,
  note,
  children,
  className = '',
}: FormSectionCardProps) {
  return (
    <section className={`rounded-xl border border-border bg-white p-6 shadow-sm ${className}`}>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <h2 className="flex items-center gap-2.5 text-xs font-bold uppercase tracking-wider text-ink">
          {stepNumber !== undefined ? (
            <span className="grid h-5 w-5 place-items-center rounded bg-slate-100 text-[11px] font-bold text-slate-700">
              {stepNumber}
            </span>
          ) : (
            <span className="h-4 w-1 rounded bg-primary" />
          )}
          <span>{title}</span>
        </h2>
        <div className="flex items-center gap-2">
          {badge && (
            <span className="rounded bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-600">
              {badge}
            </span>
          )}
          {note && <span className="text-[10px] text-slate-400">{note}</span>}
        </div>
      </div>
      {children}
    </section>
  );
}

export interface FormTextInputProps extends InputHTMLAttributes<HTMLInputElement> {
  prefixIcon?: ReactNode;
  suffixIcon?: ReactNode;
}

export function FormTextInput({
  prefixIcon,
  suffixIcon,
  className = '',
  ...props
}: FormTextInputProps) {
  if (!prefixIcon && !suffixIcon) {
    return <input className={`${formControlClass} ${className}`} {...props} />;
  }

  return (
    <div className="relative mt-1.5 flex items-center">
      {prefixIcon && (
        <div className="pointer-events-none absolute left-3 flex items-center text-slate-400">
          {prefixIcon}
        </div>
      )}
      <input
        className={`${formControlClass} !mt-0 ${prefixIcon ? 'pl-9' : ''} ${suffixIcon ? 'pr-9' : ''} ${className}`}
        {...props}
      />
      {suffixIcon && (
        <div className="pointer-events-none absolute right-3 flex items-center">{suffixIcon}</div>
      )}
    </div>
  );
}

export interface FormSelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  options?: { value: string; label: string }[];
}

export function FormSelect({ options, children, className = '', ...props }: FormSelectProps) {
  return (
    <select className={`${formControlClass} cursor-pointer ${className}`} {...props}>
      {options
        ? options.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))
        : children}
    </select>
  );
}

export interface FormTextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  maxLengthCount?: number;
  currentLength?: number;
}

export function FormTextarea({
  maxLengthCount,
  currentLength,
  className = '',
  ...props
}: FormTextareaProps) {
  return (
    <div>
      <textarea className={`${formControlClass} resize-y ${className}`} rows={3} {...props} />
      {maxLengthCount !== undefined && currentLength !== undefined && (
        <div className="mt-1 text-right text-[10px] text-slate-400">
          {currentLength}/{maxLengthCount} ký tự
        </div>
      )}
    </div>
  );
}
