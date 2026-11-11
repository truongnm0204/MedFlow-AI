import type {
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticServicePointOptionDto,
  DiagnosticStaffProfile,
} from '../types/diagnostic-order-review.types';
import type {
  DiagnosticPatientReceiveDetailResponseDto,
  DiagnosticPatientReceiveItemDto,
  DiagnosticPatientReceiveListResponseDto,
  DiagnosticPatientReceiveSummaryStatsDto,
  GetDiagnosticOrdersForReceiveParams,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientResponseDto,
  ReceiveDiagnosticPatientsBatchRequest,
} from '../types/diagnostic-patient-receive.types';
import {
  DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
  DIAGNOSTIC_SERVICE_POINTS_MOCK,
} from './diagnostic-order-review-mock';

export const DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK: DiagnosticStaffProfile = {
  ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
};

export const DIAGNOSTIC_RECEIVE_SERVICE_POINTS_MOCK: DiagnosticServicePointOptionDto[] =
  DIAGNOSTIC_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp }));

const INITIAL_DIAGNOSTIC_PATIENT_RECEIVE_ITEMS: DiagnosticPatientReceiveItemDto[] = [
  {
    orderId: 44,
    orderCode: 'DIA-202610-0044',
    visitId: 1004,
    visitCode: 'VIS-202610-1004',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2012,
      patientCode: 'PAT-2012',
      fullName: 'Phạm Thu Dung',
      dateOfBirth: '1972-04-18',
      age: 54,
      gender: 'FEMALE',
      phone: '0908123456',
      bloodType: 'O+',
      allergies: 'Dị ứng thuốc cản quang chứa I-ốt mức độ nhẹ',
    },
    orderingDoctorId: 108,
    orderingDoctorCode: 'DOC-108',
    orderingDoctorName: 'BS.CKI. Nguyễn Minh Khôi',
    serviceId: 501,
    serviceCode: 'XN-SH-001',
    serviceName: 'Sinh hóa máu: Glucose, HbA1c, Creatinin',
    category: 'LABORATORY',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 4,
    specialtyCode: 'NOI-TIET',
    specialtyName: 'Nội tiết - Đái tháo đường',
    unitPrice: 285000,
    requiresResultVerification: true,
    priority: 'STAT',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction:
      'Bệnh nhân đã nhịn ăn sáng trên 8 giờ. Ưu tiên lấy mẫu sớm để đánh giá đường huyết cấp.',
    orderedAt: '2026-10-20T08:30:00Z',
    confirmedAt: '2026-10-20T08:34:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã đối chiếu thông tin hành chính và chỉ định lâm sàng hợp lệ.',
    receivedAt: null,
    receivedByStaffId: null,
    receivedByStaffCode: null,
    receivedByStaffName: null,
    receptionNote: null,
    queueEntryId: 9044,
    formattedQueueEntryId: 'QE-00009044',
    ticketNo: 'C021',
    queueNumber: 21,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: '2026-10-20T08:35:00Z',
    readyAt: null,
    stateVersion: 2,
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canReceive: true,
      disabledReason: null,
      isOrderConfirmed: true,
      isQueueWaitingForReception: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 45,
    orderCode: 'DIA-202610-0045',
    visitId: 1005,
    visitCode: 'VIS-202610-1005',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2015,
      patientCode: 'PAT-2015',
      fullName: 'Trần Văn Hiếu',
      dateOfBirth: '1959-09-12',
      age: 67,
      gender: 'MALE',
      phone: '0912345678',
      bloodType: 'A+',
      allergies: null,
    },
    orderingDoctorId: 109,
    orderingDoctorCode: 'DOC-109',
    orderingDoctorName: 'PGS.TS.BS. Hoàng Trọng Nghĩa',
    serviceId: 502,
    serviceCode: 'TDCN-ECG-002',
    serviceName: 'Điện tâm đồ 12 chuyển đạo & Siêu âm tim Doppler',
    category: 'FUNCTIONAL',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 2,
    specialtyCode: 'TIM-MACH',
    specialtyName: 'Tim mạch can thiệp',
    unitPrice: 420000,
    requiresResultVerification: true,
    priority: 'URGENT',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Theo dõi nhịp tim nghỉ ngơi 5 phút trước khi đo ECG.',
    orderedAt: '2026-10-20T08:40:00Z',
    confirmedAt: '2026-10-20T08:43:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Ưu tiên thực hiện sớm theo yêu cầu lâm sàng của bác sĩ chỉ định.',
    receivedAt: null,
    receivedByStaffId: null,
    receivedByStaffCode: null,
    receivedByStaffName: null,
    receptionNote: null,
    queueEntryId: 9045,
    formattedQueueEntryId: 'QE-00009045',
    ticketNo: 'C022',
    queueNumber: 22,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    manualPriority: 'ELDERLY',
    effectiveClinicalPriority: 'ELDERLY',
    arrivedAt: '2026-10-20T08:44:00Z',
    readyAt: null,
    stateVersion: 2,
    servicePointId: 205,
    servicePointName: 'Buồng Điện tâm đồ & Thăm dò chức năng 05',
    roomId: 25,
    roomCode: 'P.TDCN-205',
    roomName: 'Phòng Thăm dò chức năng 205',
    roomLocation: 'Tầng 2 - Khu A',
    isAdditionalOrder: true,
    orderSequenceNo: 2,
    followUpToOrderId: 38,
    followUpToOrderCode: 'DIA-202610-0038',
    additionalOrderReason:
      'Điện tâm đồ sàng lọc có ngoại tâm thu thất rải rác, cần đánh giá chức năng tâm thu thất trái.',
    canReceive: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canReceive: true,
      disabledReason: null,
      isOrderConfirmed: true,
      isQueueWaitingForReception: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 46,
    orderCode: 'DIA-202610-0046',
    visitId: 1007,
    visitCode: 'VIS-202610-1007',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2018,
      patientCode: 'PAT-2018',
      fullName: 'Nguyễn Thị Mai Anh',
      dateOfBirth: '1997-02-14',
      age: 29,
      gender: 'FEMALE',
      phone: '0987654321',
      bloodType: 'B+',
      allergies: null,
    },
    orderingDoctorId: 112,
    orderingDoctorCode: 'DOC-112',
    orderingDoctorName: 'BS.CKII. Đặng Quốc Việt',
    serviceId: 503,
    serviceCode: 'CDHA-SA-003',
    serviceName: 'Siêu âm ổ bụng tổng quát & hệ gan mật Doppler màu',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 5,
    specialtyCode: 'TIEU-HOA',
    specialtyName: 'Tiêu hóa - Gan mật',
    unitPrice: 310000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Bệnh nhân nhịn ăn và uống đủ nước trước siêu âm.',
    orderedAt: '2026-10-20T08:45:00Z',
    confirmedAt: '2026-10-20T08:48:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã hướng dẫn bệnh nhân nhịn ăn/chuẩn bị trước kỹ thuật và xếp hàng đợi.',
    receivedAt: null,
    receivedByStaffId: null,
    receivedByStaffCode: null,
    receivedByStaffName: null,
    receptionNote: null,
    queueEntryId: 9046,
    formattedQueueEntryId: 'QE-00009046',
    ticketNo: 'C023',
    queueNumber: 23,
    queueDate: '2026-10-20',
    queueStatus: 'SKIPPED',
    presenceStatus: 'PRESENT',
    manualPriority: 'PREGNANT',
    effectiveClinicalPriority: 'PREGNANT',
    arrivedAt: '2026-10-20T08:50:00Z',
    readyAt: null,
    stateVersion: 3,
    servicePointId: 202,
    servicePointName: 'Bàn Siêu âm Doppler màu 02',
    roomId: 22,
    roomCode: 'P.SA-202',
    roomName: 'Phòng Siêu âm 202',
    roomLocation: 'Tầng 2 - Khu C',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canReceive: true,
      disabledReason: null,
      isOrderConfirmed: true,
      isQueueWaitingForReception: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 42,
    orderCode: 'DIA-202610-0042',
    visitId: 1002,
    visitCode: 'VIS-202610-1002',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2005,
      patientCode: 'PAT-2005',
      fullName: 'Võ Minh Quân',
      dateOfBirth: '1984-07-09',
      age: 42,
      gender: 'MALE',
      phone: '0933445566',
      bloodType: 'AB+',
      allergies: null,
    },
    orderingDoctorId: 105,
    orderingDoctorCode: 'DOC-105',
    orderingDoctorName: 'BS.CKI. Phan Thanh Tùng',
    serviceId: 504,
    serviceCode: 'CDHA-XQ-004',
    serviceName: 'Chụp X-quang ngực thẳng kỹ thuật số (DR)',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 3,
    specialtyCode: 'HO-HAP',
    specialtyName: 'Nội Hô hấp',
    unitPrice: 180000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Tháo dây chuyền và vật dụng kim loại vùng ngực trước khi vào buồng chụp.',
    orderedAt: '2026-10-20T08:15:00Z',
    confirmedAt: '2026-10-20T08:18:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã xác nhận chỉ định X-quang ngực thẳng.',
    receivedAt: '2026-10-20T08:22:00Z',
    receivedByStaffId: 304,
    receivedByStaffCode: 'DIAG-009',
    receivedByStaffName: 'CN. Lê Hoàng Minh',
    receptionNote: 'Đã tiếp nhận bệnh nhân tại buồng X-quang, kiểm tra tháo trang sức kim loại.',
    queueEntryId: 9042,
    formattedQueueEntryId: 'QE-00009042',
    ticketNo: 'C019',
    queueNumber: 19,
    queueDate: '2026-10-20',
    queueStatus: 'READY',
    presenceStatus: 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: '2026-10-20T08:20:00Z',
    readyAt: '2026-10-20T08:22:00Z',
    stateVersion: 3,
    servicePointId: 204,
    servicePointName: 'Buồng chụp X-quang kỹ thuật số 04',
    roomId: 24,
    roomCode: 'P.XQ-204',
    roomName: 'Phòng X-quang 204',
    roomLocation: 'Tầng 2 - Khu C',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: false,
    disabledReason:
      "Bệnh nhân của phiếu chỉ định 'DIA-202610-0042' đã được tiếp nhận và đang sẵn sàng trong hàng đợi phòng kỹ thuật (READY).",
    eligibility: {
      isEligible: false,
      canReceive: false,
      disabledReason:
        "Bệnh nhân của phiếu chỉ định 'DIA-202610-0042' đã được tiếp nhận và đang sẵn sàng trong hàng đợi phòng kỹ thuật (READY).",
      isOrderConfirmed: true,
      isQueueWaitingForReception: false,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 47,
    orderCode: 'DIA-202610-0047',
    visitId: 1008,
    visitCode: 'VIS-202610-1008',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2021,
      patientCode: 'PAT-2021',
      fullName: 'Đỗ Hoàng Nam',
      dateOfBirth: '1978-11-23',
      age: 48,
      gender: 'MALE',
      phone: '0944556677',
      bloodType: 'O+',
      allergies: null,
    },
    orderingDoctorId: 115,
    orderingDoctorCode: 'DOC-115',
    orderingDoctorName: 'BS.CKII. Trịnh Quang Huy',
    serviceId: 504,
    serviceCode: 'CDHA-XQ-005',
    serviceName: 'Chụp X-quang cột sống thắt lưng thẳng - nghiêng',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 6,
    specialtyCode: 'CO-XUONG-KHOP',
    specialtyName: 'Cơ xương khớp - Ngoại thần kinh',
    unitPrice: 240000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'ORDERED',
    requiredForConclusion: true,
    instruction: 'Chụp 2 tư thế thẳng và nghiêng vùng cột sống thắt lưng L1-S1.',
    orderedAt: '2026-10-20T08:52:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    confirmationNote: null,
    receivedAt: null,
    receivedByStaffId: null,
    receivedByStaffCode: null,
    receivedByStaffName: null,
    receptionNote: null,
    queueEntryId: 9047,
    formattedQueueEntryId: 'QE-00009047',
    ticketNo: 'C024',
    queueNumber: 24,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: '2026-10-20T08:53:00Z',
    readyAt: null,
    stateVersion: 1,
    servicePointId: 204,
    servicePointName: 'Buồng chụp X-quang kỹ thuật số 04',
    roomId: 24,
    roomCode: 'P.XQ-204',
    roomName: 'Phòng X-quang 204',
    roomLocation: 'Tầng 2 - Khu C',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: false,
    disabledReason:
      "Phiếu chỉ định cận lâm sàng 'DIA-202610-0047' chưa được xác nhận điều kiện xử lý (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi tiếp nhận bệnh nhân.",
    eligibility: {
      isEligible: false,
      canReceive: false,
      disabledReason:
        "Phiếu chỉ định cận lâm sàng 'DIA-202610-0047' chưa được xác nhận điều kiện xử lý (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi tiếp nhận bệnh nhân.",
      isOrderConfirmed: false,
      isQueueWaitingForReception: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 41,
    orderCode: 'DIA-202610-0041',
    visitId: 1001,
    visitCode: 'VIS-202610-1001',
    visitStatus: 'IN_CONSULTATION',
    patient: {
      patientId: 2001,
      patientCode: 'PAT-2001',
      fullName: 'Lê Thị Bích Ngọc',
      dateOfBirth: '1991-05-20',
      age: 35,
      gender: 'FEMALE',
      phone: '0977889900',
      bloodType: 'O+',
      allergies: null,
    },
    orderingDoctorId: 108,
    orderingDoctorCode: 'DOC-108',
    orderingDoctorName: 'BS.CKI. Nguyễn Minh Khôi',
    serviceId: 501,
    serviceCode: 'XN-HH-001',
    serviceName: 'Tổng phân tích tế bào máu ngoại vi (24 thông số)',
    category: 'LABORATORY',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 1,
    specialtyCode: 'NOI-TONG-QUAT',
    specialtyName: 'Nội tổng quát',
    unitPrice: 150000,
    requiresResultVerification: true,
    priority: 'URGENT',
    status: 'COMPLETED',
    requiredForConclusion: true,
    instruction: 'Kiểm tra số lượng tiểu cầu và Hematocrit.',
    orderedAt: '2026-10-20T07:50:00Z',
    confirmedAt: '2026-10-20T07:53:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã xác nhận chỉ định huyết học.',
    receivedAt: '2026-10-20T07:56:00Z',
    receivedByStaffId: 304,
    receivedByStaffCode: 'DIAG-009',
    receivedByStaffName: 'CN. Lê Hoàng Minh',
    receptionNote: 'Đã tiếp nhận bệnh nhân và lấy mẫu máu tĩnh mạch.',
    queueEntryId: 9041,
    formattedQueueEntryId: 'QE-00009041',
    ticketNo: 'C018',
    queueNumber: 18,
    queueDate: '2026-10-20',
    queueStatus: 'COMPLETED',
    presenceStatus: 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: '2026-10-20T07:54:00Z',
    readyAt: '2026-10-20T07:56:00Z',
    stateVersion: 5,
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: false,
    disabledReason:
      "Phiếu chỉ định cận lâm sàng 'DIA-202610-0041' đã hoàn tất kết quả (COMPLETED), không thể tiếp nhận lại.",
    eligibility: {
      isEligible: false,
      canReceive: false,
      disabledReason:
        "Phiếu chỉ định cận lâm sàng 'DIA-202610-0041' đã hoàn tất kết quả (COMPLETED), không thể tiếp nhận lại.",
      isOrderConfirmed: false,
      isQueueWaitingForReception: false,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: true,
      hasQueueManagePermission: true,
    },
  },
  {
    orderId: 48,
    orderCode: 'DIA-202610-0048',
    visitId: 1008,
    visitCode: 'VIS-202610-1008',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2021,
      patientCode: 'PAT-2021',
      fullName: 'Đỗ Hoàng Nam',
      dateOfBirth: '1978-11-23',
      age: 48,
      gender: 'MALE',
      phone: '0944556677',
      bloodType: 'O+',
      allergies: null,
    },
    orderingDoctorId: 115,
    orderingDoctorCode: 'DOC-115',
    orderingDoctorName: 'BS.CKII. Trịnh Quang Huy',
    serviceId: 507,
    serviceCode: 'CDHA-MRI-007',
    serviceName: 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 6,
    specialtyCode: 'CO-XUONG-KHOP',
    specialtyName: 'Cơ xương khớp - Ngoại thần kinh',
    unitPrice: 1850000,
    requiresResultVerification: true,
    priority: 'URGENT',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Kiểm tra dị vật kim loại trong cơ thể trước khi vào phòng từ trường.',
    orderedAt: '2026-10-20T08:55:00Z',
    confirmedAt: '2026-10-20T08:58:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã xác nhận chỉ định MRI cột sống thắt lưng.',
    receivedAt: null,
    receivedByStaffId: null,
    receivedByStaffCode: null,
    receivedByStaffName: null,
    receptionNote: null,
    queueEntryId: 9048,
    formattedQueueEntryId: 'QE-00009048',
    ticketNo: 'C025',
    queueNumber: 25,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: '2026-10-20T08:59:00Z',
    readyAt: null,
    stateVersion: 2,
    servicePointId: 207,
    servicePointName: 'Buồng chụp Cộng hưởng từ MRI 07',
    roomId: 27,
    roomCode: 'P.MRI-207',
    roomName: 'Phòng MRI 207',
    roomLocation: 'Tầng 1 - Khu Kỹ thuật cao',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canReceive: false,
    disabledReason:
      "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
    eligibility: {
      isEligible: false,
      canReceive: false,
      disabledReason:
        "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
      isOrderConfirmed: true,
      isQueueWaitingForReception: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isServicePointActive: true,
      isRoomActive: false,
      hasQueueManagePermission: true,
    },
  },
];

