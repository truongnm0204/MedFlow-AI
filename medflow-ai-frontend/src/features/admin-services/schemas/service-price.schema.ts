import { z } from 'zod';

export const updateServicePriceSchema = z.object({
  price: z
    .number({ message: 'Vui lòng nhập mức giá hợp lệ' })
    .positive('Mức giá dịch vụ phải lớn hơn 0 đ')
    .max(1_000_000_000, 'Mức giá không được vượt quá 1 tỷ đồng'),
  decisionNumber: z
    .string()
    .trim()
    .min(3, 'Số Quyết định / Căn cứ phải từ 3 ký tự trở lên')
    .max(50, 'Số Quyết định tối đa 50 ký tự'),
  effectiveDate: z
    .string()
    .min(1, 'Vui lòng chọn ngày bắt đầu hiệu lực')
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Ngày hiệu lực phải theo định dạng YYYY-MM-DD'),
  appliedBy: z
    .string()
    .trim()
    .min(2, 'Vui lòng nhập người hoặc cấp phê duyệt')
    .max(100, 'Người/Cấp phê duyệt tối đa 100 ký tự'),
  note: z.string().max(500, 'Ghi chú tối đa 500 ký tự').optional(),
});

export type UpdateServicePriceFormValues = z.infer<typeof updateServicePriceSchema>;
