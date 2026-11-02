import type { TransferQueueVisit, TransferReasonOption } from '../types/transfer-room.types';

export const TRANSFER_REASON_OPTIONS: TransferReasonOption[] = [
  { code: 'TECHNICAL_FAILURE', label: 'Buồng khám gặp sự cố kỹ thuật' },
  { code: 'URGENT_CONSULTATION', label: 'Bác sĩ hội chẩn khẩn cấp' },
  { code: 'LOAD_BALANCING', label: 'Cân bằng tải buồng khám' },
  { code: 'EXTRA_EXPERTISE', label: 'Yêu cầu chuyên môn bổ sung' },
  { code: 'OTHER', label: 'Khác' },
];

export const TRANSFER_QUEUE_VISIT_MOCK: TransferQueueVisit = {
  patientName: 'Nguyễn Văn An',
  gender: 'Nam',
  age: 34,
  patientCode: 'BN-2024-0018',
  visitCode: 'VIS-2026-0415-0108',
  currentRoom: 'Phòng 204 - BS.CKI Trần Quốc Tuấn',
  department: 'Khoa Tiêu Hóa',
  compatibleRooms: [
    {
      id: 'room-206',
      label: 'Phòng 206 - ThS.BS Nguyễn Văn Hùng',
      location: 'P.206 - Tầng 2',
    },
    {
      id: 'room-208',
      label: 'Phòng 208 - BS.CKI Phạm Minh Trí',
      location: 'P.208 - Tầng 2',
    },
  ],
  history: [
    {
      id: 'tr-002',
      fromRoom: 'Phòng 202',
      toRoom: 'Phòng 204',
      reason: 'Cân bằng tải buồng khám',
      note: 'Phòng 202 đang quá tải, chuyển sang phòng 204.',
      performedBy: 'Lễ tân Lê Thị Hoa',
      performedAt: '15/04/2026 08:42',
    },
    {
      id: 'tr-001',
      fromRoom: 'Phòng 201',
      toRoom: 'Phòng 202',
      reason: 'Buồng khám gặp sự cố kỹ thuật',
      performedBy: 'Lễ tân Lê Thị Hoa',
      performedAt: '15/04/2026 08:15',
    },
  ],
};
