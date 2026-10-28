import { z } from 'zod';

export const doctorSchema = z.object({
  id: z.string().regex(/^DOC-\d{4}$/, 'Mã bác sĩ có dạng DOC-0000'),
  staffId: z.string().min(1, 'Vui lòng chọn nhân sự'),
  name: z.string().min(2, 'Vui lòng nhập họ tên'),
  email: z.email('Email không hợp lệ'),
  degree: z.string().min(1, 'Vui lòng chọn học hàm / học vị'),
  specialty: z.string().min(2, 'Vui lòng nhập chuyên khoa'),
  department: z.string().min(1, 'Vui lòng chọn khoa công tác'),
  license: z.string().min(5, 'Vui lòng nhập số chứng chỉ hành nghề'),
  room: z.string().min(1, 'Vui lòng chọn buồng khám'),
  phone: z.string(),
  experience: z.number().min(0).max(70),
  scope: z.string(),
  biography: z.string().max(1000, 'Tối đa 1000 ký tự'),
  active: z.boolean(),
});

export type DoctorFormValues = z.infer<typeof doctorSchema>;