const CLINICAL_CONTEXT_BY_VISIT_ID: Record<number, DiagnosticOrderClinicalSummaryDto> = {
  1004: {
    visitId: 1004,
    visitCode: 'VIS-202610-1004',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Khát nước nhiều, tiểu nhiều về đêm, mệt mỏi sụt 3kg trong 1 tháng',
    presentingComplaint:
      'Bệnh nhân tiền sử đái tháo đường type 2 5 năm, tuân thủ thuốc không đều, đường huyết mao mạch sáng nay 14.8 mmol/L.',
    clinicalFindings:
      'Tỉnh táo, niêm mạc hơi khô, mạch 88 lần/phút, huyết áp 135/85 mmHg, BMI 24.6.',
    primaryDiagnosisCode: 'E11.65',
    primaryDiagnosisName:
      'Đái tháo đường không phụ thuộc insuline có tăng đường huyết chưa kiểm soát',
    clinicalDiagnosis: 'Theo dõi đợt mất bù đường huyết cấp trên nền ĐTĐ type 2',
  },
  1005: {
    visitId: 1005,
    visitCode: 'VIS-202610-1005',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Hồi hộp đánh trống ngực kèm khó thở khi gắng sức nhẹ',
    presentingComplaint: 'Cơn hồi hộp kéo dài 10-15 phút xuất hiện 3 ngày nay, nghỉ ngơi đỡ chậm.',
    clinicalFindings:
      'Tim nhịp không đều từng lúc, tần số trung bình 92 chu kỳ/phút, huyết áp 145/90 mmHg.',
    primaryDiagnosisCode: 'I49.3',
    primaryDiagnosisName: 'Ngoại tâm thu thất',
    clinicalDiagnosis: 'Ngoại tâm thu thất trên bệnh nhân tăng huyết áp độ 2',
  },
  1007: {
    visitId: 1007,
    visitCode: 'VIS-202610-1007',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Đau âm ỉ hạ sườn phải sau bữa ăn nhiều dầu mỡ',
    presentingComplaint:
      'Đau tức vùng hạ sườn phải lan lên vai phải, đầy bụng khó tiêu 1 tuần nay.',
    clinicalFindings:
      'Bụng mềm, ấn tức nhẹ điểm túi mật, nghiệm pháp Murphy (+/-), gan lách không sờ chạm.',
    primaryDiagnosisCode: 'K80.2',
    primaryDiagnosisName: 'Sỏi túi mật không có viêm túi mật',
    clinicalDiagnosis: 'Theo dõi sỏi túi mật / bùn túi mật - chỉ định siêu âm ổ bụng Doppler màu',
  },
  1008: {
    visitId: 1008,
    visitCode: 'VIS-202610-1008',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Đau cột sống thắt lưng lan xuống mặt sau đùi và cẳng chân phải',
    presentingComplaint:
      'Đau tăng khi cúi người hoặc ngồi lâu, tê bì vùng mu bàn chân phải 3 tuần nay.',
    clinicalFindings:
      'Cơ cạnh sống thắt lưng co cứng nhẹ, dấu hiệu Lasègue chân phải dương tính ở 45 độ.',
    primaryDiagnosisCode: 'M51.1',
    primaryDiagnosisName: 'Rối loạn đĩa đệm vùng thắt lưng và vùng khác có bệnh lý rễ thần kinh',
    clinicalDiagnosis: 'Thoát vị đĩa đệm cột sống thắt lưng L4-L5/L5-S1 chèn ép rễ thần kinh',
  },
  1002: {
    visitId: 1002,
    visitCode: 'VIS-202610-1002',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Ho khan kéo dài 10 ngày kèm tức ngực nhẹ khi hít sâu',
    presentingComplaint: 'Ho nhiều về đêm và sáng sớm, sốt nhẹ 37.8 độ C về chiều.',
    clinicalFindings: 'Phổi rì rào phế nang giảm nhẹ đáy phổi phải, rale nổ rải rác.',
    primaryDiagnosisCode: 'J18.9',
    primaryDiagnosisName: 'Viêm phổi, tác nhân không xác định',
    clinicalDiagnosis: 'Theo dõi viêm phế quản phổi thùy dưới phổi phải',
  },
  1001: {
    visitId: 1001,
    visitCode: 'VIS-202610-1001',
    visitStatus: 'IN_CONSULTATION',
    reasonForVisit: 'Sốt cao 38.9 độ C, đau đầu, mỏi cơ toàn thân ngày thứ 2',
    presentingComplaint: 'Khởi phát sốt đột ngột, mệt mỏi nhiều, ăn uống kém.',
    clinicalFindings: 'Da xung huyết nhẹ, họng đỏ, nghiệm pháp dây thắt âm tính.',
    primaryDiagnosisCode: 'A97.0',
    primaryDiagnosisName: 'Sốt xuất huyết Dengue không có dấu hiệu cảnh báo',
    clinicalDiagnosis: 'Theo dõi sốt xuất huyết Dengue ngày 2 - kiểm tra công thức máu & tiểu cầu',
  },
};

