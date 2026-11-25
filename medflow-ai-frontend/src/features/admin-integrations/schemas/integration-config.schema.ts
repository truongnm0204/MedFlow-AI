import { z } from 'zod';

import type {
  IntegrationProviderItemDto,
  UpdateIntegrationConfigPayload,
} from '../types/integration-config.types';

const digits6Pattern = /^\d{6}$/;
const digits6To19Pattern = /^\d{6,19}$/;
const alphanumericPattern = /^[A-Za-z0-9]+$/;
const basicEmailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export interface IntegrationFormState {
  enabled: boolean;
  reason: string;
  // VietQR
  bankBin: string;
  accountNumber: string;
  accountName: string;
  transferPrefix: string;
  pendingExpiryMinutes: string;
  // SMTP
  fromDisplayName: string;
  replyToEmail: string;
  subjectPrefix: string;
  smtpTimeoutSeconds: string;
  // AI
  providerName: string;
  modelName: string;
  aiTimeoutSeconds: string;
  maxSuggestions: string;
  disclaimerTemplate: string;
}

export type IntegrationFormFieldErrors = Partial<Record<keyof IntegrationFormState, string>>;

export function buildInitialFormState(item: IntegrationProviderItemDto): IntegrationFormState {
  return {
    enabled: item.enabled,
    reason: '',
    bankBin: item.vietQr?.bankBin ?? '',
    accountNumber: item.vietQr?.accountNumber ?? '',
    accountName: item.vietQr?.accountName ?? '',
    transferPrefix: item.vietQr?.transferPrefix ?? 'MF',
    pendingExpiryMinutes: String(item.vietQr?.pendingExpiryMinutes ?? 15),
    fromDisplayName: item.smtp?.fromDisplayName ?? 'MedFlow AI',
    replyToEmail: item.smtp?.replyToEmail ?? '',
    subjectPrefix: item.smtp?.subjectPrefix ?? '[MedFlow]',
    smtpTimeoutSeconds: String(item.smtp?.timeoutSeconds ?? 30),
    providerName: item.ai?.providerName ?? 'GEMINI',
    modelName: item.ai?.modelName ?? 'gemini-2.5-flash',
    aiTimeoutSeconds: String(item.ai?.timeoutSeconds ?? 15),
    maxSuggestions: String(item.ai?.maxSuggestions ?? 3),
    disclaimerTemplate:
      item.ai?.disclaimerTemplate ??
      'Gợi ý từ AI chỉ mang tính tham khảo điều hướng khám, không thay thế chẩn đoán y khoa của bác sĩ.',
  };
}

const vietQrSchema = z
  .object({
    enabled: z.boolean(),
    bankBin: z.string().trim(),
    accountNumber: z.string().trim(),
    accountName: z.string().trim(),
    transferPrefix: z
      .string()
      .trim()
      .min(2, 'Tiền tố nội dung chuyển khoản phải từ 2 đến 12 ký tự.')
      .max(12, 'Tiền tố nội dung chuyển khoản phải từ 2 đến 12 ký tự.')
      .regex(alphanumericPattern, 'Tiền tố chuyển khoản chỉ gồm chữ cái không dấu và chữ số.'),
    pendingExpiryMinutes: z.string().trim(),
    reason: z.string().trim().max(300, 'Lý do cập nhật không vượt quá 300 ký tự.'),
  })
  .superRefine((val, ctx) => {
    const hasAnyAccount = val.bankBin !== '' || val.accountNumber !== '' || val.accountName !== '';
    if (val.enabled || hasAnyAccount) {
      if (!digits6Pattern.test(val.bankBin)) {
        ctx.addIssue({
          code: 'custom',
          path: ['bankBin'],
          message: 'Mã BIN ngân hàng phải gồm đúng 6 chữ số (VD: 970422).',
        });
      }
      if (!digits6To19Pattern.test(val.accountNumber)) {
        ctx.addIssue({
          code: 'custom',
          path: ['accountNumber'],
          message: 'Số tài khoản nhận VietQR phải gồm từ 6 đến 19 chữ số.',
        });
      }
      if (val.accountName.length < 2 || val.accountName.length > 50) {
        ctx.addIssue({
          code: 'custom',
          path: ['accountName'],
          message: 'Tên chủ tài khoản nhận VietQR phải từ 2 đến 50 ký tự.',
        });
      }
    }

    const expiry = Number(val.pendingExpiryMinutes);
    if (!Number.isInteger(expiry) || expiry < 1 || expiry > 120) {
      ctx.addIssue({
        code: 'custom',
        path: ['pendingExpiryMinutes'],
        message: 'Thời gian hiệu lực mã QR thanh toán phải từ 1 đến 120 phút.',
      });
    }
  });

