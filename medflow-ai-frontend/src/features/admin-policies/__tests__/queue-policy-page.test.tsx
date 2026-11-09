import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import * as queuePolicyApi from '../api/queue-policy.api';
import { DEFAULT_QUEUE_POLICY_VALUES } from '../data/queue-policy-defaults';
import { QueuePolicyPage } from '../pages/queue-policy-page';
import { queuePolicySchema } from '../schemas/queue-policy.schema';
import type { QueuePolicyDto } from '../types/queue-policy.types';

function makeAxiosError(status: number, detail: string): AxiosError {
  const config = { headers: {} } as InternalAxiosRequestConfig;
  const response: AxiosResponse = {
    data: { detail },
    status,
    statusText: String(status),
    headers: {},
    config,
  };
  return new AxiosError(detail, `ERR_BAD_REQUEST_${status}`, config, {}, response);
}

const LOADED_QUEUE_POLICY_DTO: QueuePolicyDto = {
  graceMinutes: 10,
  expectedDurationMinutes: 25,
  safetyBufferMinutes: 5,
  walkinPriorityRatio: { priority: 2, normal: 1 },
  publicNumberImmutable: true,
  manualPriorityRequiresVerification: false,
  lastUpdatedAt: '2026-11-01T03:00:00Z',
};

function renderQueuePolicyPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/queue-policy']}>
        <QueuePolicyPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('queuePolicySchema (UC55)', () => {
  it('accepts default queue policy values and rejects out-of-range values', () => {
    expect(queuePolicySchema.safeParse(DEFAULT_QUEUE_POLICY_VALUES).success).toBe(true);

    expect(
      queuePolicySchema.safeParse({
        ...DEFAULT_QUEUE_POLICY_VALUES,
        graceMinutes: 61,
      }).success,
    ).toBe(false);

    expect(
      queuePolicySchema.safeParse({
        ...DEFAULT_QUEUE_POLICY_VALUES,
        expectedDurationMinutes: 4,
      }).success,
    ).toBe(false);

    expect(
      queuePolicySchema.safeParse({
        ...DEFAULT_QUEUE_POLICY_VALUES,
        walkinPriorityRatio: { priority: 3, normal: 0 },
      }).success,
    ).toBe(false);
  });
});

