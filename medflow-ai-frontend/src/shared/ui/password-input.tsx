import { forwardRef, useState, type InputHTMLAttributes } from 'react'
import { Eye, EyeOff, Lock } from 'lucide-react'
import { Input } from './input'

export interface PasswordInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: string
  error?: string
}

/** Input password + nút eye toggle (Figma `1:2`). */
export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(
  ({ label, error, ...props }, ref) => {
    const [visible, setVisible] = useState(false)
    return (
      <Input
        ref={ref}
        type={visible ? 'text' : 'password'}
        label={label}
        error={error}
        leftIcon={<Lock className="h-4 w-4" />}
        rightSlot={
          <button
            type="button"
            tabIndex={-1}
            onClick={() => setVisible((v) => !v)}
            className="text-slate-400 transition-colors hover:text-slate-600"
            aria-label={visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
          >
            {visible ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
          </button>
        }
        {...props}
      />
    )
  },
)
PasswordInput.displayName = 'PasswordInput'
