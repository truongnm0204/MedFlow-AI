import type {
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticServicePointOptionDto,
  DiagnosticStaffProfile,
} from '../types/diagnostic-order-review.types';
import type {
  DiagnosticResultEntryDetailResponseDto,
  DiagnosticResultEntryItemDto,
  DiagnosticResultEntryListResponseDto,
  DiagnosticResultEntrySummaryStatsDto,
  DiagnosticResultHistoryItemDto,
  DiagnosticResultVersionItemDto,
  EnterDiagnosticResultRequest,
  EnterDiagnosticResultResponseDto,
  GetDiagnosticOrdersForResultEntryParams,
} from '../types/diagnostic-result-entry.types';
import {
  DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
  DIAGNOSTIC_SERVICE_POINTS_MOCK,
} from './diagnostic-order-review-mock';

export const DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK: DiagnosticStaffProfile = {
  ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
};

export const DIAGNOSTIC_RESULT_ENTRY_SERVICE_POINTS_MOCK: DiagnosticServicePointOptionDto[] =
  DIAGNOSTIC_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp }));

const ELIGIBILITY_BASE = {
  isEligible: true,
  canEnterResult: true,
  canFinalizeResult: true,
  disabledReason: null,
  isOrderExecutionStartedOrCompleted: true,
  isResultAlreadyFinalized: false,
  isVisitEligible: true,
  isMedicalRecordOpen: true,
  isPatientActive: true,
  isServiceActive: true,
  isDepartmentActive: true,
  isServicePointActive: true,
  isRoomActive: true,
  hasResultWritePermission: true,
};

const RESULT_FIELD_NULLS = {
  hasResult: false,
  isResultReady: false,
  isResultFinalized: false,
  resultId: null,
  resultFinalizedAt: null,
  finalizedByStaffId: null,
  finalizedByStaffCode: null,
  finalizedByStaffName: null,
  latestVersionNo: null,
  resultText: null,
  conclusion: null,
  reportFileUrl: null,
  enteredByStaffId: null,
  enteredByStaffCode: null,
  enteredByStaffName: null,
  enteredAt: null,
  verifiedByStaffId: null,
  verifiedByStaffCode: null,
  verifiedByStaffName: null,
  verifiedAt: null,
  correctionReason: null,
} as const;

