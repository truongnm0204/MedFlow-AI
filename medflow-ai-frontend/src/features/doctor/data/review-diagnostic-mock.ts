import type { ConsultationWorkspaceResponseDto } from '../types/consultation-workspace.types';
import type {
  ConsultationDiagnosticResultItemDto,
  DiagnosticResultsReviewResponseDto,
  ReviewDiagnosticResultsRequest,
} from '../types/review-diagnostic.types';

import { resolveMockConsultationWorkspace } from './consultation-workspace-mock';

export const REVIEW_DIAGNOSTIC_STEPS = [
  'Tiếp nhận & Sinh hiệu',
  'Khám lâm sàng',
  'Chỉ định CLS',
  'Kết quả CLS trả về (SCR-DIA-02)',
  'Chẩn đoán & Kết luận',
] as const;

export const REVIEW_DIAGNOSTIC_ACTIVE_STEP = 4;

const ENDO_CONCLUSION =
  'Viêm teo niêm mạc hang vị mức độ vừa, kèm loét hành tá tràng Forrest III. Clo-Test dương tính.';

const ENDO_RESULT_TEXT = [
  'Thực quản: niêm mạc hồng, không giãn tĩnh mạch, tâm vị đóng kín.',
  'Dạ dày: niêm mạc thân vị phù nề nhẹ; hang vị sung huyết, teo niêm mạc từng đám.',
  'Hành tá tràng: ổ loét đường kính khoảng 5mm, đáy phủ giả mạc trắng, không chảy máu.',
  'Clo-Test HP: dương tính sau 15 phút.',
].join('\n');

const US_CONCLUSION = 'Gan nhiễm mỡ độ I. Túi mật, tụy, lách, hai thận chưa ghi nhận bất thường.';

const US_RESULT_TEXT = [
  'Gan: kích thước bình thường, echo dày nhẹ lan toả, bờ đều.',
  'Túi mật: thành mỏng, không thấy sỏi. Đường mật trong và ngoài gan không giãn.',
  'Tụy, lách, hai thận: kích thước và cấu trúc echo trong giới hạn bình thường.',
  'Doppler tĩnh mạch cửa: dòng chảy hướng gan, vận tốc bình thường.',
].join('\n');

function buildMockResultItem(
  overrides: Partial<ConsultationDiagnosticResultItemDto>,
): ConsultationDiagnosticResultItemDto {
  return {
    orderId: 0,
    orderCode: '',
    visitId: 108,
    visitCode: 'VIS-2026-0415-0108',
    orderingDoctorId: 3,
    orderingDoctorCode: 'DOC-003',
    orderingDoctorName: 'BS. CKII Trần Quốc Tuấn',
    serviceId: 0,
    serviceCode: '',
    serviceName: '',
    category: 'IMAGING',
    departmentId: null,
    departmentCode: null,
    departmentName: 'Khoa Nội Tổng Quát',
    specialtyId: null,
    specialtyCode: null,
    specialtyName: 'Nội tổng quát',
    unitPrice: 0,
    requiresResultVerification: true,
    priority: 'NORMAL',
    orderStatus: 'ORDERED',
    requiredForConclusion: false,
    instruction: null,
    orderedAt: '2026-04-15T07:30:00',
    confirmedAt: null,
    confirmedByStaffId: null,
    confirmedByStaffCode: null,
    confirmedByStaffName: null,
    queueEntryId: null,
    formattedQueueEntryId: null,
    ticketNo: null,
    queueNumber: null,
    queueStatus: null,
    servicePointId: null,
    servicePointName: null,
    roomId: null,
    roomCode: null,
    roomName: null,
    roomLocation: null,
    completedAt: null,
    hasResult: false,
    isResultReady: false,
    isResultFinalized: false,
    resultId: null,
    resultStatus: 'PENDING',
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
    versions: [],
    ...overrides,
  };
}

