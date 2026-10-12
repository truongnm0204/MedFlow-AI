import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockSetTokens = vi.fn()
const mockClearAuth = vi.fn()

// Mock axios module trước khi import apiClient.
vi.mock('axios', async () => {
  const actual = await vi.importActual<typeof import('axios')>('axios')
  return {
    ...actual,
    default: {
      ...actual.default,
      post: vi.fn(),
      create: vi.fn(() => actual.default.create()),
    },
  }
})

// Mock dynamic import auth.store — tránh circular dep trong test.
vi.mock('@/features/auth/store/auth.store', () => ({
  useAuthStore: {
    getState: () => ({
      setTokens: mockSetTokens,
      clearAuth: mockClearAuth,
    }),
  },
}))

const { apiClient } = await import('@/shared/lib/axios')

const cfg = (url = '/test') =>
  ({ url, headers: {} }) as InternalAxiosRequestConfig & { _retried?: boolean }

/** Trích xuất response interceptor handler từ axios internals. */
function getResponseInterceptor() {
  const interceptors = (apiClient.interceptors.response as unknown as {
    handlers: Array<{ fulfilled?: unknown; rejected?: (e: AxiosError) => Promise<unknown> }>
  }).handlers
  return interceptors.find((h) => h.rejected)?.rejected
}

describe('axios interceptor — 401 refresh', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    // Stub adapter dispatch để retry không gọi network thật trong jsdom.
    apiClient.defaults.adapter = vi.fn((_config: unknown) =>
      Promise.resolve({ data: {}, status: 200, statusText: 'OK', headers: {}, config: _config }),
    ) as never
  })

  it('bỏ qua refresh cho auth endpoints', async () => {
    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const err = new AxiosError('401', undefined, cfg('/auth/login'), {}, { status: 401 } as never)
    await expect(handler!(err)).rejects.toThrow()
  })

  it('không refresh khi không có refresh token', async () => {
    const axiosModule = await import('axios')
    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const err = new AxiosError('401', undefined, cfg('/me'), {}, { status: 401 } as never)
    await expect(handler!(err)).rejects.toThrow()
    expect(axiosModule.default.post).not.toHaveBeenCalled()
  })

  it('bỏ qua request đã retry (_retried)', async () => {
    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const config = cfg('/me')
    config._retried = true
    const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never)
    await expect(handler!(err)).rejects.toThrow()
  })

  it('bỏ qua non-401 errors', async () => {
    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const err = new AxiosError('500', undefined, cfg('/me'), {}, { status: 500 } as never)
    await expect(handler!(err)).rejects.toThrow()
  })

  it('happy path: 401 → refresh → retry request gốc với token mới', async () => {
    localStorage.setItem('medflow.refresh_token', 'rt-old')
    const axiosModule = await import('axios')
    vi.mocked(axiosModule.default.post).mockResolvedValueOnce({
      data: {
        accessToken: 'at-new',
        accessTokenExpiresIn: 3600,
        refreshToken: 'rt-new',
        refreshTokenExpiresIn: 604800,
      },
    })

    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const config = cfg('/me')
    const err = new AxiosError('401', undefined, config, {}, { status: 401 } as never)
    await handler!(err)

    expect(axiosModule.default.post).toHaveBeenCalledWith(
      '/api/v1/auth/refresh',
      { refreshToken: 'rt-old' },
      expect.objectContaining({ timeout: 15_000 }),
    )
    expect(mockSetTokens).toHaveBeenCalledWith({ accessToken: 'at-new', refreshToken: 'rt-new' })
    expect(config.headers.Authorization).toBe('Bearer at-new')
    expect(config._retried).toBe(true)
  })

  it('single-flight: nhiều 401 đồng thời chỉ gọi refresh 1 lần', async () => {
    localStorage.setItem('medflow.refresh_token', 'rt-old')
    const axiosModule = await import('axios')
    vi.mocked(axiosModule.default.post).mockResolvedValueOnce({
      data: {
        accessToken: 'at-new',
        accessTokenExpiresIn: 3600,
        refreshToken: 'rt-new',
        refreshTokenExpiresIn: 604800,
      },
    })

    const handler = getResponseInterceptor()
    expect(handler).toBeDefined()

    const err1 = new AxiosError('401', undefined, cfg('/a'), {}, { status: 401 } as never)
    const err2 = new AxiosError('401', undefined, cfg('/b'), {}, { status: 401 } as never)
    const err3 = new AxiosError('401', undefined, cfg('/c'), {}, { status: 401 } as never)

    await Promise.all([handler!(err1), handler!(err2), handler!(err3)])

    expect(axiosModule.default.post).toHaveBeenCalledTimes(1)
  })
})