const smtpSchema = z
  .object({
    enabled: z.boolean(),
    fromDisplayName: z
      .string()
      .trim()
      .min(2, 'Tên hiển thị người gửi phải từ 2 đến 80 ký tự.')
      .max(80, 'Tên hiển thị người gửi phải từ 2 đến 80 ký tự.'),
    replyToEmail: z.string().trim().max(120, 'Email phản hồi không vượt quá 120 ký tự.'),
    subjectPrefix: z
      .string()
      .trim()
      .min(2, 'Tiền tố tiêu đề Email phải từ 2 đến 30 ký tự.')
      .max(30, 'Tiền tố tiêu đề Email phải từ 2 đến 30 ký tự.'),
    smtpTimeoutSeconds: z.string().trim(),
    reason: z.string().trim().max(300, 'Lý do cập nhật không vượt quá 300 ký tự.'),
  })
  .superRefine((val, ctx) => {
    if (val.replyToEmail !== '' && !basicEmailPattern.test(val.replyToEmail)) {
      ctx.addIssue({
        code: 'custom',
        path: ['replyToEmail'],
        message: 'Địa chỉ email phản hồi (Reply-To) không đúng định dạng.',
      });
    }

    const timeout = Number(val.smtpTimeoutSeconds);
    if (!Number.isInteger(timeout) || timeout < 5 || timeout > 120) {
      ctx.addIssue({
        code: 'custom',
        path: ['smtpTimeoutSeconds'],
        message: 'Thời gian chờ kết nối SMTP phải từ 5 đến 120 giây.',
      });
    }
  });

const aiSchema = z
  .object({
    enabled: z.boolean(),
    providerName: z.enum(['GEMINI', 'OPENAI', 'DETERMINISTIC_CLINICAL'], {
      error: 'Nhà cung cấp AI không hợp lệ.',
    }),
    modelName: z
      .string()
      .trim()
      .min(2, 'Tên mô hình AI (Model) phải từ 2 đến 80 ký tự.')
      .max(80, 'Tên mô hình AI (Model) phải từ 2 đến 80 ký tự.'),
    aiTimeoutSeconds: z.string().trim(),
    maxSuggestions: z.string().trim(),
    disclaimerTemplate: z
      .string()
      .trim()
      .min(10, 'Nội dung tuyên bố miễn trừ y khoa phải từ 10 đến 300 ký tự.')
      .max(300, 'Nội dung tuyên bố miễn trừ y khoa phải từ 10 đến 300 ký tự.'),
    reason: z.string().trim().max(300, 'Lý do cập nhật không vượt quá 300 ký tự.'),
  })
  .superRefine((val, ctx) => {
    const timeout = Number(val.aiTimeoutSeconds);
    if (!Number.isInteger(timeout) || timeout < 3 || timeout > 60) {
      ctx.addIssue({
        code: 'custom',
        path: ['aiTimeoutSeconds'],
        message: 'Thời gian chờ phản hồi AI phải từ 3 đến 60 giây.',
      });
    }

    const maxSug = Number(val.maxSuggestions);
    if (!Number.isInteger(maxSug) || maxSug < 1 || maxSug > 5) {
      ctx.addIssue({
        code: 'custom',
        path: ['maxSuggestions'],
        message: 'Số lượng gợi ý tối đa phải từ 1 đến 5.',
      });
    }
  });

export interface ValidateIntegrationFormResult {
  isValid: boolean;
  payload?: UpdateIntegrationConfigPayload;
  errors: IntegrationFormFieldErrors;
}

export function validateIntegrationForm(
  providerKey: IntegrationProviderItemDto['providerKey'],
  state: IntegrationFormState,
  hospitalId: number,
): ValidateIntegrationFormResult {
  if (providerKey === 'VIETQR_SEPAY') {
    const res = vietQrSchema.safeParse(state);
    if (!res.success) {
      const errors: IntegrationFormFieldErrors = {};
      for (const issue of res.error.issues) {
        const key = issue.path[0] as keyof IntegrationFormState | undefined;
        if (key && !errors[key]) errors[key] = issue.message;
      }
      return { isValid: false, errors };
    }

    return {
      isValid: true,
      errors: {},
      payload: {
        hospitalId,
        enabled: res.data.enabled,
        bankBin: res.data.bankBin || undefined,
        accountNumber: res.data.accountNumber || undefined,
        accountName: res.data.accountName ? res.data.accountName.toUpperCase() : undefined,
        transferPrefix: res.data.transferPrefix.toUpperCase(),
        pendingExpiryMinutes: Number(res.data.pendingExpiryMinutes),
        reason: res.data.reason || undefined,
      },
    };
  }

  if (providerKey === 'SMTP_EMAIL') {
    const res = smtpSchema.safeParse(state);
    if (!res.success) {
      const errors: IntegrationFormFieldErrors = {};
      for (const issue of res.error.issues) {
        const key = issue.path[0] as keyof IntegrationFormState | undefined;
        if (key && !errors[key]) errors[key] = issue.message;
      }
      return { isValid: false, errors };
    }

    return {
      isValid: true,
      errors: {},
      payload: {
        hospitalId,
        enabled: res.data.enabled,
        fromDisplayName: res.data.fromDisplayName,
        replyToEmail: res.data.replyToEmail,
        subjectPrefix: res.data.subjectPrefix,
        timeoutSeconds: Number(res.data.smtpTimeoutSeconds),
        reason: res.data.reason || undefined,
      },
    };
  }

  const res = aiSchema.safeParse(state);
  if (!res.success) {
    const errors: IntegrationFormFieldErrors = {};
    for (const issue of res.error.issues) {
      const key = issue.path[0] as keyof IntegrationFormState | undefined;
      if (key && !errors[key]) errors[key] = issue.message;
    }
    return { isValid: false, errors };
  }

  return {
    isValid: true,
    errors: {},
    payload: {
      hospitalId,
      enabled: res.data.enabled,
      providerName: res.data.providerName,
      modelName: res.data.modelName,
      timeoutSeconds: Number(res.data.aiTimeoutSeconds),
      maxSuggestions: Number(res.data.maxSuggestions),
      disclaimerTemplate: res.data.disclaimerTemplate,
      reason: res.data.reason || undefined,
    },
  };
}
