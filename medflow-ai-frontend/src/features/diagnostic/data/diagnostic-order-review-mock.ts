import type {
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrderResponseDto,
  ConfirmDiagnosticOrdersBatchRequest,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticOrderReviewDetailResponseDto,
  DiagnosticOrderReviewItemDto,
  DiagnosticOrderReviewListResponseDto,
  DiagnosticOrderReviewSummaryStatsDto,
  DiagnosticServicePointOptionDto,
  DiagnosticStaffProfile,
  GetDiagnosticOrdersForReviewParams,
} from '../types/diagnostic-order-review.types';

export const DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK: DiagnosticStaffProfile = {
  userId: 304,
  email: 'diagnostic@medflow.vn',
  fullName: 'CN. Lê Hoàng Minh',
  staffCode: 'DIAG-009',
  hospitalId: 1,
  hospitalCode: 'BV-DH-YD',
  hospitalName: 'Bệnh viện Đại học Y Dược MedFlow',
  departmentId: 12,
  departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
  roles: ['DIAGNOSTIC_STAFF'],
  permissions: [
    'DIAGNOSTIC_RESULT_READ',
    'DIAGNOSTIC_RESULT_WRITE',
    'DIAGNOSTIC_ORDER',
    'QUEUE_VIEW',
    'QUEUE_MANAGE',
  ],
};

export const DIAGNOSTIC_SERVICE_POINTS_MOCK: DiagnosticServicePointOptionDto[] = [
  {
    servicePointId: 201,
    name: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    serviceId: 501,
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    isActive: true,
    isRoomActive: true,
  },
  {
    servicePointId: 202,
    name: 'Bàn Siêu âm Doppler màu 02',
    serviceId: 503,
    roomId: 22,
    roomCode: 'P.SA-202',
    roomName: 'Phòng Siêu âm 202',
    roomLocation: 'Tầng 2 - Khu C',
    isActive: true,
    isRoomActive: true,
  },
  {
    servicePointId: 204,
    name: 'Buồng chụp X-quang kỹ thuật số 04',
    serviceId: 504,
    roomId: 24,
    roomCode: 'P.XQ-204',
    roomName: 'Phòng X-quang 204',
    roomLocation: 'Tầng 2 - Khu C',
    isActive: true,
    isRoomActive: true,
  },
  {
    servicePointId: 205,
    name: 'Buồng Điện tâm đồ & Thăm dò chức năng 05',
    serviceId: 502,
    roomId: 25,
    roomCode: 'P.TDCN-205',
    roomName: 'Phòng Thăm dò chức năng 205',
    roomLocation: 'Tầng 2 - Khu A',
    isActive: true,
    isRoomActive: true,
  },
];

