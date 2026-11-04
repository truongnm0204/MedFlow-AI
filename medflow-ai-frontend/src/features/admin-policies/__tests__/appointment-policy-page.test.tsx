import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import * as policyApi from '../api/appointment-policy.api';
import {
  DEFAULT_APPOINTMENT_POLICY_VALUES,
  resetMockAppointmentPolicy,
} from '../data/appointment-policy-mock';
import { AppointmentPolicyPage } from '../pages/appointment-policy-page';
import { appointmentPolicySchema } from '../schemas/appointment-policy.schema';

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

function renderPolicyPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/policies']}>
        <AppointmentPolicyPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('appointmentPolicySchema (UC54)', () => {
  it('accepts default appointment policy values', () => {
    const parsed = appointmentPolicySchema.safeParse(DEFAULT_APPOINTMENT_POLICY_VALUES);
    expect(parsed.success).toBe(true);
  });

  it('rejects out-of-range field values', () => {
    const invalidWindow = appointmentPolicySchema.safeParse({
      ...DEFAULT_APPOINTMENT_POLICY_VALUES,
      bookingWindowDays: 0,
    });
    expect(invalidWindow.success).toBe(false);

    const invalidGrace = appointmentPolicySchema.safeParse({
      ...DEFAULT_APPOINTMENT_POLICY_VALUES,
      noShowGraceMinutes: 121,
    });
    expect(invalidGrace.success).toBe(false);
  });

  it('rejects cross-field violation when bookingLeadHours >= bookingWindowDays * 24', () => {
    const result = appointmentPolicySchema.safeParse({
      ...DEFAULT_APPOINTMENT_POLICY_VALUES,
      bookingWindowDays: 1,
      bookingLeadHours: 24,
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      const leadIssue = result.error.issues.find((i) => i.path.includes('bookingLeadHours'));
      expect(leadIssue?.message).toBe(
        'Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.',
      );
    }
  });
});

describe('AppointmentPolicyPage (SCR-ADM-POL-01)', () => {
  beforeEach(() => {
    resetMockAppointmentPolicy();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders heading, breadcrumbs, 3 section titles, 10 inputs, and live summary card', async () => {
    renderPolicyPage();

    expect(
      await screen.findByRole('heading', { name: /Cấu hình Chính sách Đặt lịch Khám/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Chính sách đặt lịch khám (SCR-ADM-POL-01)')).toBeInTheDocument();
    expect(await screen.findByText('Giới hạn đặt lịch trực tuyến')).toBeInTheDocument();
    expect(screen.getByText('Quy tắc đổi lịch & hủy lịch hẹn')).toBeInTheDocument();
    expect(screen.getByText('Kiểm soát vắng mặt & khóa đặt lịch')).toBeInTheDocument();

    const windowInput = screen.getByLabelText(/Số ngày được đặt trước tối đa/i) as HTMLInputElement;
    expect(windowInput.value).toBe('30');

    const summaryCard = screen.getByTestId('policy-summary-card');
    expect(summaryCard).toHaveTextContent(
      'Mở đặt trước tối đa 30 ngày, cách giờ khám ít nhất 2 giờ (tối đa 3 lịch hoạt động/hồ sơ).',
    );
    expect(summaryCard).toHaveTextContent(
      'Trễ quá 15 phút tính NO_SHOW; vắng ≥ 3 lần trong 90 ngày sẽ khóa đặt lịch 30 ngày.',
    );
  });

  it('updates live summary card when inputs change, including disabling no-show block when set to 0', async () => {
    renderPolicyPage();

    const blockDaysInput = await screen.findByLabelText(/Số ngày khóa đặt lịch/i);
    fireEvent.change(blockDaysInput, { target: { value: '0' } });

    const summaryCard = screen.getByTestId('policy-summary-card');
    expect(summaryCard).toHaveTextContent(
      'Trễ quá 15 phút tính NO_SHOW; hiện đang tắt khóa đặt lịch tự động (0 ngày).',
    );
  });

  it('blocks submission and shows error when bookingLeadHours >= bookingWindowDays * 24', async () => {
    const updateSpy = vi.spyOn(policyApi, 'updateAppointmentPolicyApi');
    renderPolicyPage();

    const windowInput = await screen.findByLabelText(/Số ngày được đặt trước tối đa/i);
    const leadInput = screen.getByLabelText(/Số giờ đặt trước tối thiểu/i);

    fireEvent.change(windowInput, { target: { value: '1' } });
    fireEvent.change(leadInput, { target: { value: '30' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu cấu hình chính sách/i });
    fireEvent.click(submitBtn);

    expect(
      await screen.findByText('Thời gian đặt trước tối thiểu phải nhỏ hơn cửa sổ đặt lịch.'),
    ).toBeInTheDocument();
    expect(updateSpy).not.toHaveBeenCalled();
  });

  it('restores default values when clicking Khôi phục mặc định', async () => {
    renderPolicyPage();

    const windowInput = (await screen.findByLabelText(
      /Số ngày được đặt trước tối đa/i,
    )) as HTMLInputElement;
    fireEvent.change(windowInput, { target: { value: '60' } });
    expect(windowInput.value).toBe('60');

    const restoreBtn = screen.getByRole('button', { name: /Khôi phục mặc định/i });
    fireEvent.click(restoreBtn);

    expect(windowInput.value).toBe('30');
    expect(screen.getByText(/Đã điền giá trị mặc định chuẩn hệ thống/i)).toBeInTheDocument();
  });

  it('submits valid changes via updateAppointmentPolicyApi and shows success status banner', async () => {
    const updateSpy = vi.spyOn(policyApi, 'updateAppointmentPolicyApi');
    renderPolicyPage();

    const windowInput = await screen.findByLabelText(/Số ngày được đặt trước tối đa/i);
    const blockDaysInput = screen.getByLabelText(/Số ngày khóa đặt lịch/i);

    fireEvent.change(windowInput, { target: { value: '45' } });
    fireEvent.change(blockDaysInput, { target: { value: '0' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu cấu hình chính sách/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith({
        ...DEFAULT_APPOINTMENT_POLICY_VALUES,
        bookingWindowDays: 45,
        noShowBlockDays: 0,
      });
    });

    expect(
      await screen.findByText('Đã lưu cấu hình chính sách đặt lịch thành công.'),
    ).toBeInTheDocument();
  });

  it('displays server ProblemDetails error message when updateAppointmentPolicyApi rejects with 403', async () => {
    vi.spyOn(policyApi, 'updateAppointmentPolicyApi').mockRejectedValueOnce(
      makeAxiosError(403, 'Tài khoản quản trị chưa được gán bệnh viện.'),
    );
    renderPolicyPage();

    const windowInput = await screen.findByLabelText(/Số ngày được đặt trước tối đa/i);
    fireEvent.change(windowInput, { target: { value: '45' } });

    const submitBtn = screen.getByRole('button', { name: /Lưu cấu hình chính sách/i });
    fireEvent.click(submitBtn);

    expect(
      await screen.findByText('Tài khoản quản trị chưa được gán bệnh viện.'),
    ).toBeInTheDocument();
  });
});
