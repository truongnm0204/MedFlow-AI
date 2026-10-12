import { useEffect, useRef } from 'react'

/** Gọi `handler` khi click ngoài phần tử `ref` (dùng cho popover, user-menu). */
export function useClickOutside<T extends HTMLElement>(handler: () => void) {
  const ref = useRef<T>(null)

  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) handler()
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') handler()
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [handler])

  return ref
}