function cloneItems(items: DiagnosticPatientReceiveItemDto[]): DiagnosticPatientReceiveItemDto[] {
  return items.map((item) => ({
    ...item,
    patient: { ...item.patient },
    eligibility: { ...item.eligibility },
  }));
}

let mockReceiveOrdersState: DiagnosticPatientReceiveItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_PATIENT_RECEIVE_ITEMS,
);

export const DIAGNOSTIC_PATIENT_RECEIVE_ITEMS_MOCK: DiagnosticPatientReceiveItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_PATIENT_RECEIVE_ITEMS,
);

export function resetDiagnosticPatientReceiveMockState(): void {
  mockReceiveOrdersState = cloneItems(INITIAL_DIAGNOSTIC_PATIENT_RECEIVE_ITEMS);
}

function computeSummaryStats(
  items: DiagnosticPatientReceiveItemDto[],
): DiagnosticPatientReceiveSummaryStatsDto {
  let pendingConfirmationCount = 0;
  let waitingReceptionCount = 0;
  let readyInQueueCount = 0;
  let inProgressCount = 0;
  let completedCount = 0;
  let cancelledCount = 0;
  let urgentOrStatWaitingCount = 0;

  for (const item of items) {
    const status = item.status.toUpperCase();
    const qStatus = (item.queueStatus ?? 'WAITING').toUpperCase();

    if (status === 'ORDERED') {
      pendingConfirmationCount += 1;
    } else if (status === 'CONFIRMED') {
      if (qStatus === 'READY' || qStatus === 'CALLED') {
        readyInQueueCount += 1;
      } else {
        waitingReceptionCount += 1;
        if (item.priority === 'URGENT' || item.priority === 'STAT') {
          urgentOrStatWaitingCount += 1;
        }
      }
    } else if (status === 'IN_PROGRESS') {
      inProgressCount += 1;
    } else if (status === 'COMPLETED') {
      completedCount += 1;
    } else if (status === 'CANCELLED') {
      cancelledCount += 1;
    }
  }

  return {
    totalOrders: items.length,
    pendingConfirmationCount,
    waitingReceptionCount,
    readyInQueueCount,
    inProgressCount,
    completedCount,
    cancelledCount,
    urgentOrStatWaitingCount,
  };
}

