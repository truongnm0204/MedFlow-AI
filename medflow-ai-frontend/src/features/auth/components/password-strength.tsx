import { ShieldCheck } from 'lucide-react'

import { passwordStrength } from '../schemas/auth.schemas'

const LABELS = ['Rất yếu', 'Yếu', 'Trung bình', 'Mạnh', 'Rất an toàn']
const COLORS = ['bg-rose-500', 'bg-orange-400', 'bg-amber-400', 'bg-lime-500', 'bg-emerald-500']
const TEXT_COLORS = [
  'text-rose-600',
  'text-orange-500',
  'text-amber-500',
  'text-lime-600',
  'text-emerald-600',
]

interface PasswordStrengthProps {
  password: string
}

/** Strength meter 4 bar + nhãn theo Figma `1:228`. */
export function PasswordStrength({ password }: PasswordStrengthProps) {
  const score = passwordStrength(password)

  return (
    <div className="flex w-full flex-col gap-1.5 rounded-lg border border-slate-200/80 bg-canvas p-2.5">
      <div className="flex items-center justify-between">
        <p className="text-[11px] text-slate-500">
          Độ mạnh mật khẩu:{' '}
          <span className={`font-semibold ${password ? TEXT_COLORS[score] : 'text-slate-500'}`}>
            {password ? LABELS[score] : 'Chưa nhập'}
          </span>
        </p>
        <span className="flex items-center gap-1 text-[11px] font-medium text-emerald-600">
          <ShieldCheck className="h-3 w-3" />
          Chuẩn mã hóa y tế
        </span>
      </div>
      <div className="flex h-1.5 gap-1 overflow-hidden rounded-full bg-slate-200">
        {[0, 1, 2, 3].map((i) => (
          <div
            key={i}
            className={`flex-1 rounded-full transition-colors ${
              password && i < Math.max(score, 1) ? COLORS[score] : 'bg-transparent'
            }`}
          />
        ))}
      </div>
    </div>
  )
}