const INITIAL_DIAGNOSTIC_ORDER_REVIEW_ITEMS: DiagnosticOrderReviewItemDto[] = [
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
    status: 'ORDERED',
    requiredForConclusion: true,
    instruction:
      'Bệnh nhân đã nhịn ăn sáng trên 8 giờ. Ưu tiên lấy mẫu sớm để đánh giá đường huyết cấp.',
    orderedAt: '2026-10-20T08:30:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    confirmationNote: null,
    queueEntryId: 9044,
    formattedQueueEntryId: 'QE-00009044',
    ticketNo: 'C021',
    queueNumber: 21,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 1,
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
    canConfirm: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canConfirm: true,
      disabledReason: null,
      isOrderPendingConfirmation: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 45,
    orderCode: 'DIA-202610-0045',
    visitId: 1005,
    visitCode: 'VIS-202610-1005',
    visitStatus: 'IN_CONSULTATION',
    patient: {
      patientId: 2045,
      patientCode: 'PAT-2045',
      fullName: 'Hoàng Đức Em',
      dateOfBirth: '1965-11-09',
      age: 61,
      gender: 'MALE',
      phone: '0912345678',
      bloodType: 'A+',
      allergies: 'Không ghi nhận tiền sử dị ứng thuốc',
    },
    orderingDoctorId: 112,
    orderingDoctorCode: 'DOC-112',
    orderingDoctorName: 'THS.BS. Vũ Thanh Hà',
    serviceId: 502,
    serviceCode: 'TDCN-ECG-012',
    serviceName: 'Điện tâm đồ 12 chuyển đạo (ECG)',
    category: 'FUNCTIONAL',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 2,
    specialtyCode: 'TIM-MACH',
    specialtyName: 'Tim mạch can thiệp',
    unitPrice: 120000,
    requiresResultVerification: true,
    priority: 'URGENT',
    status: 'ORDERED',
    requiredForConclusion: true,
    instruction:
      'Bệnh nhân nghỉ ngơi 10 phút trước đo, theo dõi sát đoạn ST-T ở chuyển đạo trước tim.',
    orderedAt: '2026-10-20T08:35:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    confirmationNote: null,
    queueEntryId: 9045,
    formattedQueueEntryId: 'QE-00009045',
    ticketNo: 'C022',
    queueNumber: 22,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 1,
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
      'Đánh giá bổ sung thiếu máu cơ tim sau khi có kết quả Troponin T ranh giới.',
    canConfirm: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canConfirm: true,
      disabledReason: null,
      isOrderPendingConfirmation: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 47,
    orderCode: 'DIA-202610-0047',
    visitId: 1007,
    visitCode: 'VIS-202610-1007',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2078,
      patientCode: 'PAT-2078',
      fullName: 'Lê Thị Mai Hương',
      dateOfBirth: '1988-07-22',
      age: 38,
      gender: 'FEMALE',
      phone: '0933456789',
      bloodType: 'B+',
      allergies: null,
    },
    orderingDoctorId: 108,
    orderingDoctorCode: 'DOC-108',
    orderingDoctorName: 'BS.CKI. Nguyễn Minh Khôi',
    serviceId: 503,
    serviceCode: 'CDHA-SA-003',
    serviceName: 'Siêu âm ổ bụng tổng quát Doppler màu',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 5,
    specialtyCode: 'TIEU-HOA',
    specialtyName: 'Nội tiêu hóa - Gan mật',
    unitPrice: 250000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'ORDERED',
    requiredForConclusion: true,
    instruction: 'Khảo sát kỹ túi mật, đường mật trong và ngoài gan; bệnh nhân nhịn tiểu vừa phải.',
    orderedAt: '2026-10-20T08:42:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    confirmationNote: null,
    queueEntryId: 9047,
    formattedQueueEntryId: 'QE-00009047',
    ticketNo: 'C024',
    queueNumber: 24,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 1,
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
    canConfirm: true,
    disabledReason: null,
    eligibility: {
      isEligible: true,
      canConfirm: true,
      disabledReason: null,
      isOrderPendingConfirmation: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 48,
    orderCode: 'DIA-202610-0048',
    visitId: 1008,
    visitCode: 'VIS-202610-1008',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2091,
      patientCode: 'PAT-2091',
      fullName: 'Ngô Văn Phúc',
      dateOfBirth: '1979-02-14',
      age: 47,
      gender: 'MALE',
      phone: '0977654321',
      bloodType: 'AB+',
      allergies: 'Tiền sử đặt vít cố định kim loại vùng cổ chân trái (năm 2019)',
    },
    orderingDoctorId: 115,
    orderingDoctorCode: 'DOC-115',
    orderingDoctorName: 'BS.CKII. Trần Quốc Bảo',
    serviceId: 507,
    serviceCode: 'CDHA-MRI-007',
    serviceName: 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 7,
    specialtyCode: 'NGOAI-TK',
    specialtyName: 'Ngoại Thần kinh - Cột sống',
    unitPrice: 1850000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'ORDERED',
    requiredForConclusion: true,
    instruction: 'Đánh giá thoát vị đĩa đệm L4-L5, L5-S1 và chèn ép rễ thần kinh.',
    orderedAt: '2026-10-20T08:48:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    confirmationNote: null,
    queueEntryId: 9048,
    formattedQueueEntryId: 'QE-00009048',
    ticketNo: 'C025',
    queueNumber: 25,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 1,
    servicePointId: 207,
    servicePointName: 'Buồng chụp MRI 1.5 Tesla 07',
    roomId: 27,
    roomCode: 'P.MRI-207',
    roomName: 'Phòng MRI 207',
    roomLocation: 'Tầng 1 - Khu Kỹ thuật cao',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canConfirm: false,
    disabledReason:
      "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
    eligibility: {
      isEligible: false,
      canConfirm: false,
      disabledReason:
        "Phòng thực hiện cận lâm sàng 'P.MRI-207' cho dịch vụ 'Chụp cộng hưởng từ (MRI) cột sống thắt lưng' hiện đang bảo trì hoặc ngừng hoạt động.",
      isOrderPendingConfirmation: true,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: false,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 42,
    orderCode: 'DIA-202610-0042',
    visitId: 1002,
    visitCode: 'VIS-202610-1002',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 1930,
      patientCode: 'PAT-1930',
      fullName: 'Trần Thị Bích',
      dateOfBirth: '1983-09-12',
      age: 43,
      gender: 'FEMALE',
      phone: '0945112233',
      bloodType: 'A+',
      allergies: null,
    },
    orderingDoctorId: 112,
    orderingDoctorCode: 'DOC-112',
    orderingDoctorName: 'THS.BS. Vũ Thanh Hà',
    serviceId: 504,
    serviceCode: 'CDHA-XQ-004',
    serviceName: 'Chụp X-quang ngực thẳng kỹ thuật số',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 3,
    specialtyCode: 'HO-HAP',
    specialtyName: 'Nội Hô hấp',
    unitPrice: 150000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Tháo trang sức kim loại vùng ngực trước khi vào buồng chụp.',
    orderedAt: '2026-10-20T08:15:00Z',
    confirmedAt: '2026-10-20T08:19:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã đối chiếu thông tin hành chính và chỉ định lâm sàng hợp lệ.',
    queueEntryId: 9042,
    formattedQueueEntryId: 'QE-00009042',
    ticketNo: 'C019',
    queueNumber: 19,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 2,
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
    canConfirm: false,
    disabledReason:
      "Phiếu chỉ định đang ở trạng thái 'CONFIRMED'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
    eligibility: {
      isEligible: true,
      canConfirm: false,
      disabledReason:
        "Phiếu chỉ định đang ở trạng thái 'CONFIRMED'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
      isOrderPendingConfirmation: false,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 41,
    orderCode: 'DIA-202610-0041',
    visitId: 1001,
    visitCode: 'VIS-202610-1001',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 1842,
      patientCode: 'PAT-1842',
      fullName: 'Nguyễn Văn An',
      dateOfBirth: '1978-05-03',
      age: 48,
      gender: 'MALE',
      phone: '0988776655',
      bloodType: 'O+',
      allergies: null,
    },
    orderingDoctorId: 108,
    orderingDoctorCode: 'DOC-108',
    orderingDoctorName: 'BS.CKI. Nguyễn Minh Khôi',
    serviceId: 501,
    serviceCode: 'XN-HH-001',
    serviceName: 'Tổng phân tích tế bào máu ngoại vi (CBC)',
    category: 'LABORATORY',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 1,
    specialtyCode: 'NOI-TQ',
    specialtyName: 'Nội tổng quát',
    unitPrice: 95000,
    requiresResultVerification: true,
    priority: 'URGENT',
    status: 'IN_PROGRESS',
    requiredForConclusion: true,
    instruction: 'Lấy 2ml máu tĩnh mạch chống đông EDTA.',
    orderedAt: '2026-10-20T08:10:00Z',
    confirmedAt: '2026-10-20T08:13:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã tiếp nhận bệnh nhân và lấy mẫu máu tại bàn số 1.',
    queueEntryId: 9041,
    formattedQueueEntryId: 'QE-00009041',
    ticketNo: 'C018',
    queueNumber: 18,
    queueDate: '2026-10-20',
    queueStatus: 'IN_SERVICE',
    presenceStatus: 'PRESENT',
    stateVersion: 3,
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
    canConfirm: false,
    disabledReason:
      "Phiếu chỉ định đang ở trạng thái 'IN_PROGRESS'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
    eligibility: {
      isEligible: true,
      canConfirm: false,
      disabledReason:
        "Phiếu chỉ định đang ở trạng thái 'IN_PROGRESS'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
      isOrderPendingConfirmation: false,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
  {
    orderId: 46,
    orderCode: 'DIA-202610-0046',
    visitId: 1006,
    visitCode: 'VIS-202610-1006',
    visitStatus: 'IN_CONSULTATION',
    patient: {
      patientId: 2060,
      patientCode: 'PAT-2060',
      fullName: 'Vũ Thị Phương',
      dateOfBirth: '1959-12-01',
      age: 67,
      gender: 'FEMALE',
      phone: '0966889900',
      bloodType: 'B+',
      allergies: 'Tiền sử tăng huyết áp đang dùng Amlodipin 5mg',
    },
    orderingDoctorId: 112,
    orderingDoctorCode: 'DOC-112',
    orderingDoctorName: 'THS.BS. Vũ Thanh Hà',
    serviceId: 506,
    serviceCode: 'CDHA-CT-006',
    serviceName: 'Chụp CT sọ não không tiêm thuốc cản quang',
    category: 'IMAGING',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 6,
    specialtyCode: 'NOI-TK',
    specialtyName: 'Nội Thần kinh',
    unitPrice: 950000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'COMPLETED',
    requiredForConclusion: true,
    instruction: 'Loại trừ xuất huyết nội sọ cấp tính.',
    orderedAt: '2026-10-20T07:55:00Z',
    confirmedAt: '2026-10-20T07:58:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    confirmationNote: 'Đã hoàn tất chụp CT và chuyển bác sĩ CĐHA đọc kết quả.',
    queueEntryId: 9046,
    formattedQueueEntryId: 'QE-00009046',
    ticketNo: 'C023',
    queueNumber: 23,
    queueDate: '2026-10-20',
    queueStatus: 'COMPLETED',
    presenceStatus: 'PRESENT',
    stateVersion: 4,
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
    canConfirm: false,
    disabledReason:
      "Phiếu chỉ định đang ở trạng thái 'COMPLETED'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
    eligibility: {
      isEligible: true,
      canConfirm: false,
      disabledReason:
        "Phiếu chỉ định đang ở trạng thái 'COMPLETED'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.",
      isOrderPendingConfirmation: false,
      isVisitEligible: true,
      isPatientActive: true,
      isServiceActive: true,
      isDepartmentActive: true,
      isDoctorAuthorizedForService: true,
      isServicePointActive: true,
      isRoomActive: true,
      isQueueEntryEligible: true,
    },
  },
];