function findMockReceiveOrderByTarget(
  idOrCode: string | number,
): DiagnosticPatientReceiveItemDto | undefined {
  const raw = String(idOrCode).trim();
  if (!raw) return undefined;
  const upper = raw.toUpperCase();
  const numericId = Number(raw);

  return mockReceiveOrdersState.find((item) => {
    if (!Number.isNaN(numericId) && item.orderId === numericId) return true;
    if (item.orderCode.toUpperCase() === upper) return true;
    if (item.ticketNo && item.ticketNo.toUpperCase() === upper) return true;
    if (item.visitCode.toUpperCase() === upper) return true;

    const tailDigits = raw.match(/(\d+)$/)?.[1];
    if (tailDigits) {
      const parsedTail = Number(tailDigits);
      if (!Number.isNaN(parsedTail) && item.orderId === parsedTail) {
        return true;
      }
    }
    return false;
  });
}

export function resolveMockDiagnosticOrdersForReceive(
  params?: GetDiagnosticOrdersForReceiveParams,
): DiagnosticPatientReceiveListResponseDto {
  const allItems = cloneItems(mockReceiveOrdersState);
  const summary = computeSummaryStats(allItems);

  const statusFilter = params?.status?.trim().toUpperCase();
  const queueStatusFilter = params?.queueStatus?.trim().toUpperCase();
  const categoryFilter = params?.category?.trim().toUpperCase();
  const priorityFilter = params?.priority?.trim().toUpperCase();
  const keyword = params?.keyword?.trim().toLowerCase() ?? '';
  const readyToReceiveOnly = Boolean(params?.readyToReceiveOnly);

  let filtered = allItems.filter((item) => {
    if (readyToReceiveOnly && !item.canReceive) {
      return false;
    }
    if (statusFilter && statusFilter !== 'ALL' && item.status.toUpperCase() !== statusFilter) {
      return false;
    }
    if (
      queueStatusFilter &&
      queueStatusFilter !== 'ALL' &&
      (item.queueStatus ?? '').toUpperCase() !== queueStatusFilter
    ) {
      return false;
    }
    if (
      categoryFilter &&
      categoryFilter !== 'ALL' &&
      item.category.toUpperCase() !== categoryFilter
    ) {
      return false;
    }
    if (
      priorityFilter &&
      priorityFilter !== 'ALL' &&
      item.priority.toUpperCase() !== priorityFilter
    ) {
      return false;
    }
    if (params?.visitId && item.visitId !== params.visitId) {
      return false;
    }
    if (
      params?.visitCode &&
      item.visitCode.toUpperCase() !== params.visitCode.trim().toUpperCase()
    ) {
      return false;
    }
    if (params?.serviceId && item.serviceId !== params.serviceId) {
      return false;
    }
    if (params?.roomId && item.roomId !== params.roomId) {
      return false;
    }
    if (params?.servicePointId && item.servicePointId !== params.servicePointId) {
      return false;
    }
    if (keyword) {
      const haystack = [
        item.orderCode,
        item.ticketNo ?? '',
        item.visitCode,
        item.patient.fullName,
        item.patient.patientCode,
        item.patient.phone ?? '',
        item.serviceCode,
        item.serviceName,
        item.orderingDoctorName,
        item.roomCode ?? '',
      ]
        .join(' ')
        .toLowerCase();
      if (!haystack.includes(keyword)) {
        return false;
      }
    }
    return true;
  });

  if (params?.limit && params.limit > 0) {
    filtered = filtered.slice(0, params.limit);
  }

  return {
    hospitalId: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalName,
    departmentId: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.departmentId,
    departmentName: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.departmentName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK },
    summary,
    availableServicePoints: DIAGNOSTIC_RECEIVE_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
    items: filtered,
  };
}

