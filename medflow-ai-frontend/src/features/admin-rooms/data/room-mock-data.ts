import type { RoomItem, RoomStatus } from '../types/room.types';

const rows: [string, string, string, string, RoomStatus][] = [
  ['RM-101', 'Buồng khám Nội tổng quát 1', 'Khoa Nội tổng quát', 'Tòa A • Tầng 1', 'ACTIVE'],
  ['RM-102', 'Buồng khám Nội tổng quát 2', 'Khoa Nội tổng quát', 'Tòa A • Tầng 1', 'ACTIVE'],
  ['RM-103', 'Buồng khám Cấp cứu ban đầu', 'Khoa Cấp cứu', 'Tòa A • Tầng 1', 'ACTIVE'],
  ['RM-104', 'Buồng khám Nhi 1', 'Khoa Nhi', 'Tòa A • Tầng 1', 'ACTIVE'],
  ['RM-105', 'Buồng khám Nhi 2', 'Khoa Nhi', 'Tòa A • Tầng 1', 'SUSPENDED'],
  ['RM-201', 'Buồng khám Tim mạch 1', 'Khoa Tim mạch', 'Tòa A • Tầng 2', 'ACTIVE'],
  ['RM-202', 'Buồng khám Tim mạch 2', 'Khoa Tim mạch', 'Tòa A • Tầng 2', 'ACTIVE'],
  ['RM-203', 'Buồng siêu âm tim', 'Khoa Tim mạch', 'Tòa A • Tầng 2', 'MAINTENANCE'],
  ['RM-204', 'Buồng khám Thần kinh', 'Khoa Thần kinh', 'Tòa A • Tầng 2', 'ACTIVE'],
  ['RM-205', 'Buồng khám Cơ xương khớp', 'Khoa Cơ xương khớp', 'Tòa A • Tầng 2', 'ACTIVE'],
  ['RM-301', 'Buồng khám Sản phụ khoa 1', 'Khoa Sản phụ khoa', 'Tòa B • Tầng 3', 'ACTIVE'],
  ['RM-302', 'Buồng khám Sản phụ khoa 2', 'Khoa Sản phụ khoa', 'Tòa B • Tầng 3', 'ACTIVE'],
  ['RM-303', 'Buồng siêu âm thai', 'Khoa Sản phụ khoa', 'Tòa B • Tầng 3', 'ACTIVE'],
  ['RM-304', 'Buồng khám Ngoại tổng quát', 'Khoa Ngoại', 'Tòa B • Tầng 3', 'ACTIVE'],
  ['RM-305', 'Buồng tiểu phẫu', 'Khoa Ngoại', 'Tòa B • Tầng 3', 'MAINTENANCE'],
  ['RM-401', 'Buồng khám Tai Mũi Họng', 'Khoa Tai Mũi Họng', 'Tòa B • Tầng 4', 'ACTIVE'],
  ['RM-402', 'Buồng khám Răng Hàm Mặt', 'Khoa Răng Hàm Mặt', 'Tòa B • Tầng 4', 'ACTIVE'],
  ['RM-403', 'Buồng khám Mắt', 'Khoa Mắt', 'Tòa B • Tầng 4', 'ACTIVE'],
  ['RM-404', 'Buồng khám Da liễu', 'Khoa Da liễu', 'Tòa B • Tầng 4', 'SUSPENDED'],
  ['RM-405', 'Buồng đo thị lực', 'Khoa Mắt', 'Tòa B • Tầng 4', 'ACTIVE'],
  ['RM-501', 'Buồng chụp X-quang', 'Khoa Chẩn đoán hình ảnh', 'Tòa C • Tầng 5', 'ACTIVE'],
  ['RM-502', 'Buồng siêu âm tổng quát', 'Khoa Chẩn đoán hình ảnh', 'Tòa C • Tầng 5', 'ACTIVE'],
  ['RM-503', 'Buồng lấy mẫu xét nghiệm 1', 'Khoa Xét nghiệm', 'Tòa C • Tầng 5', 'ACTIVE'],
  ['RM-504', 'Buồng lấy mẫu xét nghiệm 2', 'Khoa Xét nghiệm', 'Tòa C • Tầng 5', 'ACTIVE'],
];

export const INITIAL_ROOMS: RoomItem[] = rows.map(([code, name, department, location, status]) => ({
  id: code.toLowerCase(),
  code,
  name,
  department,
  location,
  status,
}));
