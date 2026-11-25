import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { MOCK_APPOINTMENTS, resetMockAppointments } from '../data/appointment-list-mock';
import { getMockAvailableSlotsPayload } from '../data/slot-booking-mock';
import { AppointmentDetailPage } from '../pages/appointment-detail-page';
import { useBookingStore } from '../store/booking.store';

function renderAppointmentDetailPage(initialEntry = '/appointments/apt-1013-0902') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/appointments/:appointmentId" element={<AppointmentDetailPage />} />
          <Route path="/appointments" element={<div>Danh sách lịch hẹn</div>} />
          <Route path="/appointment/doctors" element={<div>Danh bạ bác sĩ</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentDetailPage (SCR-APT-09 — Issue #53 & #812)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetMockAppointments();

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/appointments/available-slots') {
        const params = (
          config as { params?: { doctorId?: string; serviceId?: string } } | undefined
        )?.params;
        return {
          data: getMockAvailableSlotsPayload(params?.doctorId, params?.serviceId, '2026-10-16'),
        };
      }
      if (typeof url === 'string' && url.startsWith('/appointments/')) {
        const id = decodeURIComponent(url.replace('/appointments/', ''));
        if (id === 'apt-unauthorized') {
          throw new AxiosError(
            'Forbidden',
            'ERR_BAD_REQUEST',
            undefined,
            {},
            {
              status: 403,
              statusText: 'Forbidden',
              headers: {},
              config: {} as never,
              data: {
                status: 403,
                detail: 'Bạn không có quyền xem chi tiết lịch hẹn của tài khoản bệnh nhân khác.',
              },
            },
          );
        }
        const found = MOCK_APPOINTMENTS.find(
          (item) => item.id === id || item.appointmentCode === id,
        );
        if (!found) {
          throw new AxiosError(
            'Not Found',
            'ERR_BAD_REQUEST',
            undefined,
            {},
            {
              status: 404,
              statusText: 'Not Found',
              headers: {},
              config: {} as never,
              data: { status: 404, detail: 'Không tìm thấy lịch hẹn.' },
            },
          );
        }
        return { data: found };
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.spyOn(apiClient, 'patch').mockImplementation(async (url, body) => {
      const match = String(url).match(/^\/appointments\/([^/]+)\/reason$/);
      if (match) {
        const id = decodeURIComponent(match[1]!);
        const found = MOCK_APPOINTMENTS.find(
          (item) => item.id === id || item.appointmentCode === id,
        );
        if (!found) throw new Error('Appointment not found');
        found.reasonForVisit = (body as { reasonForVisit: string }).reasonForVisit;
        return { data: { ...found } };
      }
      throw new Error(`Unexpected PATCH ${url}`);
    });

    vi.spyOn(apiClient, 'post').mockImplementation(async (url, body) => {
      const rescheduleMatch = String(url).match(/^\/appointments\/([^/]+)\/reschedule$/);
      if (rescheduleMatch) {
        const id = decodeURIComponent(rescheduleMatch[1]!);
        const found = MOCK_APPOINTMENTS.find(
          (item) => item.id === id || item.appointmentCode === id,
        );
        if (!found) throw new Error('Appointment not found');
        found.scheduledDate = '2026-10-16';
        found.scheduledDateBadge = '16/10/2026';
        found.scheduledDateFullLabel = 'Thứ Sáu, 16/10/2026 (Ca Sáng)';
        found.scheduledTimeRange = '08:00 - 08:30';
        found.queueNumber = '005';
        return { data: { ...found } };
      }

      const cancelMatch = String(url).match(/^\/appointments\/([^/]+)\/cancel$/);
      if (cancelMatch) {
        const id = decodeURIComponent(cancelMatch[1]!);
        const found = MOCK_APPOINTMENTS.find(
          (item) => item.id === id || item.appointmentCode === id,
        );
        if (!found) throw new Error('Appointment not found');
        found.status = 'CANCELLED';
        found.statusBannerLabel = 'ĐÃ HỦY BỞI NGƯỜI BỆNH';
        found.cancellationReason = (body as { cancellationReason: string }).cancellationReason;
        return { data: { ...found } };
      }

      throw new Error(`Unexpected POST ${url}`);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders full SCR-APT-09 appointment detail layout matching Figma 17:1340 for upcoming confirmed appointment', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    expect(
      await screen.findByRole('heading', { name: 'Chi Tiết Phiếu Hẹn Khám Ngoại Trú' }),
    ).toBeInTheDocument();
    expect(screen.getByText('SCR-APT-09')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM')).toBeInTheDocument();

    expect(screen.getByText('Thông Tin Buồng Khám & Bác Sĩ Chuyên Khoa')).toBeInTheDocument();
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('09:00 - 09:30')).toBeInTheDocument();
    expect(screen.getByText('DV-TH-01: Khám Nội Tiêu Hóa Chuyên Sâu')).toBeInTheDocument();

    expect(screen.getByText('Thông Tin Bệnh Nhân Đăng Ký')).toBeInTheDocument();
    expect(screen.getByText('1986 (40 tuổi)')).toBeInTheDocument();
    expect(screen.getByText('0912 345 678')).toBeInTheDocument();
    expect(screen.getByText('001086001234')).toBeInTheDocument();
    expect(
      screen.getByText(/Đau tức vùng thượng vị từng cơn sau ăn, ợ chua liên tục 5 ngày gần đây/i),
    ).toBeInTheDocument();

    expect(screen.getByText('SỐ THỨ TỰ (STT) GIỮ TRƯỚC')).toBeInTheDocument();
    expect(screen.getByText('012')).toBeInTheDocument();
    expect(screen.getByText('Biểu Phí Niêm Yết')).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();

    expect(screen.getByRole('button', { name: /In Phiếu Khám/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đổi Lịch Khám/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hủy lịch hẹn/i })).toBeInTheDocument();
  });

  it('displays Chưa cập nhật instead of hardcoded phone when patientPhone is missing', async () => {
    const target = MOCK_APPOINTMENTS.find((a) => a.id === 'apt-1013-0902')!;
    target.patientPhone = '';

    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    expect(
      await screen.findByRole('heading', { name: 'Chi Tiết Phiếu Hẹn Khám Ngoại Trú' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Chưa cập nhật')).toBeInTheDocument();
    expect(screen.queryByText('0912 345 678')).not.toBeInTheDocument();
  });

  it('hides reschedule and cancel actions for COMPLETED appointments and shows clinical conclusion', async () => {
    renderAppointmentDetailPage('/appointments/apt-0815-0830');

    expect(await screen.findByText('ĐÃ HOÀN TẤT KHÁM')).toBeInTheDocument();
    expect(
      screen.getByText(/Viêm dạ dày trợt hang vị mức độ nhẹ, đã kê đơn thuốc 14 ngày/i),
    ).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /Đổi Lịch Khám/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Hủy lịch hẹn/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Cập nhật triệu chứng/i })).toBeNull();

    expect(screen.getByRole('button', { name: /In Phiếu Khám/i })).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Đặt lại Chuyên khoa Tiêu Hóa/i }),
    ).toBeInTheDocument();
  });

  it('validates initial symptom update form with Zod and updates symptom note on valid submission', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const editBtn = await screen.findByRole('button', { name: /Cập nhật triệu chứng/i });
    fireEvent.click(editBtn);

    const textarea = screen.getByLabelText('Lý do khám / Triệu chứng ban đầu');
    fireEvent.change(textarea, { target: { value: 'Ngắn' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu triệu chứng' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Lý do khám / triệu chứng ban đầu cần tối thiểu 10 ký tự.',
    );

    fireEvent.change(textarea, {
      target: {
        value: 'Đau âm ỉ vùng thượng vị kèm đầy hơi sau bữa tối khoảng 2 giờ.',
      },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu triệu chứng' }));

    expect(
      await screen.findByText('Đã cập nhật lý do khám / triệu chứng ban đầu vào hồ sơ tiếp nhận.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Đau âm ỉ vùng thượng vị kèm đầy hơi sau bữa tối khoảng 2 giờ.'),
    ).toBeInTheDocument();
  });

  it('opens SCR-APT-10 reschedule modal, loads real API slots, and updates schedule slot when confirmed', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const rescheduleBtn = await screen.findByRole('button', { name: /Đổi Lịch Khám/i });
    fireEvent.click(rescheduleBtn);

    expect(screen.getByText('Đổi Lịch Hẹn Khám Ngoại Trú')).toBeInTheDocument();
    expect(screen.getByText('Không phát sinh phí đổi lịch (0 VNĐ)')).toBeInTheDocument();

    expect(await screen.findByText('Trống chỗ')).toBeInTheDocument();

    const confirmBtn = screen.getByRole('button', { name: /Xác Nhận Đổi Sang Lịch Mới/i });
    expect(confirmBtn).toBeEnabled();
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(screen.getByText(/Đổi lịch khám thành công sang khung giờ/i)).toBeInTheDocument();
    });
    expect(screen.getByText('005')).toBeInTheDocument();
  });

  it('blocks reschedule confirmation and shows empty/error states when no valid slot is available', async () => {
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (url === '/appointments/available-slots') {
        return { data: { dates: [], morningSlots: [], afternoonSlots: [] } };
      }
      const found = MOCK_APPOINTMENTS.find((item) => item.id === 'apt-1013-0902')!;
      return { data: found };
    });

    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const rescheduleBtn = await screen.findByRole('button', { name: /Đổi Lịch Khám/i });
    fireEvent.click(rescheduleBtn);

    expect(
      await screen.findByText(/Hiện chưa có khung giờ trống phù hợp của/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Xác Nhận Đổi Sang Lịch Mới/i })).toBeDisabled();
  });

  it('validates cancellation reason in cancel modal and transitions appointment to CANCELLED state', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const cancelBtn = await screen.findByRole('button', { name: /Hủy lịch hẹn/i });
    fireEvent.click(cancelBtn);

    expect(screen.getByText('Xác Nhận Hủy Lịch Hẹn Khám')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lịch hẹn' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Vui lòng nhập lý do hủy lịch tối thiểu 10 ký tự.',
    );

    fireEvent.click(
      screen.getByRole('button', {
        name: /Người bệnh bận công tác đột xuất, xin chủ động dời sang kíp khám sau/i,
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lịch hẹn' }));

    await waitFor(() => {
      expect(screen.getByText('ĐÃ HỦY BỞI NGƯỜI BỆNH')).toBeInTheDocument();
    });

    expect(screen.queryByRole('button', { name: /Hủy lịch hẹn/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Đổi Lịch Khám/i })).toBeNull();
  });

  it('renders error alert when appointment ID does not exist or is unauthorized', async () => {
    renderAppointmentDetailPage('/appointments/apt-unauthorized');

    expect(
      await screen.findByRole('heading', {
        name: 'Không thể hiển thị chi tiết phiếu hẹn khám',
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Bạn không có quyền xem chi tiết lịch hẹn của tài khoản bệnh nhân khác/i),
    ).toBeInTheDocument();
  });

  it('renders sanitized placeholders instead of hardcoded 001086001234 CCCD or fake epigastric symptom when fields are empty', async () => {
    const target = MOCK_APPOINTMENTS.find((a) => a.id === 'apt-1013-0902')!;
    target.patientNationalId = undefined;
    target.patientBirthYearLabel = '';
    target.reasonForVisit = '';

    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    expect(
      await screen.findByRole('heading', { name: 'Chi Tiết Phiếu Hẹn Khám Ngoại Trú' }),
    ).toBeInTheDocument();
    expect(screen.queryByText('001086001234')).not.toBeInTheDocument();
    expect(screen.queryByText('1986 (40 tuổi)')).not.toBeInTheDocument();
    expect(screen.queryByText(/Đau tức vùng thượng vị/i)).not.toBeInTheDocument();
    expect(screen.getByText('Đã bảo mật')).toBeInTheDocument();
    expect(screen.getByText('Chưa ghi nhận triệu chứng ban đầu.')).toBeInTheDocument();
  });

  it('hides Đổi Lịch Khám when availableActions omits RESCHEDULE (MaxReschedules reached) and preserves selectedDoctorId on rebook', async () => {
    useBookingStore.getState().reset();
    const upcoming = MOCK_APPOINTMENTS.find((a) => a.id === 'apt-1013-0902')!;
    upcoming.availableActions = ['CANCEL', 'UPDATE_REASON', 'PRINT_SLIP', 'TRACK_QUEUE'];

    const view = renderAppointmentDetailPage('/appointments/apt-1013-0902');
    expect(
      await screen.findByRole('heading', { name: 'Chi Tiết Phiếu Hẹn Khám Ngoại Trú' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Đổi Lịch Khám/i })).toBeNull();
    expect(screen.getByRole('button', { name: /Hủy lịch hẹn/i })).toBeInTheDocument();
    view.unmount();

    renderAppointmentDetailPage('/appointments/apt-0815-0830');
    const rebookBtn = await screen.findByRole('button', { name: /Đặt lại Chuyên khoa Tiêu Hóa/i });
    fireEvent.click(rebookBtn);

    const store = useBookingStore.getState();
    expect(store.selectedDoctorId).toBe('doc-tran-quoc-tuan');
    expect(store.selectedServiceId).toBe('srv-gastro-01');
  });
});
