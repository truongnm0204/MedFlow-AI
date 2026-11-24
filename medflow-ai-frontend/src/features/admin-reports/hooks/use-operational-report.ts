import { useQuery } from '@tanstack/react-query';

import {
  fetchOperationalReportApi,
  type OperationalReportQueryParams,
} from '../api/operational-report.api';

export function useOperationalReport(params?: OperationalReportQueryParams) {
  return useQuery({
    queryKey: [
      'admin-operational-report',
      params?.fromDate ?? '',
      params?.toDate ?? '',
      params?.departmentId ?? 0,
      params?.serviceId ?? 0,
      params?.hospitalId ?? 0,
    ] as const,
    queryFn: () => fetchOperationalReportApi(params),
    retry: false,
  });
}
