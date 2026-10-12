import { useState, type ReactNode } from 'react'
import { useClickOutside } from '../hooks/use-click-outside'

interface PopoverProps {
  /** Nút/trigger mở menu. */
  trigger: (open: boolean) => ReactNode
  children: ReactNode
  className?: string
}

/**
 * Popover generic theo Figma `10:139`: panel trắng border #e2e8f0,
 * rounded-12, shadow-lg, đóng khi click ngoài / Esc.
 */
export function Popover({ trigger, children, className = '' }: PopoverProps) {
  const [open, setOpen] = useState(false)
  const ref = useClickOutside<HTMLDivElement>(() => setOpen(false))

  return (
    <div ref={ref} className={`relative ${className}`}>
      <div onClick={() => setOpen((o) => !o)} role="button" tabIndex={0}
        onKeyDown={(e) => e.key === 'Enter' && setOpen((o) => !o)}>
        {trigger(open)}
      </div>
      {open && (
        <div className="absolute right-0 top-full z-50 mt-2 w-80 overflow-hidden rounded-xl border border-border bg-white p-2 shadow-lg">
          {children}
        </div>
      )}
    </div>
  )
}
