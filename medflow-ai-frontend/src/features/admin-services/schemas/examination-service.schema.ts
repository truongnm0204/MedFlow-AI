import { z } from 'zod';

export const examinationServiceSchema = z.object({
  code: z
    .string()
    .min(1, 'Vui lòng nhập mã dịch vụ')
    .regex(/^EX-\d+-\d+$/, 'Mã dịch vụ theo chuẩn EX-x-xx (VD: EX-2-09)'),
  name: z
    .string()
    .min(3, 'Tên dịch vụ phải có ít nhất 3 ký tự')
    .max(200, 'Tên dịch vụ tối đa 200 ký tự'),
  category: z.enum(['Khám Lâm sàng', 'Hội chẩn Lâm sàng', 'Cận lâm sàng kỹ thuật'], {
    message: 'Vui lòng chọn phân loại dịch vụ hợp lệ',
  }),
  department: z.string().min(1, 'Vui lòng chọn khoa chuyên môn quản lý'),
  description: z.string().max(500, 'Mô tả tối đa 500 ký tự'),
  onlineBooking: z.boolean(),
  status: z.enum(['ACTIVE', 'SUSPENDED']),
});

export type ExaminationServiceFormValues = z.infer<typeof examinationServiceSchema>;
