import { AxiosError } from 'axios'

interface ProblemDetails {
  title?: string
  detail?: string
  error?: string
  message?: string
  errors?: Record<string, string[]>
}

/** Rút message hiển thị từ axios error (backend trả ProblemDetails). */
export function getErrorMessage(error: unknown, fallback = 'Đã có lỗi xảy ra. Vui lòng thử lại.'): string {
  if (error instanceof AxiosError) {
    const data = error.response?.data as ProblemDetails | string | undefined
    if (typeof data === 'string' && data.length < 200) return data
    if (data && typeof data === 'object') {
      if (data.detail) return data.detail
      if (data.error) return data.error
      if (data.message) return data.message
      if (data.title) return data.title
      const first = data.errors && Object.values(data.errors)[0]?.[0]
      if (first) return first
    }
    if (error.code === 'ERR_NETWORK') return 'Không kết nối được máy chủ. Kiểm tra backend đã chạy chưa.'
    if (error.response?.status === 429) return 'Bạn thao tác quá nhanh. Vui lòng thử lại sau.'
  }
  return error instanceof Error ? error.message : fallback
}
