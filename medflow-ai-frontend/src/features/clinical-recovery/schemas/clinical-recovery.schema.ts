import { z } from 'zod';

/** Mirrors `ClinicalSessionRecoveryConstants.ReasonMinLength / ReasonMaxLength`. */
export const RECOVERY_REASON_MIN = 3;
export const RECOVERY_REASON_MAX = 500;
export const RECOVERY_IDEMPOTENCY_KEY_MAX = 128;

const SAFE_TEXT_REGEX = /^[^<>]*$/;

const recoveryReasonField = z
  .string()
  .trim()
  .min(
    RECOVERY_REASON_MIN,
    `Lý do phải từ ${RECOVERY_REASON_MIN} đến ${RECOVERY_REASON_MAX} ký tự.`,
  )
  .max(
    RECOVERY_REASON_MAX,
    `Lý do phải từ ${RECOVERY_REASON_MIN} đến ${RECOVERY_REASON_MAX} ký tự.`,
  )
  .refine((value) => SAFE_TEXT_REGEX.test(value), 'Lý do không được chứa ký tự < hoặc >.');

const stateVersionField = z
  .number({ message: 'Phiên bản trạng thái không hợp lệ.' })
  .int('Phiên bản trạng thái không hợp lệ.')
  .nonnegative('Phiên bản trạng thái không hợp lệ.');

export const createRecoveryRequestSchema = z.object({
  queueEntryId: z.number().int().positive('Cần chọn lượt hàng đợi cần phục hồi.'),
  expectedStateVersion: stateVersionField,
  reason: recoveryReasonField,
});

export const grantRecoverySchema = z.object({
  authorizedDoctorId: z
    .number({ message: 'Cần chọn bác sĩ nhận uỷ quyền.' })
    .int('Mã bác sĩ không hợp lệ.')
    .positive('Cần chọn bác sĩ nhận uỷ quyền.'),
  expectedStateVersion: stateVersionField,
  reason: recoveryReasonField,
});

export const recoveryDecisionSchema = z.object({
  reason: recoveryReasonField,
  expectedStateVersion: stateVersionField.optional(),
});

export const confirmRecoverySchema = z.object({
  expectedStateVersion: stateVersionField,
  confirmInterrupted: z
    .boolean()
    .refine(
      (value) => value,
      'Bác sĩ phải xác nhận phiên khám thực sự bị gián đoạn trước khi phục hồi.',
    ),
  reason: recoveryReasonField,
  idempotencyKey: z
    .string()
    .trim()
    .max(
      RECOVERY_IDEMPOTENCY_KEY_MAX,
      `Khoá idempotency tối đa ${RECOVERY_IDEMPOTENCY_KEY_MAX} ký tự.`,
    )
    .optional(),
});

export type CreateRecoveryRequestFormValues = z.infer<typeof createRecoveryRequestSchema>;
export type GrantRecoveryFormValues = z.infer<typeof grantRecoverySchema>;
export type RecoveryDecisionFormValues = z.infer<typeof recoveryDecisionSchema>;
export type ConfirmRecoveryFormValues = z.infer<typeof confirmRecoverySchema>;