export function resolveMockDiagnosticPatientReceiveDetail(
  idOrCode: string,
): DiagnosticPatientReceiveDetailResponseDto {
  const matched = findMockReceiveOrderByTarget(idOrCode) ?? mockReceiveOrdersState[0];
  const orderCopy: DiagnosticPatientReceiveItemDto = {
    ...matched,
    patient: { ...matched.patient },
    eligibility: { ...matched.eligibility },
  };

  const clinicalContext: DiagnosticOrderClinicalSummaryDto = CLINICAL_CONTEXT_BY_VISIT_ID[
    orderCopy.visitId
  ] ?? {
    visitId: orderCopy.visitId,
    visitCode: orderCopy.visitCode,
    visitStatus: orderCopy.visitStatus,
    reasonForVisit: 'Khám chuyên khoa theo chỉ định lâm sàng',
    presentingComplaint: 'Theo dõi triệu chứng lâm sàng cần cận lâm sàng hỗ trợ chẩn đoán.',
    clinicalFindings: 'Sinh hiệu ổn định, bệnh nhân tỉnh táo tiếp xúc tốt.',
    primaryDiagnosisCode: 'Z00.0',
    primaryDiagnosisName: 'Khám nội khoa tổng quát',
    clinicalDiagnosis: 'Cần thực hiện cận lâm sàng theo phiếu chỉ định của bác sĩ.',
  };

  return {
    hospitalId: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.hospitalName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK },
    order: orderCopy,
    clinicalContext: { ...clinicalContext },
    availableServicePoints: DIAGNOSTIC_RECEIVE_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
  };
}