const INITIAL_DIAGNOSTIC_RESULT_ENTRY_ITEMS: DiagnosticResultEntryItemDto[] = [
  // 1. IN_PROGRESS, no result yet — eligible for entry (STAT).
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
    status: 'IN_PROGRESS',
    orderStatus: 'IN_PROGRESS',
    requiredForConclusion: true,
    instruction:
      'Bệnh nhân đã nhịn ăn sáng trên 8 giờ. Ưu tiên lấy mẫu sớm để đánh giá đường huyết cấp.',
    orderedAt: '2026-10-20T08:30:00Z',
    confirmedAt: '2026-10-20T08:34:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: '2026-10-20T08:52:00Z',
    completedAt: null,
    queueEntryId: 9044,
    formattedQueueEntryId: 'QE-00009044',
    ticketNo: 'C021',
    queueNumber: 21,
    queueDate: '2026-10-20',
    queueStatus: 'IN_PROGRESS',
    presenceStatus: 'PRESENT',
    stateVersion: 3,
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    ...RESULT_FIELD_NULLS,
    resultStatus: 'NONE',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: true,
    canFinalizeResult: true,
    disabledReason: null,
    eligibility: { ...ELIGIBILITY_BASE },
    versions: [],
  },
  // 2. IN_PROGRESS with an unfinalized DRAFT version — editable.
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
    status: 'IN_PROGRESS',
    orderStatus: 'IN_PROGRESS',
    requiredForConclusion: true,
    instruction: 'Theo dõi nhịp tim nghỉ ngơi 5 phút trước khi đo ECG.',
    orderedAt: '2026-10-20T08:40:00Z',
    confirmedAt: '2026-10-20T08:43:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: '2026-10-20T08:55:00Z',
    completedAt: null,
    queueEntryId: 9045,
    formattedQueueEntryId: 'QE-00009045',
    ticketNo: 'C022',
    queueNumber: 22,
    queueDate: '2026-10-20',
    queueStatus: 'IN_PROGRESS',
    presenceStatus: 'PRESENT',
    stateVersion: 4,
    servicePointId: 205,
    servicePointName: 'Buồng Điện tâm đồ & Thăm dò chức năng 05',
    roomId: 25,
    roomCode: 'P.TDCN-205',
    roomName: 'Phòng Thăm dò chức năng 205',
    roomLocation: 'Tầng 2 - Khu A',
    ...RESULT_FIELD_NULLS,
    hasResult: true,
    isResultReady: true,
    resultId: 9002,
    resultStatus: 'DRAFT',
    latestVersionNo: 1,
    resultText:
      'Điện tâm đồ: nhịp xoang nhanh 98 ck/p, ngoại tâm thu thất rải rác 6-8 lần/phút. Siêu âm tim: FE trái thất 55%, phân suất chèn ép nhẹ.',
    conclusion: 'Ngoại tâm thu thất rải rác — đề nghị theo dõi Holter nếu triệu chứng tăng.',
    reportFileUrl: 'https://pacs.medflow.vn/reports/ecg-DIA-202610-0045-draft.pdf',
    enteredByStaffId: 304,
    enteredByStaffCode: 'DIAG-009',
    enteredByStaffName: 'CN. Lê Hoàng Minh',
    enteredAt: '2026-10-20T09:12:00Z',
    isAdditionalOrder: true,
    orderSequenceNo: 2,
    followUpToOrderId: 38,
    followUpToOrderCode: 'DIA-202610-0038',
    additionalOrderReason:
      'Điện tâm đồ sàng lọc có ngoại tâm thu thất rải rác, cần đánh giá chức năng tâm thu thất trái.',
    canEnterResult: true,
    canFinalizeResult: true,
    disabledReason: null,
    eligibility: { ...ELIGIBILITY_BASE },
    versions: [
      {
        versionId: 9101,
        resultId: 9002,
        versionNo: 1,
        resultText:
          'Điện tâm đồ: nhịp xoang nhanh 98 ck/p, ngoại tâm thu thất rải rác 6-8 lần/phút. Siêu âm tim: FE trái thất 55%, phân suất chèn ép nhẹ.',
        conclusion: 'Ngoại tâm thu thất rải rác — đề nghị theo dõi Holter nếu triệu chứng tăng.',
        reportFileUrl: 'https://pacs.medflow.vn/reports/ecg-DIA-202610-0045-draft.pdf',
        isFinalized: false,
        enteredByStaffId: 304,
        enteredByStaffCode: 'DIAG-009',
        enteredByStaffName: 'CN. Lê Hoàng Minh',
        enteredAt: '2026-10-20T09:12:00Z',
        verifiedByStaffId: null,
        verifiedByStaffCode: null,
        verifiedByStaffName: null,
        verifiedAt: null,
        correctionReason: null,
      },
    ],
  },
  // 3. IN_PROGRESS + PENDING_VERIFY — awaiting verification, ready for finalize.
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
    status: 'IN_PROGRESS',
    orderStatus: 'IN_PROGRESS',
    requiredForConclusion: true,
    instruction: 'Bệnh nhân nhịn ăn và uống đủ nước trước siêu âm.',
    orderedAt: '2026-10-20T08:45:00Z',
    confirmedAt: '2026-10-20T08:48:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: '2026-10-20T09:00:00Z',
    completedAt: null,
    queueEntryId: 9046,
    formattedQueueEntryId: 'QE-00009046',
    ticketNo: 'C023',
    queueNumber: 23,
    queueDate: '2026-10-20',
    queueStatus: 'IN_PROGRESS',
    presenceStatus: 'PRESENT',
    stateVersion: 5,
    servicePointId: 202,
    servicePointName: 'Bàn Siêu âm Doppler màu 02',
    roomId: 22,
    roomCode: 'P.SA-202',
    roomName: 'Phòng Siêu âm 202',
    roomLocation: 'Tầng 2 - Khu C',
    ...RESULT_FIELD_NULLS,
    hasResult: true,
    isResultReady: true,
    resultId: 9003,
    resultStatus: 'PENDING_VERIFY',
    latestVersionNo: 2,
    resultText:
      'Siêu âm ổ bụng: túi mật không giãn, thành túi mật mỏng; bùn túi mật lắng đọng lớp mỏng. Gan mật: nhu mô gan đồng nhất, không u nang.',
    conclusion: 'Bùn túi mật — chưa ghi nhận sỏi có bóng lưng; đề nghị theo dõi định kỳ.',
    reportFileUrl: 'https://pacs.medflow.vn/reports/us-DIA-202610-0046-v2.pdf',
    enteredByStaffId: 304,
    enteredByStaffCode: 'DIAG-009',
    enteredByStaffName: 'CN. Lê Hoàng Minh',
    enteredAt: '2026-10-20T09:25:00Z',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: true,
    canFinalizeResult: true,
    disabledReason: null,
    eligibility: { ...ELIGIBILITY_BASE },
    versions: [
      {
        versionId: 9102,
        resultId: 9003,
        versionNo: 1,
        resultText:
          'Siêu âm ổ bụng: túi mật không giãn; hình ảnh cộm âm trong lòng túi mật, nghi sỏi nhỏ.',
        conclusion: 'Nghi sỏi túi mật nhỏ — cần chụp lại tư thế khác để xác nhận.',
        reportFileUrl: null,
        isFinalized: false,
        enteredByStaffId: 304,
        enteredByStaffCode: 'DIAG-009',
        enteredByStaffName: 'CN. Lê Hoàng Minh',
        enteredAt: '2026-10-20T09:15:00Z',
        verifiedByStaffId: null,
        verifiedByStaffCode: null,
        verifiedByStaffName: null,
        verifiedAt: null,
        correctionReason: null,
      },
      {
        versionId: 9103,
        resultId: 9003,
        versionNo: 2,
        resultText:
          'Siêu âm ổ bụng: túi mật không giãn, thành túi mật mỏng; bùn túi mật lắng đọng lớp mỏng. Gan mật: nhu mô gan đồng nhất, không u nang.',
        conclusion: 'Bùn túi mật — chưa ghi nhận sỏi có bóng lưng; đề nghị theo dõi định kỳ.',
        reportFileUrl: 'https://pacs.medflow.vn/reports/us-DIA-202610-0046-v2.pdf',
        isFinalized: false,
        enteredByStaffId: 304,
        enteredByStaffCode: 'DIAG-009',
        enteredByStaffName: 'CN. Lê Hoàng Minh',
        enteredAt: '2026-10-20T09:25:00Z',
        verifiedByStaffId: null,
        verifiedByStaffCode: null,
        verifiedByStaffName: null,
        verifiedAt: null,
        correctionReason:
          'Chụp lại ở tư thế nghiêng trái xác nhận chỉ là bùn lắng đọng, không phải sỏi.',
      },
    ],
  },
  // 4. COMPLETED + FINALIZED — ineligible, shows lock banner.
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
    orderStatus: 'COMPLETED',
    requiredForConclusion: true,
    instruction: 'Kiểm tra số lượng tiểu cầu và Hematocrit.',
    orderedAt: '2026-10-20T07:50:00Z',
    confirmedAt: '2026-10-20T07:53:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: '2026-10-20T08:05:00Z',
    completedAt: '2026-10-20T08:35:00Z',
    queueEntryId: 9041,
    formattedQueueEntryId: 'QE-00009041',
    ticketNo: 'C018',
    queueNumber: 18,
    queueDate: '2026-10-20',
    queueStatus: 'COMPLETED',
    presenceStatus: 'PRESENT',
    stateVersion: 6,
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    ...RESULT_FIELD_NULLS,
    hasResult: true,
    isResultReady: true,
    isResultFinalized: true,
    resultId: 9001,
    resultStatus: 'VERIFIED',
    resultFinalizedAt: '2026-10-20T08:40:00Z',
    finalizedByStaffId: 304,
    finalizedByStaffCode: 'DIAG-009',
    finalizedByStaffName: 'CN. Lê Hoàng Minh',
    latestVersionNo: 1,
    resultText:
      'Công thức máu: BC 11.2 G/L, tiểu cầu 78 G/L, Hct 38%, Hemoglobin 13.1 g/dL. Cầu trúc đổ ngược cần theo dõi tiểu cầu ngày 3-5.',
    conclusion:
      'Giảm tiểu cầu nhẹ phù hợp giai đoạn đầu sốt xuất huyết — đề nghị đếm lại tiểu cầu sau 24h.',
    reportFileUrl: 'https://pacs.medflow.vn/reports/cbc-DIA-202610-0041.pdf',
    enteredByStaffId: 304,
    enteredByStaffCode: 'DIAG-009',
    enteredByStaffName: 'CN. Lê Hoàng Minh',
    enteredAt: '2026-10-20T08:32:00Z',
    verifiedByStaffId: 305,
    verifiedByStaffCode: 'DIAG-004',
    verifiedByStaffName: 'BS.CKII. Vũ Thanh Trà',
    verifiedAt: '2026-10-20T08:40:00Z',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: false,
    canFinalizeResult: false,
    disabledReason:
      "Kết quả của phiếu chỉ định 'DIA-202610-0041' đã được duyệt & trả (VERIFIED). Mọi chỉnh sửa sau duyệt cần thực hiện qua quy trình amendment (UC45).",
    eligibility: {
      ...ELIGIBILITY_BASE,
      isEligible: false,
      canEnterResult: false,
      canFinalizeResult: false,
      isResultAlreadyFinalized: true,
      disabledReason:
        "Kết quả của phiếu chỉ định 'DIA-202610-0041' đã được duyệt & trả (VERIFIED). Mọi chỉnh sửa sau duyệt cần thực hiện qua quy trình amendment (UC45).",
    },
    versions: [
      {
        versionId: 9100,
        resultId: 9001,
        versionNo: 1,
        resultText:
          'Công thức máu: BC 11.2 G/L, tiểu cầu 78 G/L, Hct 38%, Hemoglobin 13.1 g/dL. Cầu trúc đổ ngược cần theo dõi tiểu cầu ngày 3-5.',
        conclusion:
          'Giảm tiểu cầu nhẹ phù hợp giai đoạn đầu sốt xuất huyết — đề nghị đếm lại tiểu cầu sau 24h.',
        reportFileUrl: 'https://pacs.medflow.vn/reports/cbc-DIA-202610-0041.pdf',
        isFinalized: true,
        enteredByStaffId: 304,
        enteredByStaffCode: 'DIAG-009',
        enteredByStaffName: 'CN. Lê Hoàng Minh',
        enteredAt: '2026-10-20T08:32:00Z',
        verifiedByStaffId: 305,
        verifiedByStaffCode: 'DIAG-004',
        verifiedByStaffName: 'BS.CKII. Vũ Thanh Trà',
        verifiedAt: '2026-10-20T08:40:00Z',
        correctionReason: null,
      },
    ],
  },
  // 5. ORDERED — execution not started, ineligible.
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
    orderStatus: 'ORDERED',
    requiredForConclusion: true,
    instruction: 'Chụp 2 tư thế thẳng và nghiêng vùng cột sống thắt lưng L1-S1.',
    orderedAt: '2026-10-20T08:52:00Z',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    startedAt: null,
    completedAt: null,
    queueEntryId: 9047,
    formattedQueueEntryId: 'QE-00009047',
    ticketNo: 'C024',
    queueNumber: 24,
    queueDate: '2026-10-20',
    queueStatus: 'WAITING',
    presenceStatus: 'PRESENT',
    stateVersion: 1,
    servicePointId: 204,
    servicePointName: 'Buồng chụp X-quang kỹ thuật số 04',
    roomId: 24,
    roomCode: 'P.XQ-204',
    roomName: 'Phòng X-quang 204',
    roomLocation: 'Tầng 2 - Khu C',
    ...RESULT_FIELD_NULLS,
    resultStatus: 'NONE',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: false,
    canFinalizeResult: false,
    disabledReason:
      "Phiếu chỉ định cận lâm sàng 'DIA-202610-0047' chưa bắt đầu thực hiện kỹ thuật (ORDERED). Kết quả chỉ có thể nhập khi phiếu đang thực hiện (IN_PROGRESS) hoặc đã hoàn tất.",
    eligibility: {
      ...ELIGIBILITY_BASE,
      isEligible: false,
      canEnterResult: false,
      canFinalizeResult: false,
      isOrderExecutionStartedOrCompleted: false,
      disabledReason:
        "Phiếu chỉ định cận lâm sàng 'DIA-202610-0047' chưa bắt đầu thực hiện kỹ thuật (ORDERED). Kết quả chỉ có thể nhập khi phiếu đang thực hiện (IN_PROGRESS) hoặc đã hoàn tất.",
    },
    versions: [],
  },
  // 6. IN_PROGRESS — MRI, no result yet, URGENT ready for entry.
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
    requiresResultVerification: false,
    priority: 'URGENT',
    status: 'IN_PROGRESS',
    orderStatus: 'IN_PROGRESS',
    requiredForConclusion: true,
    instruction: 'Kiểm tra dị vật kim loại trong cơ thể trước khi vào phòng từ trường.',
    orderedAt: '2026-10-20T08:55:00Z',
    confirmedAt: '2026-10-20T08:58:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: '2026-10-20T09:30:00Z',
    completedAt: null,
    queueEntryId: 9048,
    formattedQueueEntryId: 'QE-00009048',
    ticketNo: 'C025',
    queueNumber: 25,
    queueDate: '2026-10-20',
    queueStatus: 'IN_PROGRESS',
    presenceStatus: 'PRESENT',
    stateVersion: 4,
    servicePointId: 207,
    servicePointName: 'Buồng chụp Cộng hưởng từ MRI 07',
    roomId: 27,
    roomCode: 'P.MRI-207',
    roomName: 'Phòng MRI 207',
    roomLocation: 'Tầng 1 - Khu Kỹ thuật cao',
    ...RESULT_FIELD_NULLS,
    resultStatus: 'NONE',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: true,
    canFinalizeResult: true,
    disabledReason: null,
    eligibility: { ...ELIGIBILITY_BASE },
    versions: [],
  },
  // 7. CONFIRMED + READY — waiting execution, not yet enterable.
  {
    orderId: 49,
    orderCode: 'DIA-202610-0049',
    visitId: 1009,
    visitCode: 'VIS-202610-1009',
    visitStatus: 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: 2024,
      patientCode: 'PAT-2024',
      fullName: 'Hoàng Thị Kim Chi',
      dateOfBirth: '1988-03-30',
      age: 38,
      gender: 'FEMALE',
      phone: '0966778899',
      bloodType: 'A-',
      allergies: null,
    },
    orderingDoctorId: 110,
    orderingDoctorCode: 'DOC-110',
    orderingDoctorName: 'BS.CKI. Phạm Gia Bảo',
    serviceId: 501,
    serviceCode: 'XN-SH-002',
    serviceName: 'Định lượng Men gan AST/ALT & Bilirubin toàn phần',
    category: 'LABORATORY',
    departmentId: 12,
    departmentCode: 'KHOA-XN',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 5,
    specialtyCode: 'TIEU-HOA',
    specialtyName: 'Tiêu hóa - Gan mật',
    unitPrice: 195000,
    requiresResultVerification: true,
    priority: 'NORMAL',
    status: 'CONFIRMED',
    orderStatus: 'CONFIRMED',
    requiredForConclusion: true,
    instruction: 'Bệnh nhân nhịn ăn 6 giờ trước lấy mẫu.',
    orderedAt: '2026-10-20T09:05:00Z',
    confirmedAt: '2026-10-20T09:08:00Z',
    confirmedByStaffId: 304,
    confirmedByStaffCode: 'DIAG-009',
    confirmedByStaffName: 'CN. Lê Hoàng Minh',
    startedAt: null,
    completedAt: null,
    queueEntryId: 9049,
    formattedQueueEntryId: 'QE-00009049',
    ticketNo: 'C026',
    queueNumber: 26,
    queueDate: '2026-10-20',
    queueStatus: 'READY',
    presenceStatus: 'PRESENT',
    stateVersion: 3,
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    ...RESULT_FIELD_NULLS,
    resultStatus: 'NONE',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canEnterResult: false,
    canFinalizeResult: false,
    disabledReason:
      "Phiếu chỉ định cận lâm sàng 'DIA-202610-0049' chưa bắt đầu thực hiện kỹ thuật (CONFIRMED/READY). Kết quả chỉ có thể nhập khi phiếu đang thực hiện (IN_PROGRESS) hoặc đã hoàn tất.",
    eligibility: {
      ...ELIGIBILITY_BASE,
      isEligible: false,
      canEnterResult: false,
      canFinalizeResult: false,
      isOrderExecutionStartedOrCompleted: false,
      disabledReason:
        "Phiếu chỉ định cận lâm sàng 'DIA-202610-0049' chưa bắt đầu thực hiện kỹ thuật (CONFIRMED/READY). Kết quả chỉ có thể nhập khi phiếu đang thực hiện (IN_PROGRESS) hoặc đã hoàn tất.",
    },
    versions: [],
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
  1009: {
    visitId: 1009,
    visitCode: 'VIS-202610-1009',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Vàng da nhẹ, mệt mỏi kèm buồn nôn 1 tuần nay',
    presentingComplaint:
      'Tiền sử viêm gan B mạn tính 10 năm, ngưng thuốc ức chế virus 6 tháng trước.',
    clinicalFindings: 'Niêm mạc mắt vàng nhẹ, gan sờ dưới bờ sườn 1cm, không dấu hiệu cổ trướng.',
    primaryDiagnosisCode: 'B18.1',
    primaryDiagnosisName: 'Viêm gan siêu vi B mạn tính không có siêu vi delta',
    clinicalDiagnosis: 'Theo dõi ngừng thuốc trên viêm gan B mạn tính — cần đánh giá men gan',
  },
};

