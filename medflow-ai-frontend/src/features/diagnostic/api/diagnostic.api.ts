import { apiClient } from '@/shared/lib/axios';

import { diagnosticOrderIdentifierSchema } from '../schemas/diagnostic-worklist-filter.schema';
import type {
  CallNextDiagnosticPatientRequest,
  CallNextDiagnosticPatientResponseDto,
  CancelDiagnosticQueueEntryRequest,
  ConfirmDiagnosticOrderRequest,
  ConfirmDiagnosticOrderResponseDto,
  ConfirmDiagnosticOrdersBatchRequest,
  DiagnosticDashboardQueryParams,
  DiagnosticDashboardSummary,
  DiagnosticOrderReviewDetailResponseDto,
  DiagnosticOrderReviewListResponseDto,
  DiagnosticPatientReceiveDetailResponseDto,
  DiagnosticPatientReceiveListResponseDto,
  DiagnosticQueueBoardResponseDto,
  DiagnosticQueueEntryDetailResponseDto,
  DiagnosticQueueTurnActionRequest,
  DiagnosticResultEntryDetailResponseDto,
  DiagnosticResultEntryListResponseDto,
  DiagnosticServiceExecutionDetailResponseDto,
  DiagnosticServiceExecutionListResponseDto,
  EnterDiagnosticResultRequest,
  EnterDiagnosticResultResponseDto,
  GetDiagnosticExecutionsParams,
  GetDiagnosticOrdersForReceiveParams,
  GetDiagnosticOrdersForResultEntryParams,
  GetDiagnosticOrdersForReviewParams,
  GetDiagnosticQueueParams,
  ManageDiagnosticQueueActionResponseDto,
  ReceiveDiagnosticPatientRequest,
  ReceiveDiagnosticPatientResponseDto,
  ReceiveDiagnosticPatientsBatchRequest,
  UpdateDiagnosticQueueStatusRequest,
  UpdateDiagnosticResultRequest,
  UpdateDiagnosticServiceStatusBatchRequest,
  UpdateDiagnosticServiceStatusRequest,
  UpdateDiagnosticServiceStatusResponseDto,
} from '../types/diagnostic.types';
import type {
  DiagnosticWorklistItemDto,
  DiagnosticWorklistQueryParams,
  DiagnosticWorklistResponseDto,
} from '../types/diagnostic-worklist.types';

/**
 * Lấy dữ liệu tổng quan màn hình khởi đầu của Diagnostic Staff (`GET /diagnostic/dashboard/summary`).
 */
