import type { DepartmentItem } from '../types/department.types';

export const DEFAULT_FACILITY = 'MedFlow Cơ sở 1';

const NAMES: [string, boolean][] = [
  ['Khoa Nội Tiêu hóa', true],
  ['Khoa Khám bệnh', true],
  ['Khoa Nội tiết', true],
  ['Khoa Chẩn đoán hình ảnh (CĐHA)', true],
  ['Khoa Xét nghiệm', true],
  ['Khoa Tim mạch', true],
  ['Khoa Nhi', true],
  ['Khoa Sản', true],
  ['Khoa Ngoại tổng hợp', true],
  ['Khoa Tai Mũi Họng', true],
  ['Khoa Răng Hàm Mặt', true],
  ['Khoa Mắt', true],
  ['Khoa Da liễu', false],
  ['Khoa Y học cổ truyền', false],
];

export const DEPARTMENT_MOCK_DATA: DepartmentItem[] = NAMES.map(([name, active], index) => {
  const n = String(index + 1).padStart(3, '0');
  return {
    id: `dep-${n}`,
    code: `DEP-${n}`,
    name,
    facility: DEFAULT_FACILITY,
    status: active ? 'ACTIVE' : 'INACTIVE',
  };
});
