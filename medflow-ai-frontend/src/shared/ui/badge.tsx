import type { ReactNode } from 'react'

type Tone = 'sky' | 'emerald' | 'amber' | 'rose' | 'slate'

const TONES: Record<Tone, string> = {
  sky: 'bg-sky-50 text-sky-700 border-sky-200',
  emerald: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  amber: 'bg-amber-50 text-amber-700 border-amber-200',
  rose: 'bg-rose-50 text-rose-700 border-rose-200',
  slate: 'bg-slate-100 text-slate-600 border-slate-200',
}

interface BadgeProps {
  tone?: Tone
  /** Filled = nền đặc (vd badge "2" trên chuông, "1" trong menu). */
  solid?: boolean
  className?: string
  children: ReactNode
}

export function Badge({ tone = 'slate', solid = false, className = '', children }: BadgeProps) {
  const styles = solid
    ? 'border-transparent bg-primary text-white'
    : TONES[tone]
  return (
    <span
      className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold leading-4 ${styles} ${className}`}
    >
      {children}
    </span>
  )
}