export async function fetchDiagnosticDashboardSummary(
  params?: DiagnosticDashboardQueryParams,
): Promise<DiagnosticDashboardSummary> {
  const { data } = await apiClient.get<DiagnosticDashboardSummary>(
    '/diagnostic/dashboard/summary',
    params ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy danh sách công việc cận lâm sàng (`GET /diagnostic/worklist`) cho UC38.
 */
export async function fetchDiagnosticWorklist(
  params?: DiagnosticWorklistQueryParams,
): Promise<DiagnosticWorklistResponseDto> {
  const hasParams = Boolean(params && Object.keys(params).length > 0);
  const { data } = await apiClient.get<DiagnosticWorklistResponseDto>(
    '/diagnostic/worklist',
    hasParams ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết 1 phiếu chỉ định cận lâm sàng trong danh sách công việc (`GET /diagnostic/worklist/{orderIdOrCode}`).
 */
export async function fetchDiagnosticWorklistOrderDetail(
  orderIdOrCode: string,
): Promise<DiagnosticWorklistItemDto> {
  const parsed = diagnosticOrderIdentifierSchema.safeParse(orderIdOrCode);
  if (!parsed.success) {
    throw new Error(
      parsed.error.issues[0]?.message ??
        'Mã hoặc định danh phiếu chỉ định cận lâm sàng không hợp lệ.',
    );
  }

  const normalizedId = parsed.data;
  const { data } = await apiClient.get<DiagnosticWorklistItemDto>(
    `/diagnostic/worklist/${encodeURIComponent(normalizedId)}`,
  );
  return data;
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng chờ kiểm tra & xác nhận (`GET /diagnostic/orders/review`).
 */
export async function fetchDiagnosticOrdersForReview(
  params?: GetDiagnosticOrdersForReviewParams,
): Promise<DiagnosticOrderReviewListResponseDto> {
  const { data } = await apiClient.get<DiagnosticOrderReviewListResponseDto>(
    '/diagnostic/orders/review',
    params ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & checklist đủ điều kiện (`GET /diagnostic/orders/{idOrCode}/review`).
 */
export async function fetchDiagnosticOrderReviewDetail(
  idOrCode: string,
): Promise<DiagnosticOrderReviewDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticOrderReviewDetailResponseDto>(
    `/diagnostic/orders/${encoded}/review`,
  );
  return data;
}

/**
 * Xác nhận một phiếu chỉ định CLS hợp lệ và xếp vào hàng đợi (`POST /diagnostic/orders/{idOrCode}/confirm`).
 */
export async function confirmDiagnosticOrder(
  idOrCode: string,
  payload?: ConfirmDiagnosticOrderRequest,
): Promise<ConfirmDiagnosticOrderResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ConfirmDiagnosticOrderResponseDto>(
    `/diagnostic/orders/${encoded}/confirm`,
    payload ?? { isEligibilityVerified: true },
  );
  return data;
}

/**
 * Xác nhận hàng loạt nhiều phiếu chỉ định CLS hợp lệ (`POST /diagnostic/orders/confirm`).
 */
export async function confirmDiagnosticOrdersBatch(
  payload: ConfirmDiagnosticOrdersBatchRequest,
): Promise<ConfirmDiagnosticOrderResponseDto> {
  const { data } = await apiClient.post<ConfirmDiagnosticOrderResponseDto>(
    '/diagnostic/orders/confirm',
    payload,
  );
  return data;
}

/**
 * Lấy bảng hàng đợi cận lâm sàng theo điểm phục vụ & phòng kỹ thuật (`GET /diagnostic/queue`) cho UC41.
 */
export async function fetchDiagnosticQueueBoard(
  params?: GetDiagnosticQueueParams,
): Promise<DiagnosticQueueBoardResponseDto> {
  const hasParams = Boolean(params && Object.keys(params).length > 0);
  const { data } = await apiClient.get<DiagnosticQueueBoardResponseDto>(
    '/diagnostic/queue',
    hasParams ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết một lượt xếp hàng cận lâm sàng & lịch sử chuyển trạng thái (`GET /diagnostic/queue/{idOrCode}`).
 */
export async function fetchDiagnosticQueueEntryDetail(
  idOrCode: string,
): Promise<DiagnosticQueueEntryDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticQueueEntryDetailResponseDto>(
    `/diagnostic/queue/${encoded}`,
  );
  return data;
}

/**
 * Gọi bệnh nhân đủ điều kiện tiếp theo tại điểm phục vụ cận lâm sàng (`POST /diagnostic/queue/service-points/{servicePointId}/call-next`).
 */
export async function callNextDiagnosticPatient(
  servicePointId: string,
  payload?: CallNextDiagnosticPatientRequest,
): Promise<CallNextDiagnosticPatientResponseDto> {
  const encoded = encodeURIComponent(servicePointId.trim());
  const { data } = await apiClient.post<CallNextDiagnosticPatientResponseDto>(
    `/diagnostic/queue/service-points/${encoded}/call-next`,
    payload ?? {},
  );
  return data;
}

/**
 * Cập nhật trạng thái lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/status`).
 */
export async function updateDiagnosticQueueStatus(
  idOrCode: string,
  payload: UpdateDiagnosticQueueStatusRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/status`,
    payload,
  );
  return data;
}

/**
 * Đánh dấu bệnh nhân sẵn sàng trong hàng đợi cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/ready`).
 */
export async function markDiagnosticQueueReady(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/ready`,
    payload ?? {},
  );
  return data;
}

/**
 * Gọi trực tiếp một lượt xếp hàng cận lâm sàng cụ thể (`POST /diagnostic/queue/{idOrCode}/call`).
 */
export async function callDiagnosticQueueEntry(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/call`,
    payload ?? {},
  );
  return data;
}

/**
 * Bắt đầu thực hiện kỹ thuật cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/start`).
 */
export async function startDiagnosticQueueExecution(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/start`,
    payload ?? {},
  );
  return data;
}

/**
 * Hoàn tất thực hiện lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/complete`).
 */
export async function completeDiagnosticQueueExecution(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/complete`,
    payload ?? {},
  );
  return data;
}

/**
 * Đánh dấu bệnh nhân vắng mặt / qua lượt (`POST /diagnostic/queue/{idOrCode}/missed-turn`).
 */
export async function markDiagnosticQueueMissedTurn(
  idOrCode: string,
  payload: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/missed-turn`,
    payload,
  );
  return data;
}

/**
 * Khôi phục lượt chờ cho bệnh nhân qua lượt đã quay lại (`POST /diagnostic/queue/{idOrCode}/restore-turn`).
 */
export async function restoreDiagnosticQueueTurn(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/restore-turn`,
    payload ?? {},
  );
  return data;
}

/**
 * Hủy lượt xếp hàng cận lâm sàng (`POST /diagnostic/queue/{idOrCode}/cancel`).
 */
export async function cancelDiagnosticQueueEntry(
  idOrCode: string,
  payload: CancelDiagnosticQueueEntryRequest,
): Promise<ManageDiagnosticQueueActionResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ManageDiagnosticQueueActionResponseDto>(
    `/diagnostic/queue/${encoded}/cancel`,
    payload,
  );
  return data;
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng chờ tiếp nhận bệnh nhân tại điểm dịch vụ (`GET /diagnostic/orders/receive` — UC40).
 */
export async function fetchDiagnosticOrdersForReceive(
  params?: GetDiagnosticOrdersForReceiveParams,
): Promise<DiagnosticPatientReceiveListResponseDto> {
  const { data } = await apiClient.get<DiagnosticPatientReceiveListResponseDto>(
    '/diagnostic/orders/receive',
    params ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & checklist tiếp nhận bệnh nhân (`GET /diagnostic/orders/{idOrCode}/receive` — UC40).
 */
export async function fetchDiagnosticPatientReceiveDetail(
  idOrCode: string,
): Promise<DiagnosticPatientReceiveDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticPatientReceiveDetailResponseDto>(
    `/diagnostic/orders/${encoded}/receive`,
  );
  return data;
}

/**
 * Tiếp nhận bệnh nhân tại điểm dịch vụ cận lâm sàng, chuyển trạng thái hàng đợi sang READY (`POST /diagnostic/orders/{idOrCode}/receive` — UC40).
 */
export async function receiveDiagnosticPatient(
  idOrCode: string,
  payload?: ReceiveDiagnosticPatientRequest,
): Promise<ReceiveDiagnosticPatientResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<ReceiveDiagnosticPatientResponseDto>(
    `/diagnostic/orders/${encoded}/receive`,
    payload ?? {
      isPatientPresent: true,
      isIdentityVerified: true,
      isPreparationVerified: true,
    },
  );
  return data;
}

/**
 * Tiếp nhận bệnh nhân hàng loạt cho các phiếu chỉ định cận lâm sàng đã xác nhận (`POST /diagnostic/orders/receive` — UC40).
 */
export async function receiveDiagnosticPatientsBatch(
  payload: ReceiveDiagnosticPatientsBatchRequest,
): Promise<ReceiveDiagnosticPatientResponseDto> {
  const { data } = await apiClient.post<ReceiveDiagnosticPatientResponseDto>(
    '/diagnostic/orders/receive',
    payload,
  );
  return data;
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng theo trạng thái thực hiện kỹ thuật (`GET /diagnostic/orders/execution` — UC42).
 */
export async function fetchDiagnosticExecutions(
  params?: GetDiagnosticExecutionsParams,
): Promise<DiagnosticServiceExecutionListResponseDto> {
  const { data } = await apiClient.get<DiagnosticServiceExecutionListResponseDto>(
    '/diagnostic/orders/execution',
    params ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng & lịch sử chuyển trạng thái thực hiện (`GET /diagnostic/orders/{idOrCode}/execution` — UC42).
 */
export async function fetchDiagnosticExecutionDetail(
  idOrCode: string,
): Promise<DiagnosticServiceExecutionDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticServiceExecutionDetailResponseDto>(
    `/diagnostic/orders/${encoded}/execution`,
  );
  return data;
}

/**
 * Cập nhật trạng thái thực hiện kỹ thuật đơn lẻ (`POST /diagnostic/orders/{idOrCode}/execution-status` — UC42).
 */
export async function updateDiagnosticServiceStatus(
  idOrCode: string,
  payload: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
    `/diagnostic/orders/${encoded}/execution-status`,
    payload,
  );
  return data;
}

/**
 * Bắt đầu thực hiện kỹ thuật (`POST /diagnostic/orders/{idOrCode}/start` — UC42).
 */
export async function startDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
    `/diagnostic/orders/${encoded}/start`,
    payload ?? { targetStatus: 'IN_PROGRESS' },
  );
  return data;
}

/**
 * Hoàn tất thực hiện kỹ thuật (`POST /diagnostic/orders/{idOrCode}/complete` — UC42).
 */
export async function completeDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
    `/diagnostic/orders/${encoded}/complete`,
    payload ?? { targetStatus: 'COMPLETED' },
  );
  return data;
}

/**
 * Cập nhật trạng thái thực hiện kỹ thuật hàng loạt (`POST /diagnostic/orders/execution-status` — UC42).
 */
export async function updateDiagnosticServiceStatusBatch(
  payload: UpdateDiagnosticServiceStatusBatchRequest,
): Promise<UpdateDiagnosticServiceStatusResponseDto> {
  const { data } = await apiClient.post<UpdateDiagnosticServiceStatusResponseDto>(
    '/diagnostic/orders/execution-status',
    payload,
  );
  return data;
}

/**
 * Lấy danh sách phiếu chỉ định cận lâm sàng cần nhập / trả kết quả (`GET /diagnostic/results` — UC43).
 */
export async function fetchDiagnosticOrdersForResultEntry(
  params?: GetDiagnosticOrdersForResultEntryParams,
): Promise<DiagnosticResultEntryListResponseDto> {
  const { data } = await apiClient.get<DiagnosticResultEntryListResponseDto>(
    '/diagnostic/results',
    params ? { params } : undefined,
  );
  return data;
}

/**
 * Lấy chi tiết phiếu chỉ định CLS kèm bối cảnh lâm sàng, lịch sử phiên bản kết quả & checklist điều kiện nhập kết quả (`GET /diagnostic/results/{idOrCode}` — UC43).
 */
export async function fetchDiagnosticResultEntryDetail(
  idOrCode: string,
): Promise<DiagnosticResultEntryDetailResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.get<DiagnosticResultEntryDetailResponseDto>(
    `/diagnostic/results/${encoded}`,
  );
  return data;
}

/**
 * Nhân viên cận lâm sàng lưu / duyệt & trả kết quả cho phiếu chỉ định (`POST /diagnostic/orders/{idOrCode}/result` — UC43).
 */
export async function enterDiagnosticResult(
  idOrCode: string,
  payload: EnterDiagnosticResultRequest,
): Promise<EnterDiagnosticResultResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<EnterDiagnosticResultResponseDto>(
    `/diagnostic/orders/${encoded}/result`,
    payload,
  );
  return data;
}

/**
 * Sửa kết quả cận lâm sàng đã khóa (`POST /diagnostic/orders/{idOrCode}/result/amend` — UC43).
 */
export async function updateDiagnosticResult(
  idOrCode: string,
  payload: UpdateDiagnosticResultRequest,
): Promise<EnterDiagnosticResultResponseDto> {
  const encoded = encodeURIComponent(idOrCode.trim());
  const { data } = await apiClient.post<EnterDiagnosticResultResponseDto>(
    `/diagnostic/orders/${encoded}/result/amend`,
    payload,
  );
  return data;
}
