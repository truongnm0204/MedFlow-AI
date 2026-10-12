import { forwardRef, type InputHTMLAttributes, type ReactNode } from 'react'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
  leftIcon?: ReactNode
  rightSlot?: ReactNode
}

/** Input theo Figma auth: label uppercase 12px semibold, border #e2e8f0, icon trái pl-10. */
export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, leftIcon, rightSlot, id, className = '', ...props }, ref) => {
    const inputId = id ?? label?.toLowerCase().replace(/\s+/g, '-')
    return (
      <div className="flex w-full flex-col gap-2">
        {label && (
          <label htmlFor={inputId} className="text-xs font-semibold uppercase tracking-wide text-slate-700">
            {label}
          </label>
        )}
        <div className="relative">
          {leftIcon && (
            <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3.5 text-slate-400">
              {leftIcon}
            </span>
          )}
          <input
            ref={ref}
            id={inputId}
            className={`w-full rounded-lg border bg-white py-3 text-sm text-slate-900 shadow-sm
              placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-primary/40
              ${leftIcon ? 'pl-10' : 'pl-4'} ${rightSlot ? 'pr-11' : 'pr-4'}
              ${error ? 'border-danger focus:ring-danger/30' : 'border-border'} ${className}`}
            aria-invalid={!!error}
            {...props}
          />
          {rightSlot && (
            <span className="absolute inset-y-0 right-0 flex items-center pr-3.5">{rightSlot}</span>
          )}
        </div>
        {error && <p className="text-xs text-danger">{error}</p>}
      </div>
    )
  },
)
Input.displayName = 'Input'
