import type { ReactNode } from 'react'
import { CheckCircle2, Info, TriangleAlert } from 'lucide-react'

type Tone = 'info' | 'success' | 'error'

const TONES: Record<Tone, { box: string; icon: ReactNode }> = {
  info: {
    box: 'border-sky-200 bg-sky-50 text-sky-800',
    icon: <Info className="h-4 w-4 shrink-0 text-sky-600" />,
  },
  success: {
    box: 'border-emerald-200 bg-emerald-50 text-emerald-800',
    icon: <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" />,
  },
  error: {
    box: 'border-rose-200 bg-rose-50 text-rose-700',
    icon: <TriangleAlert className="h-4 w-4 shrink-0 text-rose-500" />,
  },
}

interface AlertProps {
  tone?: Tone
  className?: string
  children: ReactNode
}

export function Alert({ tone = 'info', className = '', children }: AlertProps) {
  const { box, icon } = TONES[tone]
  return (
    <div className={`flex items-start gap-2.5 rounded-lg border px-3.5 py-3 text-xs leading-relaxed ${box} ${className}`}>
      {icon}
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  )
}