function applyMockReceptionToOrder(
  target: DiagnosticPatientReceiveItemDto,
  payload?: ReceiveDiagnosticPatientRequest,
): DiagnosticPatientReceiveItemDto {
  const nowIso = new Date().toISOString();
  const chosenServicePoint = payload?.servicePointId
    ? DIAGNOSTIC_RECEIVE_SERVICE_POINTS_MOCK.find(
        (sp) => sp.servicePointId === payload.servicePointId,
      )
    : undefined;

  const updatedPriority = payload?.priority?.trim()
    ? payload.priority.trim().toUpperCase() === 'STANDARD' ||
      payload.priority.trim().toUpperCase() === 'ROUTINE'
      ? 'NORMAL'
      : payload.priority.trim().toUpperCase()
    : target.priority;

  const rawManualPriority = (payload?.manualPriority ?? payload?.clinicalPriority)?.trim();
  const updatedManualPriority = rawManualPriority
    ? rawManualPriority.toUpperCase()
    : (target.manualPriority ?? 'NORMAL');

  const updatedNote =
    payload?.receptionNote?.trim() ||
    payload?.receiveNote?.trim() ||
    payload?.notes?.trim() ||
    'Bệnh nhân đã có mặt tại điểm dịch vụ, đối chiếu đúng họ tên & mã BN, sẵn sàng vào hàng đợi.';

  const updatedInstruction =
    payload?.instruction !== undefined && payload?.instruction !== null
      ? payload.instruction.trim() || target.instruction
      : target.instruction;

  target.priority = updatedPriority;
  target.manualPriority = updatedManualPriority;
  target.effectiveClinicalPriority = updatedManualPriority;
  target.instruction = updatedInstruction;
  target.receivedAt = nowIso;
  target.receivedByStaffId = DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.userId;
  target.receivedByStaffCode = DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009';
  target.receivedByStaffName = DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK.fullName;
  target.receptionNote = updatedNote;
  target.presenceStatus = 'PRESENT';
  target.queueStatus = 'READY';
  target.arrivedAt = target.arrivedAt ?? nowIso;
  target.readyAt = nowIso;
  target.stateVersion = (target.stateVersion || 1) + 1;

  if (chosenServicePoint) {
    target.servicePointId = chosenServicePoint.servicePointId;
    target.servicePointName = chosenServicePoint.name;
    target.roomId = chosenServicePoint.roomId ?? target.roomId;
    target.roomCode = chosenServicePoint.roomCode ?? target.roomCode;
    target.roomName = chosenServicePoint.roomName ?? target.roomName;
    target.roomLocation = chosenServicePoint.roomLocation ?? target.roomLocation;
  }

  if (!target.ticketNo) {
    target.ticketNo = `C${String(target.orderId).padStart(3, '0')}`;
  }

  target.canReceive = false;
  target.disabledReason = `Bệnh nhân của phiếu chỉ định '${target.orderCode}' đã được tiếp nhận và đang sẵn sàng trong hàng đợi phòng kỹ thuật (READY).`;
  target.eligibility = {
    ...target.eligibility,
    isEligible: false,
    canReceive: false,
    isQueueWaitingForReception: false,
    disabledReason: target.disabledReason,
  };

  return {
    ...target,
    patient: { ...target.patient },
    eligibility: { ...target.eligibility },
  };
}

