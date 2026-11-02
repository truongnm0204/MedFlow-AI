import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import {
  CANCELLED_STATUSES,
  COMPLETED_STATUSES,
  MOCK_APPOINTMENT_MEMBERS,
  MOCK_APPOINTMENTS,
  UPCOMING_STATUSES,
} from '../data/appointment-list-mock';
import type {
  AppointmentListFilterParams,
  AppointmentListItem,
  AppointmentListResponse,
  AppointmentMemberCountItem,
  AppointmentTabCounts,
} from '../types/appointment-list.types';

/** Chỉ fallback về mock khi backend chưa mở endpoint (404) hoặc offline/network error. */
function shouldFallbackToMock(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  if (!error.response) return true;
  return error.response.status === 404;
}

function computeMemberCounts(allAppointments: AppointmentListItem[]): AppointmentMemberCountItem[] {
  return MOCK_APPOINTMENT_MEMBERS.map((member) => ({
    ...member,
    appointmentCount: allAppointments.filter((apt) => apt.patientId === member.id).length,
  }));
}

function computeTabCounts(memberScopedAppointments: AppointmentListItem[]): AppointmentTabCounts {
  return {
    all: memberScopedAppointments.length,
    upcoming: memberScopedAppointments.filter((apt) => UPCOMING_STATUSES.includes(apt.status))
      .length,
    completed: memberScopedAppointments.filter((apt) => COMPLETED_STATUSES.includes(apt.status))
      .length,
    cancelled: memberScopedAppointments.filter((apt) => CANCELLED_STATUSES.includes(apt.status))
      .length,
  };
}

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

  const hasToken =
    typeof window !== 'undefined' && Boolean(window.localStorage?.getItem('medflow.access_token'));

  if (hasToken) {
    try {
      const { data } = await apiClient.get<AppointmentListResponse>('/appointments/me', {
        params: { memberId, tab, keyword, dateFrom, dateTo, page, pageSize },
      });
      return data;
    } catch (error) {
      if (!shouldFallbackToMock(error)) {
        throw error;
      }
    }
  }

  const allAppointments = [...MOCK_APPOINTMENTS];
  const memberCounts = computeMemberCounts(allAppointments);

  // 1. Lọc theo thành viên gia đình
  let memberScoped = allAppointments;
  if (memberId && memberId !== 'ALL') {
    memberScoped = memberScoped.filter(
      (apt) =>
        apt.patientId === memberId || apt.patientCode.toLowerCase() === memberId.toLowerCase(),
    );
  }

  // 2. Lọc theo từ khóa tìm kiếm & khoảng ngày trước khi tính số lượng trên mỗi tab
  if (keyword.trim()) {
    const q = keyword.trim().toLowerCase().replace(/^#/, '');
    memberScoped = memberScoped.filter(
      (apt) =>
        apt.appointmentCode.toLowerCase().includes(q) ||
        apt.doctorName.toLowerCase().includes(q) ||
        apt.specialtyName.toLowerCase().includes(q) ||
        apt.serviceName.toLowerCase().includes(q) ||
        apt.serviceCode.toLowerCase().includes(q) ||
        apt.patientName.toLowerCase().includes(q) ||
        apt.roomName.toLowerCase().includes(q),
    );
  }

  if (dateFrom) {
    memberScoped = memberScoped.filter((apt) => apt.scheduledDate >= dateFrom);
  }

  if (dateTo) {
    memberScoped = memberScoped.filter((apt) => apt.scheduledDate <= dateTo);
  }

  const tabCounts = computeTabCounts(memberScoped);

  // 3. Lọc theo tab trạng thái
  let filtered = memberScoped;
  if (tab === 'UPCOMING') {
    filtered = filtered.filter((apt) => UPCOMING_STATUSES.includes(apt.status));
  } else if (tab === 'COMPLETED') {
    filtered = filtered.filter((apt) => COMPLETED_STATUSES.includes(apt.status));
  } else if (tab === 'CANCELLED') {
    filtered = filtered.filter((apt) => CANCELLED_STATUSES.includes(apt.status));
  }

  const total = filtered.length;
  const safePageSize = Math.max(1, pageSize);
  const totalPages = Math.max(1, Math.ceil(total / safePageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const startIndex = (safePage - 1) * safePageSize;
  const items = filtered.slice(startIndex, startIndex + safePageSize);

  return {
    items,
    total,
    page: safePage,
    pageSize: safePageSize,
    totalPages,
    tabCounts,
    memberCounts,
    totalAllMembersCount: allAppointments.length,
  };
}