const CLINICAL_CONTEXT_BY_VISIT_ID: Record<number, DiagnosticOrderClinicalSummaryDto> = {
  1004: {
    visitId: 1004,
    visitCode: 'VIS-202610-1004',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Khát nước nhiều, tiểu đêm 3-4 lần, mệt mỏi kéo dài 2 tuần',
    presentingComplaint:
      'Bệnh nhân nữ 54 tuổi có tiền sử đái tháo đường típ 2, đợt này đường huyết mao mạch tại nhà dao động 11-14 mmol/L.',
    clinicalFindings:
      'Huyết áp 135/85 mmHg, mạch 84 lần/phút, BMI 25.4, niêm mạc hồng, không phù ngoại biên.',
    primaryDiagnosisCode: 'E11.65',
    primaryDiagnosisName:
      'Đái tháo đường không phụ thuộc insuline có tăng đường huyết chưa kiểm soát',
    clinicalDiagnosis:
      'Theo dõi đái tháo đường típ 2 mất bù đường huyết - đánh giá chức năng thận nền',
  },
  1005: {
    visitId: 1005,
    visitCode: 'VIS-202610-1005',
    visitStatus: 'IN_CONSULTATION',
    reasonForVisit: 'Đau tức ngực trái âm ỉ lan lên vai trái khi gắng sức nhẹ',
    presentingComplaint:
      'Cơn đau ngực xuất hiện 2 ngày nay, kéo dài 5-10 phút mỗi cơn, giảm khi nghỉ ngơi.',
    clinicalFindings:
      'Tim nhịp đều 92 chu kỳ/phút, T1 T2 rõ, huyết áp 145/90 mmHg, SpO2 98% khí trời.',
    primaryDiagnosisCode: 'I20.8',
    primaryDiagnosisName: 'Các dạng cơn đau thắt ngực khác',
    clinicalDiagnosis:
      'Cơn đau thắt ngực không ổn định - cần đo ECG 12 chuyển đạo đối chiếu bổ sung',
  },
  1007: {
    visitId: 1007,
    visitCode: 'VIS-202610-1007',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Đau âm ỉ hạ sườn phải sau bữa ăn nhiều dầu mỡ, đầy bụng khó tiêu',
    presentingComplaint:
      'Đau quặn từng cơn nhẹ vùng hạ sườn phải khoảng 5 ngày nay, không sốt, không vàng da.',
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
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Sốt cao 38.9 độ C, đau đầu, mỏi cơ toàn thân ngày thứ 2',
    presentingComplaint: 'Khởi phát sốt đột ngột, mệt mỏi nhiều, ăn uống kém.',
    clinicalFindings: 'Da xung huyết nhẹ, họng đỏ, nghiệm pháp dây thắt âm tính.',
    primaryDiagnosisCode: 'A97.0',
    primaryDiagnosisName: 'Sốt xuất huyết Dengue không có dấu hiệu cảnh báo',
    clinicalDiagnosis: 'Theo dõi sốt xuất huyết Dengue ngày 2 - kiểm tra công thức máu & tiểu cầu',
  },
  1006: {
    visitId: 1006,
    visitCode: 'VIS-202610-1006',
    visitStatus: 'IN_CONSULTATION',
    reasonForVisit: 'Chóng mặt xoay tròn, tê nhẹ nửa mặt trái thoáng qua',
    presentingComplaint: 'Khởi phát cơn chóng mặt sáng sớm khi ngủ dậy, kèm buồn nôn nhẹ.',
    clinicalFindings:
      'Tỉnh táo, GCS 15 điểm, không yếu liệt tứ chi khu trú rõ, huyết áp 150/90 mmHg.',
    primaryDiagnosisCode: 'G45.9',
    primaryDiagnosisName: 'Cơn thiếu máu não cục bộ thoáng qua, không đặc hiệu',
    clinicalDiagnosis: 'Theo dõi cơn thiếu máu não thoáng qua (TIA) hệ tuần hoàn sau',
  },
};