export function receiveMockDiagnosticPatient(
  idOrCode: string,
  payload?: ReceiveDiagnosticPatientRequest,
): ReceiveDiagnosticPatientResponseDto {
  const matched = findMockReceiveOrderByTarget(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${idOrCode}'.`);
  }

  if (!matched.canReceive || matched.status !== 'CONFIRMED') {
    throw new Error(
      matched.disabledReason ||
        `Phiếu chỉ định '${matched.orderCode}' không đủ điều kiện tiếp nhận bệnh nhân.`,
    );
  }

  const receivedOrder = applyMockReceptionToOrder(matched, payload);

  return {
    actionOutcome: 'PATIENT_RECEIVED',
    receivedCount: 1,
    receivedAt: receivedOrder.receivedAt ?? new Date().toISOString(),
    receivedByStaff: { ...DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK },
    order: receivedOrder,
    receivedOrders: [receivedOrder],
  };
}

export function receiveMockDiagnosticPatientsBatch(
  payload: ReceiveDiagnosticPatientsBatchRequest,
): ReceiveDiagnosticPatientResponseDto {
  const targetKeys: Array<string | number> = [];

  if (payload.orderId) targetKeys.push(payload.orderId);
  if (payload.orderCode) targetKeys.push(payload.orderCode);
  if (payload.orderIds?.length) targetKeys.push(...payload.orderIds);
  if (payload.orderCodes?.length) targetKeys.push(...payload.orderCodes);

  if (targetKeys.length === 0 && (payload.visitId || payload.visitCode)) {
    const visitCodeUpper = payload.visitCode?.trim().toUpperCase();
    for (const item of mockReceiveOrdersState) {
      if (
        item.canReceive &&
        ((payload.visitId && item.visitId === payload.visitId) ||
          (visitCodeUpper && item.visitCode.toUpperCase() === visitCodeUpper))
      ) {
        targetKeys.push(item.orderId);
      }
    }
  }

  const receivedOrders: DiagnosticPatientReceiveItemDto[] = [];

  for (const key of targetKeys) {
    const matched = findMockReceiveOrderByTarget(key);
    if (!matched) {
      throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${key}'.`);
    }
    if (!matched.canReceive || matched.status !== 'CONFIRMED') {
      throw new Error(
        matched.disabledReason ||
          `Phiếu chỉ định '${matched.orderCode}' không đủ điều kiện tiếp nhận bệnh nhân.`,
      );
    }
    receivedOrders.push(applyMockReceptionToOrder(matched, payload));
  }

  if (receivedOrders.length === 0) {
    throw new Error(
      'Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để tiếp nhận bệnh nhân.',
    );
  }

  const primaryOrder = receivedOrders[0];

  return {
    actionOutcome: receivedOrders.length > 1 ? 'BATCH_PATIENTS_RECEIVED' : 'PATIENT_RECEIVED',
    receivedCount: receivedOrders.length,
    receivedAt: primaryOrder.receivedAt ?? new Date().toISOString(),
    receivedByStaff: { ...DIAGNOSTIC_RECEIVE_STAFF_PROFILE_MOCK },
    order: primaryOrder,
    receivedOrders,
  };
}
