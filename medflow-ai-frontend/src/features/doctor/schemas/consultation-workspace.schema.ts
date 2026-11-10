import { z } from 'zod';

export const consultationIdentifierSchema = z
  .string()
  .trim()
  .min(1, 'Vui lòng nhập mã lượt khám hoặc số phiếu hàng đợi.')
  .max(40, 'Mã lượt khám tối đa 40 ký tự.')
  .regex(
    /^[A-Za-z0-9#_-]+$/,
    'Mã lượt khám chỉ được chứa chữ cái, chữ số, dấu #, gạch ngang (-) hoặc gạch dưới (_).',
  );

export const openConsultationWorkspaceSchema = z.object({
  expectedStateVersion: z
    .number()
    .int('Phiên bản trạng thái phải là số nguyên.')
    .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
    .nullable()
    .optional(),
  notes: z
    .string()
    .trim()
    .max(300, 'Ghi chú tối đa 300 ký tự.')
    .refine(
      (val) => !val.includes('<') && !val.includes('>'),
      'Ghi chú không được chứa ký tự < hoặc >.',
    )
    .nullable()
    .optional(),
});

export type ConsultationIdentifierInput = z.infer<typeof consultationIdentifierSchema>;
export type OpenConsultationWorkspaceInput = z.infer<typeof openConsultationWorkspaceSchema>;

function clinicalTextFieldSchema(fieldLabel: string, maxLength: number) {
  return z
    .string()
    .trim()
    .max(maxLength, `${fieldLabel} tối đa ${maxLength} ký tự.`)
    .refine(
      (val) => !val.includes('<') && !val.includes('>'),
      `${fieldLabel} không được chứa ký tự < hoặc >.`,
    )
    .nullable()
    .optional();
}

const bpSystolicFieldSchema = z
  .number()
  .int('Huyết áp tâm thu phải là số nguyên.')
  .min(40, 'Huyết áp tâm thu phải nằm trong khoảng 40–300 mmHg.')
  .max(300, 'Huyết áp tâm thu phải nằm trong khoảng 40–300 mmHg.')
  .nullable()
  .optional();

const bpDiastolicFieldSchema = z
  .number()
  .int('Huyết áp tâm trương phải là số nguyên.')
  .min(20, 'Huyết áp tâm trương phải nằm trong khoảng 20–200 mmHg.')
  .max(200, 'Huyết áp tâm trương phải nằm trong khoảng 20–200 mmHg.')
  .nullable()
  .optional();

const pulseBpmFieldSchema = z
  .number()
  .int('Mạch phải là số nguyên.')
  .min(20, 'Mạch phải nằm trong khoảng 20–300 lần/phút.')
  .max(300, 'Mạch phải nằm trong khoảng 20–300 lần/phút.')
  .nullable()
  .optional();

const temperatureCFieldSchema = z
  .number()
  .min(30, 'Nhiệt độ cơ thể phải nằm trong khoảng 30.0–45.0 °C.')
  .max(45, 'Nhiệt độ cơ thể phải nằm trong khoảng 30.0–45.0 °C.')
  .nullable()
  .optional();

const spo2PercentFieldSchema = z
  .number()
  .int('Chỉ số SpO2 phải là số nguyên.')
  .min(50, 'Chỉ số SpO2 phải nằm trong khoảng 50–100%.')
  .max(100, 'Chỉ số SpO2 phải nằm trong khoảng 50–100%.')
  .nullable()
  .optional();

const heightCmFieldSchema = z
  .number()
  .min(30, 'Chiều cao phải nằm trong khoảng 30–250 cm.')
  .max(250, 'Chiều cao phải nằm trong khoảng 30–250 cm.')
  .nullable()
  .optional();

const weightKgFieldSchema = z
  .number()
  .min(1, 'Cân nặng phải nằm trong khoảng 1–500 kg.')
  .max(500, 'Cân nặng phải nằm trong khoảng 1–500 kg.')
  .nullable()
  .optional();

export const recordClinicalVitalSignsSchema = z.object({
  bpSystolicMmhg: bpSystolicFieldSchema,
  bpDiastolicMmhg: bpDiastolicFieldSchema,
  pulseBpm: pulseBpmFieldSchema,
  temperatureC: temperatureCFieldSchema,
  spo2Percent: spo2PercentFieldSchema,
  heightCm: heightCmFieldSchema,
  weightKg: weightKgFieldSchema,
});

export const recordClinicalExaminationSchema = z
  .object({
    presentingComplaint: clinicalTextFieldSchema('Lý do khám / triệu chứng chính', 1000),
    symptomHistory: clinicalTextFieldSchema('Bệnh sử & diễn tiến triệu chứng', 4000),
    relevantHistoryAllergies: clinicalTextFieldSchema('Tiền sử & dị ứng liên quan', 2000),
    vitalSigns: recordClinicalVitalSignsSchema.nullable().optional(),
    bpSystolicMmhg: bpSystolicFieldSchema,
    bpDiastolicMmhg: bpDiastolicFieldSchema,
    pulseBpm: pulseBpmFieldSchema,
    temperatureC: temperatureCFieldSchema,
    spo2Percent: spo2PercentFieldSchema,
    heightCm: heightCmFieldSchema,
    weightKg: weightKgFieldSchema,
    clinicalFindings: clinicalTextFieldSchema('Khám lâm sàng', 4000),
    clinicalConclusion: clinicalTextFieldSchema('Nhận định & kết luận lâm sàng ban đầu', 2000),
    amendmentReason: clinicalTextFieldSchema('Lý do cập nhật', 500),
    isConfirmed: z.boolean().nullable().optional(),
    expectedStateVersion: z
      .number()
      .int('Phiên bản trạng thái phải là số nguyên.')
      .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
    expectedRevisionNo: z
      .number()
      .int('Phiên bản bệnh án phải là số nguyên.')
      .min(1, 'Phiên bản bệnh án (ExpectedRevisionNo) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
  })
  .superRefine((data, ctx) => {
    const v = data.vitalSigns;
    const bpSystolic = v?.bpSystolicMmhg ?? data.bpSystolicMmhg ?? null;
    const bpDiastolic = v?.bpDiastolicMmhg ?? data.bpDiastolicMmhg ?? null;
    const pulseBpm = v?.pulseBpm ?? data.pulseBpm ?? null;
    const temperatureC = v?.temperatureC ?? data.temperatureC ?? null;
    const spo2Percent = v?.spo2Percent ?? data.spo2Percent ?? null;
    const heightCm = v?.heightCm ?? data.heightCm ?? null;
    const weightKg = v?.weightKg ?? data.weightKg ?? null;

    const hasSystolic = bpSystolic !== null && bpSystolic !== undefined;
    const hasDiastolic = bpDiastolic !== null && bpDiastolic !== undefined;

    if (hasSystolic !== hasDiastolic) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['vitalSigns', 'bpSystolicMmhg'],
        message: 'Huyết áp tâm thu và huyết áp tâm trương phải được nhập cùng nhau.',
      });
    } else if (hasSystolic && hasDiastolic && bpSystolic <= bpDiastolic) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['vitalSigns', 'bpSystolicMmhg'],
        message: 'Huyết áp tâm thu phải lớn hơn huyết áp tâm trương.',
      });
    }

    const hasAnyText = Boolean(
      (data.presentingComplaint && data.presentingComplaint.trim().length > 0) ||
      (data.symptomHistory && data.symptomHistory.trim().length > 0) ||
      (data.relevantHistoryAllergies && data.relevantHistoryAllergies.trim().length > 0) ||
      (data.clinicalFindings && data.clinicalFindings.trim().length > 0) ||
      (data.clinicalConclusion && data.clinicalConclusion.trim().length > 0),
    );

    const hasAnyVitalSign =
      hasSystolic ||
      hasDiastolic ||
      pulseBpm !== null ||
      temperatureC !== null ||
      spo2Percent !== null ||
      heightCm !== null ||
      weightKg !== null;

    if (!hasAnyText && !hasAnyVitalSign) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['presentingComplaint'],
        message:
          'Vui lòng nhập ít nhất một thông tin triệu chứng, dấu hiệu sinh tồn hoặc nhận xét khám lâm sàng.',
      });
    }
  });

