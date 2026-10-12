import { useCallback, useEffect, useRef, useState } from 'react'

interface Countdown {
  /** Giây còn lại. */
  remaining: number
  isRunning: boolean
  /** Đặt lại đồng hồ về `seconds` (hoặc giá trị mới) và chạy lại. */
  restart: (seconds?: number) => void
}

/** Countdown 1s tick, cleanup khi unmount. */
export function useCountdown(seconds: number): Countdown {
  const [remaining, setRemaining] = useState(seconds)
  const duration = useRef(seconds)
  const isRunning = remaining > 0

  useEffect(() => {
    if (!isRunning) return
    const id = setInterval(() => {
      setRemaining((r) => (r > 0 ? r - 1 : 0))
    }, 1000)
    return () => clearInterval(id)
  }, [isRunning])

  const restart = useCallback((next?: number) => {
    if (next !== undefined) duration.current = next
    setRemaining(duration.current)
  }, [])

  return { remaining, isRunning, restart }
}