function cloneItems(items: DiagnosticOrderReviewItemDto[]): DiagnosticOrderReviewItemDto[] {
  return items.map((item) => ({
    ...item,
    patient: { ...item.patient },
    eligibility: { ...item.eligibility },
  }));
}

let mockOrdersState: DiagnosticOrderReviewItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_ORDER_REVIEW_ITEMS,
);

export const DIAGNOSTIC_ORDER_REVIEW_ITEMS_MOCK: DiagnosticOrderReviewItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_ORDER_REVIEW_ITEMS,
);

export function resetDiagnosticOrderReviewMockState(): void {
  mockOrdersState = cloneItems(INITIAL_DIAGNOSTIC_ORDER_REVIEW_ITEMS);
}

function computeSummaryStats(
  items: DiagnosticOrderReviewItemDto[],
): DiagnosticOrderReviewSummaryStatsDto {
  let pendingConfirmationCount = 0;
  let confirmedCount = 0;
  let inProgressCount = 0;
  let completedCount = 0;
  let cancelledCount = 0;
  let urgentOrStatPendingCount = 0;

  for (const item of items) {
    if (item.status === 'ORDERED') {
      pendingConfirmationCount += 1;
      if (item.priority === 'URGENT' || item.priority === 'STAT') {
        urgentOrStatPendingCount += 1;
      }
    } else if (item.status === 'CONFIRMED') {
      confirmedCount += 1;
    } else if (item.status === 'IN_PROGRESS') {
      inProgressCount += 1;
    } else if (item.status === 'COMPLETED') {
      completedCount += 1;
    } else if (item.status === 'CANCELLED') {
      cancelledCount += 1;
    }
  }

  return {
    totalOrders: items.length,
    pendingConfirmationCount,
    confirmedCount,
    inProgressCount,
    completedCount,
    cancelledCount,
    urgentOrStatPendingCount,
  };
}