export type RecordClinicalVitalSignsInput = z.infer<typeof recordClinicalVitalSignsSchema>;
export type RecordClinicalExaminationInput = z.infer<typeof recordClinicalExaminationSchema>;

export function calculateClinicalBmi(
  heightCm?: number | null,
  weightKg?: number | null,
): number | null {
  if (
    heightCm === null ||
    heightCm === undefined ||
    weightKg === null ||
    weightKg === undefined ||
    heightCm <= 0 ||
    weightKg <= 0
  ) {
    return null;
  }
  const heightM = heightCm / 100;
  const bmi = weightKg / (heightM * heightM);
  return Math.round(bmi * 10) / 10;
}

export function formatBloodPressureLabel(
  systolic?: number | null,
  diastolic?: number | null,
): string | null {
  if (
    systolic === null ||
    systolic === undefined ||
    diastolic === null ||
    diastolic === undefined
  ) {
    return null;
  }
  return `${systolic}/${diastolic} mmHg`;
}

const VALID_DIAGNOSTIC_PRIORITIES: Record<string, true> = {
  NORMAL: true,
  STANDARD: true,
  ROUTINE: true,
  URGENT: true,
  STAT: true,
};

export const diagnosticOrderPrioritySchema = z
  .string()
  .trim()
  .refine(
    (val) => val.length === 0 || Boolean(VALID_DIAGNOSTIC_PRIORITIES[val.toUpperCase()]),
    'Mức độ ưu tiên chỉ định cận lâm sàng không hợp lệ. Chỉ chấp nhận NORMAL, URGENT hoặc STAT.',
  )
  .transform((val): 'NORMAL' | 'URGENT' | 'STAT' => {
    const upper = val.trim().toUpperCase();
    if (upper === 'URGENT') return 'URGENT';
    if (upper === 'STAT') return 'STAT';
    return 'NORMAL';
  });

