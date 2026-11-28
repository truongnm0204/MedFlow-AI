import type {
  PatientIdentityStatus,
  PatientUnverifiedReason,
} from '@/features/ekyc/types/ekyc.types';

/** Nhãn hiển thị cho trạng thái xác minh danh tính bệnh nhân. */
export const PATIENT_IDENTITY_STATUS_LABELS: Record<PatientIdentityStatus, string> = {
  VERIFIED: 'Đã xác minh',
  UNVERIFIED: 'Chưa xác minh',
};

/** Nhãn tiếng Việt cho 6 lý do tạo hồ sơ không xác minh (owner-approved). */
export const PATIENT_UNVERIFIED_REASON_LABELS: Record<PatientUnverifiedReason, string> = {
  CHILD: 'Trẻ em',
  FOREIGNER: 'Người nước ngoài',
  NO_VALID_DOCUMENT: 'Không có giấy tờ hợp lệ',
  CONSENT_DECLINED: 'Không đồng ý xác minh điện tử',
  VNPT_UNAVAILABLE: 'Hệ thống xác minh không khả dụng',
  QUOTA_EXHAUSTED: 'Hết hạn mức xác minh',
};
