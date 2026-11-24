import { apiClient } from '@/shared/lib/axios';

import type {
  AppointmentListFilterParams,
  AppointmentListResponse,
} from '../types/appointment-list.types';

export async function fetchPatientAppointmentsApi(
  params: AppointmentListFilterParams = {},
): Promise<AppointmentListResponse> {
  const {
    memberId = 'ALL',
    tab = 'ALL',
    keyword = '',
    dateFrom = '',
    dateTo = '',
    page = 1,
    pageSize = 10,
  } = params;

  const { data } = await apiClient.get<AppointmentListResponse>('/appointments/me', {
    params: { memberId, tab, keyword, dateFrom, dateTo, page, pageSize },
  });
  return data;
}
