import { z } from 'zod';

import type { CreateRoomInput } from '../types/room-create.types';

export const ROOM_DEPARTMENTS = [
  'Khoa Nội Tiêu hóa',
  'Khoa Nội tiết',
  'Khoa Tim mạch',
  'Khoa Nhi',
  'Khoa Tai Mũi Họng',
  'Khoa Da liễu',
  'Khoa Chẩn đoán hình ảnh',
] as const;

export const ROOM_CAPACITY_OPTIONS = [1, 2, 3, 4] as const;

export const roomCreateSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Vui lòng nhập mã phòng')
    .regex(/^RM-\d{3,4}$/, 'Mã phòng phải có dạng RM-xxx (3–4 chữ số)'),
  name: z.string().trim().min(1, 'Vui lòng nhập tên phòng').max(120, 'Tên phòng tối đa 120 ký tự'),
  type: z.enum(['OUTPATIENT', 'PROCEDURAL']),
  department: z.string().min(1, 'Vui lòng chọn khoa phụ trách'),
  location: z.string().trim().min(1, 'Vui lòng nhập vị trí phòng'),
  capacity: z
    .number({ error: 'Vui lòng chọn sức chứa' })
    .int()
    .min(1, 'Sức chứa tối thiểu 1')
    .max(4, 'Sức chứa tối đa 4'),
  status: z.enum(['ACTIVE', 'MAINTENANCE', 'INACTIVE']),
});

export type RoomCreateFormValues = z.infer<typeof roomCreateSchema>;

export const DEFAULT_ROOM_FORM_VALUES: CreateRoomInput = {
  code: '',
  name: '',
  type: 'OUTPATIENT',
  department: '',
  location: '',
  capacity: 1,
  status: 'ACTIVE',
};