function cloneItems(items: DiagnosticResultEntryItemDto[]): DiagnosticResultEntryItemDto[] {
  return items.map((item) => ({
    ...item,
    patient: { ...item.patient },
    eligibility: { ...item.eligibility },
    versions: item.versions.map((v) => ({ ...v })),
  }));
}

let mockResultEntryOrdersState: DiagnosticResultEntryItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_RESULT_ENTRY_ITEMS,
);

export const DIAGNOSTIC_RESULT_ENTRY_ITEMS_MOCK: DiagnosticResultEntryItemDto[] = cloneItems(
  INITIAL_DIAGNOSTIC_RESULT_ENTRY_ITEMS,
);

export const DIAGNOSTIC_RESULT_ENTRY_LIST_MOCK: DiagnosticResultEntryListResponseDto = {
  hospitalId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalId,
  hospitalCode: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalCode,
  hospitalName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalName,
  departmentId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.departmentId,
  departmentName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.departmentName,
  generatedAtUtc: '2026-10-20T09:40:00Z',
  staffProfile: { ...DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK },
  summary: {
    totalOrders: 7,
    readyForResultEntryCount: 4,
    inProgressExecutionCount: 4,
    draftOrPreliminaryCount: 1,
    pendingVerificationCount: 1,
    finalizedCount: 1,
    cancelledCount: 0,
    urgentOrStatPendingResultCount: 2,
  },
  availableServicePoints: DIAGNOSTIC_RESULT_ENTRY_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
  items: cloneItems(INITIAL_DIAGNOSTIC_RESULT_ENTRY_ITEMS),
};

