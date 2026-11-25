import { QueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';

export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (error instanceof AxiosError) {
    const status = error.response?.status;
    if (status === 429 || status === 401 || status === 403 || status === 404) {
      return false;
    }
  }
  return failureCount < 1;
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: shouldRetryQuery,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
    mutations: {
      retry: false,
    },
  },
});
