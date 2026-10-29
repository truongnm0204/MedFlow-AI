import type { AppointmentReminder, PatientNotification } from '../types/appointment-reminder.types';

export const APPOINTMENT_REMINDERS_MOCK: AppointmentReminder[] = [
  {
    id: 'rem-001',
    appointmentCode: 'APT-2024-0891',
    patientName: 'Nguyễn Văn An',
    doctorName: 'BS. CKII Trần Khánh',
    doctorAvatar: 'https://images.unsplash.com/photo-1622253692010-333f2da6031d?w=150&auto=format&fit=crop&q=80',
    specialtyName: 'Nội tổng quát',
    roomName: 'P.102 • Tầng 1 - Khu Khám Đa Khoa',
    scheduledDate: '2026-10-30',
    scheduledTime: '09:15',
    hoursRemaining: 2,
    urgency: 'URGENT',
    preparationNotes: [
      'Nhịn ăn sáng ít nhất 6 tiếng nếu có chỉ định xét nghiệm đường huyết hoặc mỡ máu.',
      'Uống đủ nước lọc, không sử dụng đồ uống có cồn, trà hoặc cà phê trước khi khám.',
      'Có mặt tại Quầy tiếp nhận Tầng 1 trước giờ hẹn 15 phút để hoàn tất thủ tục check-in.',
    ],
    documentsRequired: [
      'Căn cước công dân gắn chip hoặc VNeID mức 2.',
      'Thẻ BHYT (bản cứng hoặc hình ảnh trên ứng dụng VssID).',
      'Đơn thuốc cũ và kết quả xét nghiệm gần nhất trong vòng 3 tháng (nếu có).',
    ],
    isConfirmedByPatient: false,
    notifiedAt: 'Hôm nay, 07:15',
  },
  {
    id: 'rem-002',
    appointmentCode: 'APT-2024-0895',
    patientName: 'Nguyễn Văn An',
    doctorName: 'ThS. BS Đinh Long',
    doctorAvatar: 'https://images.unsplash.com/photo-1537368910025-700350fe46c7?w=150&auto=format&fit=crop&q=80',
    specialtyName: 'Tai mũi họng',
    roomName: 'P.204 • Tầng 2 - Khu Khám Chuyên Sâu',
    scheduledDate: '2026-11-02',
    scheduledTime: '14:30',
    hoursRemaining: 72,
    urgency: 'UPCOMING',
    preparationNotes: [
      'Không dùng thuốc xịt mũi có chứa corticoid trước khi khám 24 giờ.',
      'Đeo khẩu trang y tế khi đến bệnh viện.',
    ],
    documentsRequired: [
      'Căn cước công dân gắn chip hoặc VNeID mức 2.',
      'Thẻ BHYT (nếu có).',
    ],
    isConfirmedByPatient: true,
    notifiedAt: 'Hôm qua, 14:30',
  },
];

export const PATIENT_NOTIFICATIONS_MOCK: PatientNotification[] = [
  {
    id: 'notif-001',
    title: 'Nhắc lịch khám sắp diễn ra (còn 2 tiếng)',
    message: 'Bạn có lịch khám Nội tổng quát với BS. Trần Khánh vào lúc 09:15 hôm nay tại P.102.',
    category: 'APPOINTMENT_REMINDER',
    createdAt: '15 phút trước',
    isRead: false,
    appointmentReminder: APPOINTMENT_REMINDERS_MOCK[0],
  },
  {
    id: 'notif-002',
    title: 'Xác nhận đặt lịch khám thành công',
    message: 'Lịch khám Tai mũi họng ngày 02/11/2026 đã được xác nhận mã APT-2024-0895.',
    category: 'APPOINTMENT_REMINDER',
    createdAt: 'Hôm qua',
    isRead: true,
    appointmentReminder: APPOINTMENT_REMINDERS_MOCK[1],
  },
  {
    id: 'notif-003',
    title: 'Cập nhật tài khoản MedFlow',
    message: 'Hồ sơ sức khỏe của bạn đã được liên kết với số định danh CCCD thành công.',
    category: 'SYSTEM',
    createdAt: '2 ngày trước',
    isRead: true,
  },
];
