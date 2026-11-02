import { z } from 'zod';

export const examinationSlotSchema = z
  .object({
    code: z.string().min(1, 'Mã slot là bắt buộc'),
    serviceType: z.enum(['OUTPATIENT', 'DIAGNOSTIC', 'INPATIENT'], {
      message: 'Vui lòng chọn loại dịch vụ hợp lệ',
    }),
    serviceId: z.string().min(1, 'Vui lòng chọn dịch vụ khám'),
    serviceName: z.string().min(1, 'Tên dịch vụ là bắt buộc'),
    serviceCode: z.string().min(1, 'Mã dịch vụ là bắt buộc'),
    specialty: z.string().min(1, 'Chuyên khoa là bắt buộc'),
    date: z
      .string()
      .min(1, 'Vui lòng chọn ngày áp dụng')
      .regex(/^\d{4}-\d{2}-\d{2}$/, 'Định dạng ngày không hợp lệ (YYYY-MM-DD)'),
    startTime: z
      .string()
      .min(1, 'Vui lòng chọn giờ bắt đầu')
      .regex(/^([01]\d|2[0-3]):[0-5]\d$/, 'Định dạng giờ không hợp lệ (HH:mm)'),
    endTime: z
      .string()
      .min(1, 'Vui lòng chọn giờ kết thúc')
      .regex(/^([01]\d|2[0-3]):[0-5]\d$/, 'Định dạng giờ không hợp lệ (HH:mm)'),
    facility: z.string().min(1, 'Cơ sở là bắt buộc'),
    capacityMax: z
      .number({ message: 'Sức chứa phải là số' })
      .int('Sức chứa phải là số nguyên')
      .min(1, 'Sức chứa phải từ 1 lượt trở lên')
      .max(100, 'Sức chứa không được vượt quá 100 lượt'),
    room: z.string(),
    doctorId: z.string(),
    doctorName: z.string(),
    status: z.enum(['OPEN', 'FULL', 'CLOSED']),
  })
  .refine(
    (data) => {
      if (!data.startTime || !data.endTime) return true;
      return data.endTime > data.startTime;
    },
    {
      message: 'Giờ kết thúc phải sau giờ bắt đầu',
      path: ['endTime'],
    },
  );

export type ExaminationSlotFormValues = z.infer<typeof examinationSlotSchema>;

export const DEFAULT_SLOT_FORM_VALUES: ExaminationSlotFormValues = {
  code: 'SLT-HN-20261027-01',
  serviceType: 'OUTPATIENT',
  serviceId: 'srv-001',
  serviceName: 'Khám Nội tiết',
  serviceCode: 'EX-1-01',
  specialty: 'Nội tiết',
  date: '2026-10-27',
  startTime: '08:00',
  endTime: '08:20',
  facility: 'Cơ sở Hà Nội',
  capacityMax: 8,
  room: 'PK-201',
  doctorId: 'doc-001',
  doctorName: 'BS. Nguyễn Thu Lan',
  status: 'OPEN',
};

export const CLOSE_SLOT_REASONS = [
  {
    code: 'DOCTOR_UNAVAILABLE',
    label: 'Bác sĩ bận đột xuất / công tác / hội chẩn chuyên môn',
  },
  {
    code: 'EQUIPMENT_MAINTENANCE',
    label: 'Bảo trì buồng khám / trang thiết bị chuyên dụng',
  },
  {
    code: 'SCHEDULE_ADJUSTMENT',
    label: 'Điều chỉnh phân bổ lịch hoạt động của khoa / phòng',
  },
  {
    code: 'EMERGENCY',
    label: 'Sự cố vận hành khẩn cấp / bất khả kháng',
  },
  {
    code: 'OTHER',
    label: 'Lý do khác (ghi rõ lý do)',
  },
] as const;

export const slotCloseSchema = z
  .object({
    reasonCode: z.enum(
      ['DOCTOR_UNAVAILABLE', 'EQUIPMENT_MAINTENANCE', 'SCHEDULE_ADJUSTMENT', 'EMERGENCY', 'OTHER'],
      { message: 'Vui lòng chọn lý do đóng khung giờ' },
    ),
    note: z.string().optional(),
    notifyDoctor: z.boolean().default(true),
    notifyPatients: z.boolean().default(true),
    affectedBookingsAction: z
      .enum(['KEEP_EXISTING', 'RESCHEDULE_REQUIRED'])
      .default('KEEP_EXISTING'),
    confirmUnderstood: z.boolean().optional(),
  })
  .refine(
    (data) => {
      if (data.reasonCode === 'OTHER') {
        return Boolean(data.note && data.note.trim().length >= 5);
      }
      return true;
    },
    {
      message: 'Vui lòng nhập chi tiết lý do đóng (tối thiểu 5 ký tự)',
      path: ['note'],
    },
  );

export type SlotCloseFormValues = z.infer<typeof slotCloseSchema>;