export function resetDiagnosticResultEntryMockState(): void {
  mockResultEntryOrdersState = cloneItems(INITIAL_DIAGNOSTIC_RESULT_ENTRY_ITEMS);
}

function computeSummaryStats(
  items: DiagnosticResultEntryItemDto[],
): DiagnosticResultEntrySummaryStatsDto {
  let readyForResultEntryCount = 0;
  let inProgressExecutionCount = 0;
  let draftOrPreliminaryCount = 0;
  let pendingVerificationCount = 0;
  let finalizedCount = 0;
  let cancelledCount = 0;
  let urgentOrStatPendingResultCount = 0;

  for (const item of items) {
    const status = (item.orderStatus || item.status).toUpperCase();
    const resultStatus = (item.resultStatus ?? 'NONE').toUpperCase();

    if (item.canEnterResult) {
      readyForResultEntryCount += 1;
    }
    if (status === 'IN_PROGRESS') {
      inProgressExecutionCount += 1;
      if (!item.isResultFinalized && (item.priority === 'URGENT' || item.priority === 'STAT')) {
        urgentOrStatPendingResultCount += 1;
      }
    }
    if (resultStatus === 'DRAFT' || resultStatus === 'PRELIMINARY') {
      draftOrPreliminaryCount += 1;
    } else if (resultStatus === 'PENDING_VERIFY') {
      pendingVerificationCount += 1;
    } else if (resultStatus === 'FINALIZED' || resultStatus === 'VERIFIED') {
      finalizedCount += 1;
    }
    if (status === 'CANCELLED') {
      cancelledCount += 1;
    }
  }

  return {
    totalOrders: items.length,
    readyForResultEntryCount,
    inProgressExecutionCount,
    draftOrPreliminaryCount,
    pendingVerificationCount,
    finalizedCount,
    cancelledCount,
    urgentOrStatPendingResultCount,
  };
}