const diagnosticServiceIdFieldSchema = z
  .number()
  .int('ID dịch vụ cận lâm sàng phải là số nguyên.')
  .min(1, 'ID dịch vụ cận lâm sàng phải lớn hơn 0.')
  .nullable()
  .optional();

const diagnosticServiceCodeFieldSchema = z
  .string()
  .trim()
  .max(40, 'Mã dịch vụ cận lâm sàng không hợp lệ.')
  .refine(
    (val) => !val.includes('<') && !val.includes('>'),
    'Mã dịch vụ cận lâm sàng không hợp lệ.',
  )
  .nullable()
  .optional();

export const createDiagnosticOrderItemSchema = z
  .object({
    serviceId: diagnosticServiceIdFieldSchema,
    serviceCode: diagnosticServiceCodeFieldSchema,
    instruction: clinicalTextFieldSchema('Chỉ dẫn cận lâm sàng', 1000),
    priority: diagnosticOrderPrioritySchema.nullable().optional(),
    requiredForConclusion: z.boolean().nullable().optional(),
  })
  .superRefine((item, ctx) => {
    const hasServiceId = item.serviceId !== null && item.serviceId !== undefined;
    const trimmedCode = item.serviceCode?.trim() ?? '';
    const hasServiceCode = trimmedCode.length > 0;

    if (!hasServiceId && !hasServiceCode) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['serviceId'],
        message: 'Vui lòng cung cấp ID hoặc mã dịch vụ cận lâm sàng.',
      });
    }
  });

export const createDiagnosticOrderSchema = z
  .object({
    serviceId: diagnosticServiceIdFieldSchema,
    serviceCode: diagnosticServiceCodeFieldSchema,
    instruction: clinicalTextFieldSchema('Chỉ dẫn cận lâm sàng', 1000),
    priority: diagnosticOrderPrioritySchema.nullable().optional(),
    requiredForConclusion: z.boolean().nullable().optional(),
    items: z
      .array(createDiagnosticOrderItemSchema)
      .max(10, 'Mỗi lần chỉ định tối đa 10 dịch vụ cận lâm sàng.')
      .nullable()
      .optional(),
    expectedStateVersion: z
      .number()
      .int('Phiên bản trạng thái phải là số nguyên.')
      .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
    transitionToAwaitingResults: z.boolean().nullable().optional(),
    clinicalNote: clinicalTextFieldSchema('Ghi chú lâm sàng', 500),
  })
  .superRefine((data, ctx) => {
    const hasItems = Boolean(data.items && data.items.length > 0);
    const hasTopServiceId = data.serviceId !== null && data.serviceId !== undefined;
    const hasTopServiceCode = Boolean(data.serviceCode && data.serviceCode.trim().length > 0);

    if (!hasItems && !hasTopServiceId && !hasTopServiceCode) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['items'],
        message: 'Vui lòng chọn ít nhất một dịch vụ cận lâm sàng để chỉ định.',
      });
      return;
    }

    if (data.items && data.items.length > 0) {
      const seenIds = new Set<number>();
      const seenCodes = new Set<string>();

      data.items.forEach((item, index) => {
        if (item.serviceId !== null && item.serviceId !== undefined) {
          if (seenIds.has(item.serviceId)) {
            ctx.addIssue({
              code: z.ZodIssueCode.custom,
              path: ['items', index, 'serviceId'],
              message:
                'Không được chọn trùng lặp dịch vụ cận lâm sàng trong cùng một lần chỉ định.',
            });
          } else {
            seenIds.add(item.serviceId);
          }
        }

        const codeKey = item.serviceCode?.trim().toUpperCase();
        if (codeKey) {
          if (seenCodes.has(codeKey)) {
            ctx.addIssue({
              code: z.ZodIssueCode.custom,
              path: ['items', index, 'serviceCode'],
              message:
                'Không được chọn trùng lặp dịch vụ cận lâm sàng trong cùng một lần chỉ định.',
            });
          } else {
            seenCodes.add(codeKey);
          }
        }
      });
    }
  });

