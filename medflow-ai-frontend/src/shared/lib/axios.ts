import axios from 'axios'

export const apiClient = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  timeout: 15_000,
})

// Attach auth token when present.
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('medflow.access_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Centralized error normalization point.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => Promise.reject(error),
)