function findMockResultEntryOrderByTarget(
  idOrCode: string | number,
): DiagnosticResultEntryItemDto | undefined {
  const raw = String(idOrCode).trim();
  if (!raw) return undefined;
  const upper = raw.toUpperCase();
  const numericId = Number(raw);

  return mockResultEntryOrdersState.find((item) => {
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

export function resolveMockDiagnosticOrdersForResultEntry(
  params?: GetDiagnosticOrdersForResultEntryParams,
): DiagnosticResultEntryListResponseDto {
  const allItems = cloneItems(mockResultEntryOrdersState);
  const summary = computeSummaryStats(allItems);

  const statusFilter = params?.status?.trim().toUpperCase();
  const resultStatusFilter = params?.resultStatus?.trim().toUpperCase();
  const queueStatusFilter = params?.queueStatus?.trim().toUpperCase();
  const categoryFilter = params?.category?.trim().toUpperCase();
  const priorityFilter = params?.priority?.trim().toUpperCase();
  const keyword = params?.keyword?.trim().toLowerCase() ?? '';
  const readyForResultEntryOnly = Boolean(params?.readyForResultEntryOnly);
  const pendingFinalizationOnly = Boolean(params?.pendingFinalizationOnly);

  let filtered = allItems.filter((item) => {
    if (readyForResultEntryOnly && !item.canEnterResult) {
      return false;
    }
    if (
      pendingFinalizationOnly &&
      (item.resultStatus ?? 'NONE').toUpperCase() !== 'PENDING_VERIFY'
    ) {
      return false;
    }
    if (statusFilter && statusFilter !== 'ALL') {
      const orderStatus = (item.orderStatus || item.status).toUpperCase();
      if (orderStatus !== statusFilter) return false;
    }
    if (resultStatusFilter && resultStatusFilter !== 'ALL') {
      const resultStatus = (item.resultStatus ?? 'NONE').toUpperCase();
      if (resultStatus !== resultStatusFilter) return false;
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
    hospitalId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalName,
    departmentId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.departmentId,
    departmentName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.departmentName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK },
    summary,
    availableServicePoints: DIAGNOSTIC_RESULT_ENTRY_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
    items: filtered,
  };
}

function buildStatusHistoryForOrder(
  order: DiagnosticResultEntryItemDto,
): DiagnosticResultHistoryItemDto[] {
  const entries: DiagnosticResultHistoryItemDto[] = [];
  let auditSeq = order.orderId * 1000;

  for (const version of order.versions) {
    auditSeq += 1;
    entries.push({
      auditLogId: auditSeq,
      orderId: order.orderId,
      orderCode: order.orderCode,
      resultId: version.resultId,
      versionNo: version.versionNo,
      action: version.isFinalized ? 'FINALIZE_RESULT' : 'SAVE_RESULT_VERSION',
      resultStatus: version.isFinalized ? 'FINALIZED' : order.resultStatus,
      isFinalized: version.isFinalized,
      resultText: version.resultText,
      conclusion: version.conclusion,
      reportFileUrl: version.reportFileUrl,
      reason: version.correctionReason,
      actorUserId: version.enteredByStaffId,
      actorStaffCode: version.enteredByStaffCode,
      actorStaffName: version.enteredByStaffName,
      occurredAtUtc: version.enteredAt ?? order.startedAt ?? order.orderedAt,
    });
    if (version.isFinalized && version.verifiedAt) {
      auditSeq += 1;
      entries.push({
        auditLogId: auditSeq,
        orderId: order.orderId,
        orderCode: order.orderCode,
        resultId: version.resultId,
        versionNo: version.versionNo,
        action: 'VERIFY_RESULT',
        resultStatus: 'VERIFIED',
        isFinalized: true,
        resultText: version.resultText,
        conclusion: version.conclusion,
        reportFileUrl: version.reportFileUrl,
        reason: null,
        actorUserId: version.verifiedByStaffId,
        actorStaffCode: version.verifiedByStaffCode,
        actorStaffName: version.verifiedByStaffName,
        occurredAtUtc: version.verifiedAt,
      });
    }
  }

  return entries;
}

export function resolveMockDiagnosticResultEntryDetail(
  idOrCode: string,
): DiagnosticResultEntryDetailResponseDto {
  const matched = findMockResultEntryOrderByTarget(idOrCode) ?? mockResultEntryOrdersState[0];
  const orderCopy: DiagnosticResultEntryItemDto = {
    ...matched,
    patient: { ...matched.patient },
    eligibility: { ...matched.eligibility },
    versions: matched.versions.map((v) => ({ ...v })),
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
    hospitalId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.hospitalName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK },
    order: orderCopy,
    clinicalContext: { ...clinicalContext },
    versions: orderCopy.versions.map((v) => ({ ...v })),
    statusHistory: buildStatusHistoryForOrder(orderCopy),
    availableServicePoints: DIAGNOSTIC_RESULT_ENTRY_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
  };
}

let mockResultVersionSeq = 9200;
let mockResultIdSeq = 9100;

export function enterMockDiagnosticResult(
  idOrCode: string,
  payload: EnterDiagnosticResultRequest,
): EnterDiagnosticResultResponseDto {
  const target =
    (payload.orderId ? findMockResultEntryOrderByTarget(payload.orderId) : undefined) ??
    (payload.orderCode ? findMockResultEntryOrderByTarget(payload.orderCode) : undefined) ??
    findMockResultEntryOrderByTarget(idOrCode);

  if (!target) {
    throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${idOrCode}'.`);
  }

  if (target.isResultFinalized || target.eligibility.isResultAlreadyFinalized) {
    throw new Error(
      `Kết quả của phiếu chỉ định '${target.orderCode}' đã được duyệt & trả (FINALIZED). Không thể chỉnh sửa hoặc nhập lại — vui lòng sử dụng quy trình amendment (UC45).`,
    );
  }

  if (!target.canEnterResult) {
    throw new Error(
      target.disabledReason ||
        `Phiếu chỉ định '${target.orderCode}' không đủ điều kiện nhập kết quả cận lâm sàng.`,
    );
  }

  if (
    payload.expectedStateVersion != null &&
    payload.expectedStateVersion !== target.stateVersion
  ) {
    throw new Error(
      `Dữ liệu phiếu chỉ định '${target.orderCode}' đã thay đổi bởi thao tác khác (state_version mismatch: expected ${payload.expectedStateVersion}, current ${target.stateVersion}). Vui lòng tải lại.`,
    );
  }

  const nowIso = new Date().toISOString();
  const previousStateVersion = target.stateVersion;
  const previousOrderStatus = target.orderStatus || target.status;
  const previousQueueStatus = target.queueStatus ?? null;
  const isFinalized = Boolean(payload.isFinalized);
  const isVerified =
    isFinalized && Boolean(payload.isVerified ?? target.requiresResultVerification);
  const requestedStatus = (payload.status ?? '').trim().toUpperCase();
  const resultStatus = isFinalized
    ? isVerified
      ? 'VERIFIED'
      : 'FINALIZED'
    : requestedStatus === 'PRELIMINARY' || requestedStatus === 'PENDING_VERIFY'
      ? requestedStatus
      : 'DRAFT';

  const resultId = target.resultId ?? (mockResultIdSeq += 1);
  const versionNo = (target.latestVersionNo ?? 0) + 1;
  const versionId = (mockResultVersionSeq += 1);

  const newVersion: DiagnosticResultVersionItemDto = {
    versionId,
    resultId,
    versionNo,
    resultText: payload.resultText?.trim() || null,
    conclusion: payload.conclusion?.trim() || null,
    reportFileUrl: payload.reportFileUrl?.trim() || null,
    isFinalized,
    enteredByStaffId: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.userId,
    enteredByStaffCode: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009',
    enteredByStaffName: DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.fullName,
    enteredAt: nowIso,
    verifiedByStaffId: isVerified ? DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.userId : null,
    verifiedByStaffCode: isVerified
      ? (DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009')
      : null,
    verifiedByStaffName: isVerified ? DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK.fullName : null,
    verifiedAt: isVerified ? nowIso : null,
    correctionReason: payload.correctionReason?.trim() || null,
  };

  target.resultId = resultId;
  target.hasResult = true;
  target.isResultReady = true;
  target.resultStatus = resultStatus;
  target.latestVersionNo = versionNo;
  target.resultText = newVersion.resultText;
  target.conclusion = newVersion.conclusion;
  target.reportFileUrl = newVersion.reportFileUrl;
  target.enteredByStaffId = newVersion.enteredByStaffId;
  target.enteredByStaffCode = newVersion.enteredByStaffCode;
  target.enteredByStaffName = newVersion.enteredByStaffName;
  target.enteredAt = nowIso;
  target.correctionReason = newVersion.correctionReason;

  if (isFinalized) {
    target.isResultFinalized = true;
    target.resultFinalizedAt = nowIso;
    target.finalizedByStaffId = newVersion.enteredByStaffId;
    target.finalizedByStaffCode = newVersion.enteredByStaffCode;
    target.finalizedByStaffName = newVersion.enteredByStaffName;
    target.verifiedByStaffId = newVersion.verifiedByStaffId;
    target.verifiedByStaffCode = newVersion.verifiedByStaffCode;
    target.verifiedByStaffName = newVersion.verifiedByStaffName;
    target.verifiedAt = newVersion.verifiedAt;
    target.orderStatus = 'COMPLETED';
    target.status = 'COMPLETED';
    target.completedAt = nowIso;
    target.queueStatus = 'COMPLETED';
    target.canEnterResult = false;
    target.canFinalizeResult = false;
    target.disabledReason = `Kết quả của phiếu chỉ định '${target.orderCode}' đã được duyệt & trả (${resultStatus}). Mọi chỉnh sửa sau duyệt cần thực hiện qua quy trình amendment (UC45).`;
    target.eligibility = {
      ...target.eligibility,
      isEligible: false,
      canEnterResult: false,
      canFinalizeResult: false,
      isResultAlreadyFinalized: true,
      disabledReason: target.disabledReason,
    };
  }

  target.stateVersion = previousStateVersion + 1;
  target.versions = [...target.versions, newVersion];

  const updatedOrder: DiagnosticResultEntryItemDto = {
    ...target,
    patient: { ...target.patient },
    eligibility: { ...target.eligibility },
    versions: target.versions.map((v) => ({ ...v })),
  };

  return {
    actionOutcome: isFinalized ? 'RESULT_FINALIZED' : 'RESULT_SAVED',
    message: isFinalized
      ? `Kết quả phiếu '${target.orderCode}' đã được duyệt & trả (${resultStatus}).`
      : `Đã lưu kết quả phiên bản v${versionNo} (${resultStatus}) cho phiếu '${target.orderCode}'.`,
    orderId: target.orderId,
    orderCode: target.orderCode,
    resultId,
    versionNo,
    resultStatus,
    isFinalized,
    previousOrderStatus,
    orderStatus: target.orderStatus,
    previousQueueStatus,
    queueStatus: target.queueStatus ?? null,
    previousStateVersion,
    stateVersion: target.stateVersion,
    enteredAt: nowIso,
    finalizedAt: isFinalized ? nowIso : null,
    enteredByStaff: { ...DIAGNOSTIC_RESULT_ENTRY_STAFF_PROFILE_MOCK },
    order: updatedOrder,
    latestVersion: { ...newVersion },
    versions: updatedOrder.versions.map((v) => ({ ...v })),
  };
}
