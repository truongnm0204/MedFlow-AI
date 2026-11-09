import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { CancelQueueEntryModal } from '../components/cancel-queue-entry-modal';
import type {
  ExaminationQueueEntryDto,
  ServicePointQueueDto,
} from '../types/examination-queue.types';
import type {
  CancelQueueEntryResponseDto,
  CancelQueueEntryTarget,
} from '../types/queue-status-update.types';

const SERVICE_POINT: ServicePointQueueDto = {
  servicePointId: 'sp-room-103',
  servicePointName: 'Điểm khám Tim mạch 1',
  roomCode: 'P.103',
  roomName: 'Phòng khám Tim mạch 1',
  departmentName: 'Khoa Tim mạch',
  doctorName: 'BS.CKI Lê Hoàng Nam',
  isActive: true,
  currentServingTicketNo: 'B005',
  nextTicketNo: 'B006',
  waitingCount: 1,
  inProgressCount: 1,
  completedCount: 0,
  skippedCount: 0,
  entries: [],
};

const WAITING_ENTRY: ExaminationQueueEntryDto = {
  id: 'q-006',
  queueNumber: 6,
  ticketNo: 'B006',
  servingOrder: 2,
  visitCode: 'VIS-2026-0106',
  patientName: 'Vũ Thị Phương',
  patientCode: 'BN-2024-0061',
  visitType: 'WALK_IN',
  servicePointId: 'sp-room-103',
  roomCode: 'P.103',
  roomName: 'Phòng khám Tim mạch 1',
  departmentName: 'Khoa Tim mạch',
  doctorName: 'BS.CKI Lê Hoàng Nam',
  status: 'WAITING',
  priority: 'NORMAL',
  queueType: 'EXAMINATION',
  presenceStatus: 'PRESENT',
  arrivalClass: 'WALK_IN',
  scheduledAt: null,
  checkedInAt: '08:20',
  calledAt: null,
  startedAt: null,
  blockedReason: null,
};

const TARGET: CancelQueueEntryTarget = { entry: WAITING_ENTRY, servicePoint: SERVICE_POINT };
const STATUS_URL = '/reception/examination-queue/entries/q-006/status';
const CANCEL_URL = '/reception/examination-queue/entries/q-006/cancel';
const REASON = 'Bệnh nhân xin về, không tiếp tục khám';

function mockStatusDetail(stateVersions: number[]) {
  let call = 0;
  return vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url !== STATUS_URL) throw new Error(`Unexpected GET ${url}`);
    const stateVersion = stateVersions[Math.min(call, stateVersions.length - 1)];
    call += 1;
    return {
      data: {
        queueEntryId: 6,
        currentStatus: 'WAITING',
        stateVersion,
        allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
        statusHistory: [],
      },
    };
  });
}

function httpError(status: number, detail: string) {
  return new AxiosError(
    `Request failed with status code ${status}`,
    'ERR_BAD_REQUEST',
    undefined,
    undefined,
    {
      status,
      statusText: String(status),
      headers: {},
      config: { headers: {} } as InternalAxiosRequestConfig,
      data: { title: 'Error', status, detail },
    },
  );
}

function renderModal() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onClose = vi.fn();
  const onCancelled = vi.fn();
  render(
    <QueryClientProvider client={qc}>
      <CancelQueueEntryModal open target={TARGET} onClose={onClose} onCancelled={onCancelled} />
    </QueryClientProvider>,
  );
  return { onClose, onCancelled };
}

async function waitForSubmitEnabled() {
  await waitFor(() => {
    expect(screen.getByRole('button', { name: 'Xác nhận hủy lượt' })).not.toBeDisabled();
  });
}

function typeReason(value: string) {
  fireEvent.change(screen.getByLabelText('Lý do hủy lượt (bắt buộc)'), { target: { value } });
}

describe('CancelQueueEntryModal (UC28 - Issue #103)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps submit disabled until the current stateVersion is loaded', async () => {
    let resolveDetail: (value: unknown) => void = () => {};
    vi.spyOn(apiClient, 'get').mockImplementation(
      () => new Promise((resolve) => (resolveDetail = resolve)),
    );

    renderModal();

    expect(screen.getByRole('button', { name: 'Xác nhận hủy lượt' })).toBeDisabled();

    resolveDetail({ data: { queueEntryId: 6, currentStatus: 'WAITING', stateVersion: 3 } });
    await waitForSubmitEnabled();
  });

  it.each([
    ['', 'Lý do hủy lượt phải từ 5 đến 500 ký tự.'],
    ['   abc   ', 'Lý do hủy lượt phải từ 5 đến 500 ký tự.'],
    ['Bệnh nhân <script> về', 'Lý do hủy lượt không được chứa ký tự < hoặc >.'],
  ])('rejects invalid reason %j without calling the API', async (reason, message) => {
    mockStatusDetail([7]);
    const postSpy = vi.spyOn(apiClient, 'post');
    renderModal();
    await waitForSubmitEnabled();

    typeReason(reason);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lượt' }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('posts trimmed reason with expectedStateVersion and null notes, then reports the result', async () => {
    mockStatusDetail([7]);
    const response = {
      queueEntryId: 6,
      ticketNo: 'B006',
      queueNumber: 6,
      currentStatus: 'CANCELLED',
      allowedTargetStatuses: [],
      previousStateVersion: 7,
      stateVersion: 8,
    } as unknown as CancelQueueEntryResponseDto;
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: response });
    const { onCancelled } = renderModal();
    await waitForSubmitEnabled();

    typeReason(`  ${REASON}  `);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lượt' }));

    await waitFor(() => {
      expect(onCancelled).toHaveBeenCalledWith(response);
    });
    expect(postSpy).toHaveBeenCalledWith(CANCEL_URL, {
      reason: REASON,
      expectedStateVersion: 7,
      notes: null,
    });
  });

  it('shows the backend 409 message, stays open, and resubmits with the refreshed stateVersion', async () => {
    const getSpy = mockStatusDetail([7, 9]);
    const postSpy = vi
      .spyOn(apiClient, 'post')
      .mockRejectedValueOnce(
        httpError(409, 'Dữ liệu hàng đợi đã thay đổi bởi thao tác khác. Vui lòng tải lại.'),
      )
      .mockResolvedValueOnce({ data: { ticketNo: 'B006', queueNumber: 6 } });
    const { onCancelled } = renderModal();
    await waitForSubmitEnabled();

    typeReason(REASON);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lượt' }));

    expect(
      await screen.findByText('Dữ liệu hàng đợi đã thay đổi bởi thao tác khác. Vui lòng tải lại.'),
    ).toBeInTheDocument();
    expect(onCancelled).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledTimes(2);
    });

    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lượt' }));

    await waitFor(() => {
      expect(onCancelled).toHaveBeenCalledTimes(1);
    });
    expect(postSpy).toHaveBeenLastCalledWith(CANCEL_URL, {
      reason: REASON,
      expectedStateVersion: 9,
      notes: null,
    });
  });

  it('shows the backend 403 message and does not report success', async () => {
    mockStatusDetail([7]);
    vi.spyOn(apiClient, 'post').mockRejectedValueOnce(
      httpError(403, 'Bạn không có quyền hủy lượt xếp hàng tại bệnh viện này.'),
    );
    const { onCancelled } = renderModal();
    await waitForSubmitEnabled();

    typeReason(REASON);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lượt' }));

    expect(
      await screen.findByText('Bạn không có quyền hủy lượt xếp hàng tại bệnh viện này.'),
    ).toBeInTheDocument();
    expect(onCancelled).not.toHaveBeenCalled();
  });
});