describe('QueuePolicyPage (SCR-ADM-POL-02)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders loaded DTO values, switch states, and live summary card with warning when verification is disabled', async () => {
    vi.spyOn(queuePolicyApi, 'fetchQueuePolicyApi').mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    renderQueuePolicyPage();

    const graceInput = (await screen.findByLabelText(
      /Thời gian ân hạn đúng giờ/i,
    )) as HTMLInputElement;
    await waitFor(() => {
      expect(graceInput.value).toBe('10');
    });

    const verificationSwitch = screen.getByRole('switch', {
      name: /Bắt buộc xác minh/i,
    }) as HTMLInputElement;
    expect(verificationSwitch.checked).toBe(false);

    const summaryCard = screen.getByTestId('queue-policy-summary-card');
    expect(summaryCard).toHaveTextContent(
      'Chỉ chèn bệnh nhân đến sớm khi còn ≥ 30 phút (25 phút khám + 5 phút dự phòng)',
    );
    expect(summaryCard).toHaveTextContent(
      'Cảnh báo: nhân viên có thể gán ưu tiên không cần xác minh.',
    );
  });

  it('updates live summary Safe Backfill threshold when safety buffer changes', async () => {
    vi.spyOn(queuePolicyApi, 'fetchQueuePolicyApi').mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    renderQueuePolicyPage();

    const bufferInput = await screen.findByLabelText(/Thời gian dự phòng/i);
    await waitFor(() => {
      expect((bufferInput as HTMLInputElement).value).toBe('5');
    });

    fireEvent.change(bufferInput, { target: { value: '10' } });

    await waitFor(() => {
      expect(screen.getByTestId('queue-policy-summary-card')).toHaveTextContent('≥ 35 phút');
    });
  });

  it('blocks submission and shows validation error when graceMinutes is 61', async () => {
    vi.spyOn(queuePolicyApi, 'fetchQueuePolicyApi').mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    const updateSpy = vi.spyOn(queuePolicyApi, 'updateQueuePolicyApi');
    renderQueuePolicyPage();

    const graceInput = await screen.findByLabelText(/Thời gian ân hạn đúng giờ/i);
    fireEvent.change(graceInput, { target: { value: '61' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu cấu hình chính sách/i });
    fireEvent.click(submitBtn);

    expect(
      await screen.findByText('Thời gian ân hạn đúng giờ phải từ 0 đến 60.'),
    ).toBeInTheDocument();
    expect(updateSpy).not.toHaveBeenCalled();
  });

  it('submits updated switch and ratio values via updateQueuePolicyApi and shows success banner', async () => {
    vi.spyOn(queuePolicyApi, 'fetchQueuePolicyApi').mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    const expectedPayload = {
      graceMinutes: 10,
      expectedDurationMinutes: 25,
      safetyBufferMinutes: 5,
      walkinPriorityRatio: { priority: 4, normal: 1 },
      publicNumberImmutable: false,
      manualPriorityRequiresVerification: false,
    };
    const updateSpy = vi
      .spyOn(queuePolicyApi, 'updateQueuePolicyApi')
      .mockResolvedValueOnce({ ...expectedPayload, lastUpdatedAt: '2026-11-02T04:00:00Z' });

    renderQueuePolicyPage();

    const graceInput = (await screen.findByLabelText(
      /Thời gian ân hạn đúng giờ/i,
    )) as HTMLInputElement;
    await waitFor(() => {
      expect(graceInput.value).toBe('10');
    });

    const publicNumberSwitch = screen.getByRole('switch', {
      name: /Giữ nguyên số thứ tự công khai/i,
    });
    fireEvent.click(publicNumberSwitch);

    const priorityInput = screen.getByLabelText(/Số lượt ưu tiên liên tiếp/i);
    fireEvent.change(priorityInput, { target: { value: '4' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu cấu hình chính sách/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith(expectedPayload);
    });

    expect(
      await screen.findByText('Đã lưu cấu hình chính sách hàng đợi thành công.'),
    ).toBeInTheDocument();
  });

  it('shows 404 unsupported endpoint message when updateQueuePolicyApi rejects with 404', async () => {
    vi.spyOn(queuePolicyApi, 'fetchQueuePolicyApi').mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    vi.spyOn(queuePolicyApi, 'updateQueuePolicyApi').mockRejectedValueOnce(
      makeAxiosError(404, 'Not Found'),
    );

    renderQueuePolicyPage();

    const graceInput = await screen.findByLabelText(/Thời gian ân hạn đúng giờ/i);
    await waitFor(() => {
      expect((graceInput as HTMLInputElement).value).toBe('10');
    });

    fireEvent.change(graceInput, { target: { value: '15' } });
    fireEvent.click(screen.getByRole('button', { name: /Lưu cấu hình chính sách/i }));

    expect(
      await screen.findByText(
        'Máy chủ chưa hỗ trợ API cấu hình chính sách hàng đợi (/api/v1/admin/queue-policy).',
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText('Đã lưu cấu hình chính sách hàng đợi thành công.'),
    ).not.toBeInTheDocument();
  });

  it('renders error alert and retry button when fetchQueuePolicyApi fails with 403, then loads form on retry', async () => {
    const fetchSpy = vi
      .spyOn(queuePolicyApi, 'fetchQueuePolicyApi')
      .mockRejectedValueOnce(makeAxiosError(403, 'Tài khoản quản trị chưa được gán bệnh viện.'));

    renderQueuePolicyPage();

    expect(
      await screen.findByText('Tài khoản quản trị chưa được gán bệnh viện.'),
    ).toBeInTheDocument();

    const retryBtn = screen.getByRole('button', { name: /Thử tải lại/i });
    expect(retryBtn).toBeInTheDocument();
    expect(screen.queryByLabelText(/Thời gian ân hạn đúng giờ/i)).toBeNull();
    expect(screen.getByRole('button', { name: /Lưu cấu hình chính sách/i })).toBeDisabled();

    fetchSpy.mockResolvedValueOnce(LOADED_QUEUE_POLICY_DTO);
    fireEvent.click(retryBtn);

    const graceInput = (await screen.findByLabelText(
      /Thời gian ân hạn đúng giờ/i,
    )) as HTMLInputElement;
    await waitFor(() => {
      expect(graceInput.value).toBe('10');
    });
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });
});