export type CreateDiagnosticOrderItemInput = z.infer<typeof createDiagnosticOrderItemSchema>;
export type CreateDiagnosticOrderInput = z.infer<typeof createDiagnosticOrderSchema>;

export const ADDITIONAL_DIAGNOSTIC_REASON_PRESETS = [
  'Chỉ định bổ sung sau khi xem kết quả cận lâm sàng ban đầu',
  'Diễn biến lâm sàng mới phát sinh trong phiên khám',
  'Cần xét nghiệm/chẩn đoán hình ảnh chuyên sâu để chẩn đoán phân biệt',
  'Theo dõi động học chỉ số cận lâm sàng bất thường',
] as const;

export interface FollowUpOrderContextSource {
  orderCode: string;
  serviceCode: string;
  serviceName: string;
  conclusion?: string | null;
}

export function buildFollowUpClinicalNoteFromOrder(source: FollowUpOrderContextSource): string {
  const cleanOrderCode = source.orderCode.replace(/[<>]/g, '').trim();
  const cleanServiceCode = source.serviceCode.replace(/[<>]/g, '').trim();
  const cleanConclusion = source.conclusion?.replace(/[<>]/g, '').trim() ?? '';

  const rawNote = cleanConclusion
    ? `Chỉ định bổ sung sau kết quả ${cleanServiceCode} (${cleanOrderCode}): ${cleanConclusion}`
    : `Chỉ định bổ sung sau kết quả ${cleanServiceCode} (${cleanOrderCode}) để làm rõ chẩn đoán lâm sàng.`;

  return rawNote.length > 500 ? `${rawNote.slice(0, 497).trimEnd()}...` : rawNote;
}

export function getTodayLocalIso(): string {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function isValidIsoCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }

  const [yearStr, monthStr, dayStr] = value.split('-');
  const year = Number(yearStr);
  const month = Number(monthStr);
  const day = Number(dayStr);

  if (month < 1 || month > 12 || day < 1 || day > 31) {
    return false;
  }

  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

export function createCompleteVisitSchema(referenceDateIso?: string) {
  return z.object({
    clinicalConclusion: z
      .string()
      .trim()
      .max(2000, 'Kết luận lâm sàng tối đa 2000 ký tự.')
      .refine(
        (val) => !val.includes('<') && !val.includes('>'),
        'Kết luận lâm sàng không được chứa ký tự < hoặc >.',
      )
      .refine(
        (val) => val.length === 0 || val.length >= 3,
        'Kết luận lâm sàng phải có ít nhất 3 ký tự.',
      )
      .nullable()
      .optional(),
    dischargeInstructions: clinicalTextFieldSchema('Hướng dẫn điều trị / dặn dò xuất viện', 2000),
    followUpInstructions: clinicalTextFieldSchema('Hướng dẫn tái khám', 1000),
    followUpDate: z
      .string()
      .trim()
      .superRefine((val, ctx) => {
        if (!val) {
          return;
        }
        if (!isValidIsoCalendarDate(val)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'Ngày hẹn tái khám không hợp lệ (định dạng YYYY-MM-DD).',
          });
          return;
        }
        const effectiveReferenceDate = referenceDateIso?.trim() || getTodayLocalIso();
        if (val <= effectiveReferenceDate) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'Ngày hẹn tái khám phải sau ngày khám hiện tại.',
          });
        }
      })
      .nullable()
      .optional(),
    warningSigns: clinicalTextFieldSchema('Dấu hiệu cảnh báo cần tái khám ngay', 1000),
    notes: clinicalTextFieldSchema('Ghi chú hoàn tất lượt khám', 500),
    completionNote: clinicalTextFieldSchema('Ghi chú hoàn tất lượt khám', 500),
    confirmNoPrescription: z.boolean().nullable().optional(),
    expectedStateVersion: z
      .number()
      .int('Phiên bản trạng thái phải là số nguyên.')
      .min(1, 'Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
    expectedRevisionNo: z
      .number()
      .int('Phiên bản bệnh án phải là số nguyên.')
      .min(1, 'Phiên bản bệnh án (ExpectedRevisionNo) phải lớn hơn hoặc bằng 1.')
      .nullable()
      .optional(),
  });
}

export const completeVisitSchema = createCompleteVisitSchema();

export type CompleteVisitInput = z.infer<typeof completeVisitSchema>;
