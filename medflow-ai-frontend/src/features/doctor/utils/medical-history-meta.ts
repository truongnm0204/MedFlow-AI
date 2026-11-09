import type {
  MedicalHistoryFilterStatus,
  MedicalHistoryLookupMode,
} from '../types/medical-history.types';
import type { DoctorQueueBadgeTone } from './doctor-assigned-queue-meta';

export const MEDICAL_HISTORY_STATUS_FILTER_OPTIONS: {
  id: MedicalHistoryFilterStatus;
  label: string;
}[] = [
  { id: 'ALL', label: 'Tất cả trạng thái' },
  { id: 'COMPLETED', label: 'Đã hoàn tất' },
  { id: 'FINALIZED', label: 'Bệnh án đã chốt' },
  { id: 'IN_PROGRESS', label: 'Đang khám' },
  { id: 'AWAITING_RESULTS', label: 'Chờ kết quả CLS' },
  { id: 'CALLED', label: 'Đã gọi vào' },
  { id: 'WAITING', label: 'Chờ khám' },
  { id: 'DRAFT', label: 'Bản nháp' },
];

export const MEDICAL_HISTORY_LIMIT_OPTIONS = [10, 20, 30, 50] as const;

export function getVisitStatusMeta(status: string): {
  label: string;
  tone: DoctorQueueBadgeTone;
} {
  const normalized = status?.trim().toUpperCase() ?? '';
  switch (normalized) {
    case 'COMPLETED':
      return { label: 'Đã hoàn tất', tone: 'emerald' };
    case 'IN_PROGRESS':
    case 'IN_CONSULTATION':
      return { label: 'Đang khám', tone: 'sky' };
    case 'AWAITING_RESULTS':
      return { label: 'Chờ kết quả CLS', tone: 'amber' };
    case 'CALLED':
      return { label: 'Đã gọi vào', tone: 'sky' };
    case 'WAITING':
    case 'CHECKED_IN':
      return { label: 'Chờ khám', tone: 'amber' };
    case 'CANCELLED':
    case 'NO_SHOW':
      return { label: 'Đã hủy / Vắng mặt', tone: 'rose' };
    default:
      return { label: status || 'Không rõ', tone: 'slate' };
  }
}

export function getRecordStatusMeta(status: string): {
  label: string;
  tone: DoctorQueueBadgeTone;
} {
  const normalized = status?.trim().toUpperCase() ?? '';
  switch (normalized) {
    case 'FINALIZED':
      return { label: 'Bệnh án đã chốt', tone: 'emerald' };
    case 'DRAFT':
      return { label: 'Bản nháp lâm sàng', tone: 'amber' };
    default:
      return { label: status || 'Chưa cập nhật', tone: 'slate' };
  }
}

export function getDiagnosticOrderStatusMeta(
  status: string,
  isResultFinalized: boolean,
): {
  label: string;
  tone: DoctorQueueBadgeTone;
} {
  if (isResultFinalized) {
    return { label: 'Đã có kết quả chốt', tone: 'emerald' };
  }

  const normalized = status?.trim().toUpperCase() ?? '';
  switch (normalized) {
    case 'COMPLETED':
    case 'FINALIZED':
      return { label: 'Đã có kết quả', tone: 'emerald' };
    case 'IN_PROGRESS':
    case 'CONFIRMED':
      return { label: 'Đang thực hiện', tone: 'sky' };
    case 'ORDERED':
    case 'PENDING':
      return { label: 'Đã chỉ định', tone: 'amber' };
    case 'CANCELLED':
      return { label: 'Đã hủy', tone: 'rose' };
    default:
      return { label: status || 'Đang xử lý', tone: 'slate' };
  }
}

export function getPrescriptionStatusMeta(status: string): {
  label: string;
  tone: DoctorQueueBadgeTone;
} {
  const normalized = status?.trim().toUpperCase() ?? '';
  switch (normalized) {
    case 'DISPENSED':
    case 'COMPLETED':
      return { label: 'Đã cấp phát', tone: 'emerald' };
    case 'ISSUED':
    case 'CONFIRMED':
    case 'FINALIZED':
      return { label: 'Đã phát hành', tone: 'sky' };
    case 'DRAFT':
      return { label: 'Đơn nháp', tone: 'amber' };
    case 'CANCELLED':
      return { label: 'Đã hủy', tone: 'rose' };
    default:
      return { label: status || 'Đã kê', tone: 'slate' };
  }
}

export function getGenderLabel(gender: string): string {
  const normalized = gender?.trim().toUpperCase() ?? '';
  if (normalized === 'MALE' || normalized === 'NAM') return 'Nam';
  if (normalized === 'FEMALE' || normalized === 'NỮ' || normalized === 'NU') return 'Nữ';
  return 'Khác';
}

export interface ValidateMedicalHistoryFilterFormInput {
  identifier: string;
  mode: MedicalHistoryLookupMode;
  status: string;
  specialtyCode: string;
  fromDate: string;
  toDate: string;
  keyword: string;
  limit: number;
}

export function validateMedicalHistoryFilterForm(
  input: ValidateMedicalHistoryFilterFormInput,
): string | null {
  const rawIdentifier = input.identifier.trim();
  const cleanedIdentifier = rawIdentifier.startsWith('#')
    ? rawIdentifier.slice(1).trim()
    : rawIdentifier;

  if (!cleanedIdentifier) {
    return 'Vui lòng nhập mã bệnh nhân hoặc mã lượt khám.';
  }

  if (/^-?\d+$/.test(cleanedIdentifier)) {
    if (Number(cleanedIdentifier) <= 0) {
      return input.mode === 'visit'
        ? 'ID lượt khám phải lớn hơn 0.'
        : 'ID bệnh nhân phải lớn hơn 0.';
    }
  } else if (cleanedIdentifier.length > 40 || !/^[A-Za-z0-9_-]+$/.test(cleanedIdentifier)) {
    return input.mode === 'visit'
      ? 'Mã định danh lượt khám không hợp lệ.'
      : 'Mã định danh bệnh nhân không hợp lệ.';
  }

  if (input.keyword.trim().length > 100) {
    return 'Từ khóa tìm kiếm không được vượt quá 100 ký tự.';
  }

  const trimmedSpecialty = input.specialtyCode.trim();
  if (trimmedSpecialty && trimmedSpecialty.toUpperCase() !== 'ALL') {
    if (trimmedSpecialty.length > 30 || !/^[A-Za-z0-9_-]+$/.test(trimmedSpecialty)) {
      return 'Mã chuyên khoa lọc không hợp lệ.';
    }
  }

  const trimmedFromDate = input.fromDate.trim();
  if (trimmedFromDate && !/^\d{4}-\d{2}-\d{2}$/.test(trimmedFromDate)) {
    return 'Khoảng ngày lọc không hợp lệ (định dạng YYYY-MM-DD).';
  }

  const trimmedToDate = input.toDate.trim();
  if (trimmedToDate && !/^\d{4}-\d{2}-\d{2}$/.test(trimmedToDate)) {
    return 'Khoảng ngày lọc không hợp lệ (định dạng YYYY-MM-DD).';
  }

  if (trimmedFromDate && trimmedToDate && trimmedFromDate > trimmedToDate) {
    return 'Ngày bắt đầu không được lớn hơn ngày kết thúc.';
  }

  if (!Number.isInteger(input.limit) || input.limit < 1 || input.limit > 50) {
    return 'Số lượng lịch sử khám hiển thị phải từ 1 đến 50.';
  }

  return null;
}
