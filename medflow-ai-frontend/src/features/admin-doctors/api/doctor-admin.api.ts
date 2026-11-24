import axios from 'axios';

import { apiClient } from '@/shared/lib/axios';

import type { Doctor, WorkHistoryEntry } from '../types/doctor.types';

export interface DoctorAdminApiItemDto {
  id: number;
  doctorCode: string;
  staffId?: number | null;
  staffCode?: string | null;
  fullName: string;
  email?: string | null;
  phone?: string | null;
  degree?: string | null;
  specialtyId?: number | null;
  specialtyName?: string | null;
  departmentId?: number | null;
  departmentName?: string | null;
  licenseNumber?: string | null;
  yearsOfExperience?: number | null;
  isActive: boolean;
  profileImageUrl?: string | null;
  professionalBio?: string | null;
  qualificationsSummary?: string | null;
  room?: string | null;
  history?: WorkHistoryEntry[];
  hasPendingAppointments?: boolean;
  pendingAppointmentsCount?: number;
}

interface DoctorAdminListApiResponseDto {
  data?: DoctorAdminApiItemDto[];
  total?: number;
  page?: number;
  pageSize?: number;
  totalPages?: number;
}

interface ProblemDetailsBody {
  detail?: string;
  title?: string;
  message?: string;
}

export interface AdminDoctorRecord extends Doctor {
  backendId: string;
}

function extractDoctorApiError(error: unknown, fallbackMessage: string): Error {
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ProblemDetailsBody | undefined;
    const msg = body?.detail || body?.message || body?.title;
    if (msg && msg.trim().length > 0) return new Error(msg.trim());
  }
  if (error instanceof Error) return error;
  return new Error(fallbackMessage);
}

function parseQualifications(
  raw?: string | null,
  explicitHistory?: WorkHistoryEntry[],
): { scope: string; history: WorkHistoryEntry[] } {
  if (!raw) {
    return { scope: '', history: explicitHistory ?? [] };
  }
  const parts = raw.split('\n---\n');
  const scope = parts[0]?.trim() ?? '';
  if (explicitHistory && explicitHistory.length > 0) {
    return { scope, history: explicitHistory };
  }
  if (parts.length > 1 && parts[1]) {
    const history = parts[1]
      .split('\n')
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line) => {
        const sep = line.indexOf(': ');
        if (sep > 0) {
          return { year: line.slice(0, sep).trim(), description: line.slice(sep + 2).trim() };
        }
        return { year: '', description: line };
      });
    return { scope, history };
  }
  return { scope, history: [] };
}

function serializeQualifications(scope: string, history: WorkHistoryEntry[]): string {
  const validHistory = history.filter((h) => h.year.trim() || h.description.trim());
  if (validHistory.length === 0) return scope.trim();
  const serializedHistory = validHistory
    .map((h) =>
      h.year.trim() ? `${h.year.trim()}: ${h.description.trim()}` : h.description.trim(),
    )
    .join('\n');
  return `${scope.trim()}\n---\n${serializedHistory}`;
}

export function mapAdminDoctorDto(dto: DoctorAdminApiItemDto): AdminDoctorRecord {
  const { scope, history } = parseQualifications(dto.qualificationsSummary, dto.history);
  return {
    backendId: String(dto.id),
    id: dto.doctorCode || `DOC-${String(dto.id).padStart(4, '0')}`,
    staffId: dto.staffCode || (dto.staffId ? `STF-${dto.staffId}` : ''),
    name: dto.fullName,
    email: dto.email || '',
    degree: dto.degree || '',
    specialty: dto.specialtyName || '',
    department: dto.departmentName || '',
    license: dto.licenseNumber || '',
    room: dto.room || 'Chưa phân buồng',
    phone: dto.phone || '',
    experience: dto.yearsOfExperience ?? 0,
    scope,
    biography: dto.professionalBio || '',
    active: dto.isActive,
    portrait: dto.profileImageUrl || undefined,
    history,
    hasPendingAppointments: dto.hasPendingAppointments ?? (dto.pendingAppointmentsCount ?? 0) > 0,
    pendingAppointmentsCount: dto.pendingAppointmentsCount ?? 0,
  };
}

export async function fetchAdminDoctorsApi(): Promise<AdminDoctorRecord[]> {
  try {
    const { data } = await apiClient.get<DoctorAdminListApiResponseDto | DoctorAdminApiItemDto[]>(
      '/admin/doctors',
      { params: { page: 1, pageSize: 200 } },
    );
    const raw = Array.isArray(data) ? data : (data.data ?? []);
    return raw.map(mapAdminDoctorDto);
  } catch (error) {
    throw extractDoctorApiError(error, 'Không thể tải danh sách bác sĩ.');
  }
}

export async function createAdminDoctorApi(doctor: Doctor): Promise<AdminDoctorRecord> {
  try {
    const { data } = await apiClient.post<DoctorAdminApiItemDto>('/admin/doctors', {
      doctorCode: doctor.id,
      id: doctor.id,
      staffCode: doctor.staffId,
      department: doctor.department,
      specialty: doctor.specialty,
      degree: doctor.degree,
      license: doctor.license,
      experience: doctor.experience,
      biography: doctor.biography,
      scope: serializeQualifications(doctor.scope, doctor.history),
      portrait: doctor.portrait ?? null,
      phone: doctor.phone,
      active: doctor.active,
    });
    return mapAdminDoctorDto({
      ...data,
      room: doctor.room,
      history: doctor.history,
    });
  } catch (error) {
    throw extractDoctorApiError(error, 'Không thể tạo hồ sơ bác sĩ.');
  }
}

export async function updateAdminDoctorApi(
  idOrCode: string,
  doctor: Doctor,
): Promise<AdminDoctorRecord> {
  try {
    const { data } = await apiClient.put<DoctorAdminApiItemDto>(
      `/admin/doctors/${encodeURIComponent(idOrCode)}`,
      {
        name: doctor.name,
        fullName: doctor.name,
        department: doctor.department,
        specialty: doctor.specialty,
        degree: doctor.degree,
        license: doctor.license,
        yearsOfExperience: doctor.experience,
        profileImageUrl: doctor.portrait ?? null,
        biography: doctor.biography,
        qualificationsSummary: serializeQualifications(doctor.scope, doctor.history),
        isActive: doctor.active,
      },
    );
    return mapAdminDoctorDto({
      ...data,
      room: doctor.room,
      history: doctor.history,
    });
  } catch (error) {
    throw extractDoctorApiError(error, 'Không thể cập nhật hồ sơ bác sĩ.');
  }
}

export async function deactivateAdminDoctorApi(idOrCode: string, reason?: string): Promise<void> {
  try {
    await apiClient.patch(`/admin/doctors/${encodeURIComponent(idOrCode)}/deactivate`, {
      reason: reason ?? 'Tạm ngừng nhận lịch từ cổng quản trị',
    });
  } catch (error) {
    throw extractDoctorApiError(error, 'Không thể ngừng hoạt động bác sĩ.');
  }
}

export async function reactivateAdminDoctorApi(idOrCode: string): Promise<void> {
  try {
    await apiClient.patch(`/admin/doctors/${encodeURIComponent(idOrCode)}/reactivate`, {});
  } catch (error) {
    throw extractDoctorApiError(error, 'Không thể kích hoạt lại hồ sơ bác sĩ.');
  }
}
