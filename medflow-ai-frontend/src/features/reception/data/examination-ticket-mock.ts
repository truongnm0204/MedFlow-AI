import type { ExaminationTicketData } from '../types/examination-ticket.types';

export const EXAMINATION_TICKET_MOCK: ExaminationTicketData = {
  hospitalName: 'BỆNH VIỆN ĐA KHOA QUỐC TẾ MEDFLOW',
  ticketNumber: '009',
  roomName: 'PHÒNG 206',
  departmentName: 'KHOA TIÊU HÓA',
  location: 'TẦNG 2, NHÀ A',
  doctorName: 'ThS.BS Nguyễn Văn Hùng',
  patientName: 'Trần Minh Đức',
  gender: 'MALE',
  age: 41,
  patientCode: 'BN-2024-0021',
  visitCode: 'VIS-2026-0415-0145',
  serviceName: 'Khám Chuyên khoa Tiêu Hóa',
  receivedAt: '08:45 • Hôm nay',
  expectedTimeRange: '09:00 - 09:15',
  barcodeValue: 'VIS-2026-0415-0145',
  footerNote: 'Quý khách vui lòng theo dõi màn hình hiển thị số thứ tự trước cửa phòng khám.',
};