function findMockOrderByTarget(
  idOrCode: string | number,
): DiagnosticOrderReviewItemDto | undefined {
  const raw = String(idOrCode).trim();
  if (!raw) return undefined;
  const upper = raw.toUpperCase();
  const numericId = Number(raw);

  return mockOrdersState.find((item) => {
    if (!Number.isNaN(numericId) && item.orderId === numericId) return true;
    if (item.orderCode.toUpperCase() === upper) return true;
    if (item.ticketNo && item.ticketNo.toUpperCase() === upper) return true;

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

export function resolveMockDiagnosticOrdersForReview(
  params?: GetDiagnosticOrdersForReviewParams,
): DiagnosticOrderReviewListResponseDto {
  const allItems = cloneItems(mockOrdersState);
  const summary = computeSummaryStats(allItems);

  const statusFilter = params?.status?.trim().toUpperCase();
  const categoryFilter = params?.category?.trim().toUpperCase();
  const priorityFilter = params?.priority?.trim().toUpperCase();
  const keyword = params?.keyword?.trim().toLowerCase() ?? '';
  const pendingOnly = Boolean(params?.pendingOnly);

  let filtered = allItems.filter((item) => {
    if (pendingOnly && item.status !== 'ORDERED') {
      return false;
    }
    if (statusFilter && statusFilter !== 'ALL' && item.status.toUpperCase() !== statusFilter) {
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
    hospitalId: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalName,
    departmentId: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.departmentId,
    departmentName: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.departmentName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK },
    summary,
    availableServicePoints: DIAGNOSTIC_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
    items: filtered,
  };
}

export function resolveMockDiagnosticOrderReviewDetail(
  idOrCode: string,
): DiagnosticOrderReviewDetailResponseDto {
  const matched = findMockOrderByTarget(idOrCode) ?? mockOrdersState[0];
  const orderCopy: DiagnosticOrderReviewItemDto = {
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
    hospitalId: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.hospitalName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK },
    order: orderCopy,
    clinicalContext: { ...clinicalContext },
    availableServicePoints: DIAGNOSTIC_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
  };
}

function applyMockConfirmationToOrder(
  target: DiagnosticOrderReviewItemDto,
  payload?: ConfirmDiagnosticOrderRequest,
): DiagnosticOrderReviewItemDto {
  const nowIso = new Date().toISOString();
  const chosenServicePoint = payload?.servicePointId
    ? DIAGNOSTIC_SERVICE_POINTS_MOCK.find((sp) => sp.servicePointId === payload.servicePointId)
    : undefined;

  const updatedPriority = payload?.priority?.trim()
    ? payload.priority.trim().toUpperCase() === 'STANDARD' ||
      payload.priority.trim().toUpperCase() === 'ROUTINE'
      ? 'NORMAL'
      : payload.priority.trim().toUpperCase()
    : target.priority;

  const updatedNote =
    payload?.confirmationNote?.trim() ||
    payload?.reviewNote?.trim() ||
    payload?.notes?.trim() ||
    'Đã đối chiếu thông tin hành chính và xác nhận chỉ định cận lâm sàng hợp lệ.';

  const updatedInstruction =
    payload?.instruction !== undefined && payload?.instruction !== null
      ? payload.instruction.trim() || target.instruction
      : target.instruction;

  target.status = 'CONFIRMED';
  target.priority = updatedPriority;
  target.instruction = updatedInstruction;
  target.confirmedAt = nowIso;
  target.confirmedByStaffId = DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.userId;
  target.confirmedByStaffCode = DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009';
  target.confirmedByStaffName = DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK.fullName;
  target.confirmationNote = updatedNote;
  target.presenceStatus = payload?.patientPresent === false ? 'ABSENT' : 'PRESENT';
  target.queueStatus = 'WAITING';
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

  target.canConfirm = false;
  target.disabledReason =
    "Phiếu chỉ định đang ở trạng thái 'CONFIRMED'. Chỉ phiếu ở trạng thái 'ORDERED' mới được phép xác nhận.";
  target.eligibility = {
    ...target.eligibility,
    canConfirm: false,
    isOrderPendingConfirmation: false,
    disabledReason: target.disabledReason,
  };

  return {
    ...target,
    patient: { ...target.patient },
    eligibility: { ...target.eligibility },
  };
}

export function confirmMockDiagnosticOrder(
  idOrCode: string,
  payload?: ConfirmDiagnosticOrderRequest,
): ConfirmDiagnosticOrderResponseDto {
  const matched = findMockOrderByTarget(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${idOrCode}'.`);
  }

  if (!matched.canConfirm || matched.status !== 'ORDERED') {
    throw new Error(
      matched.disabledReason ||
        `Phiếu chỉ định '${matched.orderCode}' không đủ điều kiện xác nhận.`,
    );
  }

  const confirmedOrder = applyMockConfirmationToOrder(matched, payload);

  return {
    actionOutcome: 'CONFIRMED',
    confirmedCount: 1,
    confirmedAt: confirmedOrder.confirmedAt ?? new Date().toISOString(),
    confirmedByStaff: { ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK },
    order: confirmedOrder,
    confirmedOrders: [confirmedOrder],
  };
}

export function confirmMockDiagnosticOrdersBatch(
  payload: ConfirmDiagnosticOrdersBatchRequest,
): ConfirmDiagnosticOrderResponseDto {
  const targetKeys: Array<string | number> = [];

  if (payload.orderId) targetKeys.push(payload.orderId);
  if (payload.orderCode) targetKeys.push(payload.orderCode);
  if (payload.orderIds?.length) targetKeys.push(...payload.orderIds);
  if (payload.orderCodes?.length) targetKeys.push(...payload.orderCodes);

  const confirmedOrders: DiagnosticOrderReviewItemDto[] = [];

  for (const key of targetKeys) {
    const matched = findMockOrderByTarget(key);
    if (!matched) {
      throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${key}'.`);
    }
    if (!matched.canConfirm || matched.status !== 'ORDERED') {
      throw new Error(
        matched.disabledReason ||
          `Phiếu chỉ định '${matched.orderCode}' không đủ điều kiện xác nhận.`,
      );
    }
    confirmedOrders.push(applyMockConfirmationToOrder(matched, payload));
  }

  if (confirmedOrders.length === 0) {
    throw new Error('Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để xác nhận.');
  }

  const primaryOrder = confirmedOrders[0];

  return {
    actionOutcome: confirmedOrders.length > 1 ? 'BATCH_CONFIRMED' : 'CONFIRMED',
    confirmedCount: confirmedOrders.length,
    confirmedAt: primaryOrder.confirmedAt ?? new Date().toISOString(),
    confirmedByStaff: { ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK },
    order: primaryOrder,
    confirmedOrders,
  };
}