export const REVIEW_DIAGNOSTIC_RESULTS_MOCK: DiagnosticResultsReviewResponseDto = {
  visitId: 108,
  visitCode: 'VIS-2026-0415-0108',
  visitStatus: 'IN_PROGRESS',
  visitStatusLabel: 'Đang khám',
  workspaceState: 'IN_PROGRESS',
  canViewDiagnosticResults: true,
  canAcknowledgeDiagnosticResults: true,
  disabledReason: null,
  generatedAtUtc: '2026-04-15T03:00:00Z',
  patient: {
    patientId: 108,
    patientCode: 'PAT-2024-0108',
    fullName: 'NGUYỄN VĂN AN',
    dateOfBirth: null,
    birthYear: null,
    age: null,
    gender: null,
    genderLabel: 'Nam',
    phone: null,
    email: null,
    address: null,
    bloodType: null,
    allergiesSummary: 'Penicillin, NSAIDs',
  },
  diagnosticSummary: {
    totalQueueEntries: 3,
    openQueueEntries: 1,
    completedQueueEntries: 2,
    allMandatoryResultsReady: true,
    finalResultRequired: true,
  },
  totalOrdersCount: 3,
  readyResultsCount: 2,
  pendingResultsCount: 1,
  mandatoryOrdersCount: 1,
  mandatoryReadyCount: 1,
  mandatoryPendingCount: 0,
  items: [
    buildMockResultItem({
      orderId: 9101,
      orderCode: 'DIA-202604-9101',
      serviceId: 9101,
      serviceCode: 'CLS-ENDO-01',
      serviceName: 'Nội soi thực quản - dạ dày - tá tràng ống mềm có tiền mê (kèm Clo-Test HP)',
      category: 'IMAGING',
      orderStatus: 'COMPLETED',
      requiredForConclusion: true,
      hasResult: true,
      isResultReady: true,
      isResultFinalized: true,
      resultId: 9101,
      resultStatus: 'VERIFIED',
      resultFinalizedAt: '2026-04-15T09:42:00',
      latestVersionNo: 1,
      resultText: ENDO_RESULT_TEXT,
      conclusion: ENDO_CONCLUSION,
      enteredByStaffName: 'BS.CKI Hoàng Minh Hải',
      enteredAt: '2026-04-15T09:42:00',
      verifiedByStaffName: 'BS.CKI Hoàng Minh Hải',
      verifiedAt: '2026-04-15T09:42:00',
      versions: [
        {
          versionId: 1,
          resultId: 9101,
          versionNo: 1,
          resultText: ENDO_RESULT_TEXT,
          conclusion: ENDO_CONCLUSION,
          reportFileUrl: null,
          isFinalized: true,
          enteredByStaffId: null,
          enteredByStaffCode: null,
          enteredByStaffName: 'BS.CKI Hoàng Minh Hải',
          enteredAt: '2026-04-15T09:42:00',
          verifiedByStaffId: null,
          verifiedByStaffCode: null,
          verifiedByStaffName: 'BS.CKI Hoàng Minh Hải',
          verifiedAt: '2026-04-15T09:42:00',
          correctionReason: null,
        },
      ],
    }),
    buildMockResultItem({
      orderId: 9102,
      orderCode: 'DIA-202604-9102',
      serviceId: 9102,
      serviceCode: 'CLS-US-02',
      serviceName: 'Siêu âm ổ bụng tổng quát màu Doppler (4D)',
      category: 'IMAGING',
      orderStatus: 'COMPLETED',
      hasResult: true,
      isResultReady: true,
      isResultFinalized: true,
      resultId: 9102,
      resultStatus: 'FINALIZED',
      resultFinalizedAt: '2026-04-15T08:55:00',
      resultText: US_RESULT_TEXT,
      conclusion: US_CONCLUSION,
      enteredByStaffName: 'ThS.BS Nguyễn Thu Trang',
      enteredAt: '2026-04-15T08:55:00',
    }),
    buildMockResultItem({
      orderId: 9103,
      orderCode: 'DIA-202604-9103',
      serviceId: 9103,
      serviceCode: 'CLS-LAB-01',
      serviceName: 'Tổng phân tích tế bào máu ngoại vi bằng laser (CBC 24 thông số)',
      category: 'LABORATORY',
      orderStatus: 'IN_PROGRESS',
      hasResult: false,
      isResultReady: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
      queueStatus: 'IN_PROGRESS',
      roomName: 'Phòng Xét nghiệm 01',
    }),
  ],
};

export function resolveMockDiagnosticResultsReview(
  idOrCode: string,
): DiagnosticResultsReviewResponseDto {
  const visitCode = idOrCode.trim() || REVIEW_DIAGNOSTIC_RESULTS_MOCK.visitCode;
  return {
    ...REVIEW_DIAGNOSTIC_RESULTS_MOCK,
    visitCode,
    patient: { ...REVIEW_DIAGNOSTIC_RESULTS_MOCK.patient },
    diagnosticSummary: { ...REVIEW_DIAGNOSTIC_RESULTS_MOCK.diagnosticSummary },
    items: REVIEW_DIAGNOSTIC_RESULTS_MOCK.items.map((item) => ({
      ...item,
      visitCode,
      versions: item.versions.map((version) => ({ ...version })),
    })),
  };
}

export function reviewMockDiagnosticResults(
  idOrCode: string,
  _request: ReviewDiagnosticResultsRequest,
): ConsultationWorkspaceResponseDto {
  return {
    ...resolveMockConsultationWorkspace(idOrCode),
    actionOutcome: 'DIAGNOSTIC_RESULTS_REVIEWED',
  };
}
