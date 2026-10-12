import { useRef, type ClipboardEvent, type KeyboardEvent } from 'react'

const OTP_LENGTH = 6

interface OtpInputProps {
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}

/** 6 ô OTP 64px — auto-advance, backspace lùi, paste fill-all. */
export function OtpInput({ value, onChange, disabled }: OtpInputProps) {
  const inputsRef = useRef<Array<HTMLInputElement | null>>([])
  const digits = Array.from({ length: OTP_LENGTH }, (_, i) => value[i] ?? '')

  function focusAt(index: number) {
    inputsRef.current[index]?.focus()
    inputsRef.current[index]?.select()
  }

  function setDigit(index: number, digit: string) {
    const next = (value.slice(0, index) + digit + value.slice(index + 1)).slice(0, OTP_LENGTH)
    onChange(next)
  }

  function handleChange(index: number, raw: string) {
    const digit = raw.replace(/\D/g, '').slice(-1)
    if (!digit) return
    setDigit(index, digit)
    if (index < OTP_LENGTH - 1) focusAt(index + 1)
  }

  function handleKeyDown(index: number, e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Backspace') {
      e.preventDefault()
      if (digits[index]) {
        setDigit(index, '')
      } else if (index > 0) {
        setDigit(index - 1, '')
        focusAt(index - 1)
      }
    } else if (e.key === 'ArrowLeft' && index > 0) {
      e.preventDefault()
      focusAt(index - 1)
    } else if (e.key === 'ArrowRight' && index < OTP_LENGTH - 1) {
      e.preventDefault()
      focusAt(index + 1)
    }
  }

  function handlePaste(e: ClipboardEvent<HTMLInputElement>) {
    e.preventDefault()
    const pasted = e.clipboardData.getData('text').replace(/\D/g, '').slice(0, OTP_LENGTH)
    if (!pasted) return
    onChange(pasted)
    focusAt(Math.min(pasted.length, OTP_LENGTH - 1))
  }

  return (
    <div className="flex justify-center gap-3" role="group" aria-label="Mã OTP 6 chữ số">
      {digits.map((digit, i) => (
        <input
          key={i}
          ref={(el) => {
            inputsRef.current[i] = el
          }}
          type="text"
          inputMode="numeric"
          autoComplete={i === 0 ? 'one-time-code' : 'off'}
          maxLength={1}
          value={digit}
          disabled={disabled}
          onChange={(e) => handleChange(i, e.target.value)}
          onKeyDown={(e) => handleKeyDown(i, e)}
          onPaste={handlePaste}
          onFocus={(e) => e.target.select()}
          aria-label={`Chữ số ${i + 1}`}
          className={`h-16 w-full rounded-xl border-2 bg-white text-center font-mono text-2xl font-bold
            text-ink shadow-sm transition-colors focus:outline-none disabled:opacity-60
            ${digit ? 'border-[#0284c7]' : 'border-slate-300'}
            focus:border-[#0ea5e9] focus:shadow-[0_0_0_4px_#e0f2fe]`}
        />
      ))}
    </div>
  )
}
