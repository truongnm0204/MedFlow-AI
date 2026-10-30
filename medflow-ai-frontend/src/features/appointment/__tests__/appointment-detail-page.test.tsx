import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { resetMockAppointments } from '../data/appointment-list-mock';
import { AppointmentDetailPage } from '../pages/appointment-detail-page';

function renderAppointmentDetailPage(initialEntry = '/appointments/apt-1013-0902') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
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

describe('AppointmentDetailPage (SCR-APT-09 — Issue #53)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    resetMockAppointments();
  });

  it('renders full SCR-APT-09 appointment detail layout matching Figma 17:1340 for upcoming confirmed appointment', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    expect(
      await screen.findByRole('heading', { name: 'Chi Tiết Phiếu Hẹn Khám Ngoại Trú' }),
    ).toBeInTheDocument();
    expect(screen.getByText('SCR-APT-09')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM')).toBeInTheDocument();

    // Clinic & Doctor Card (2x2 Grid)
    expect(screen.getByText('Thông Tin Buồng Khám & Bác Sĩ Chuyên Khoa')).toBeInTheDocument();
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('09:00 - 09:30')).toBeInTheDocument();
    expect(screen.getByText('DV-TH-01: Khám Nội Tiêu Hóa Chuyên Sâu')).toBeInTheDocument();

    // Patient Info Card (4-Column metadata + Initial symptoms)
    expect(screen.getByText('Thông Tin Bệnh Nhân Đăng Ký')).toBeInTheDocument();
    expect(screen.getByText('1986 (40 tuổi)')).toBeInTheDocument();
    expect(screen.getByText('0912 345 678')).toBeInTheDocument();
    expect(screen.getByText('001086001234')).toBeInTheDocument();
    expect(
      screen.getByText(/Đau tức vùng thượng vị từng cơn sau ăn, ợ chua liên tục 5 ngày gần đây/i),
    ).toBeInTheDocument();

    // Right Column: Queue Number & Listed Fee Cards
    expect(screen.getByText('SỐ THỨ TỰ (STT) GIỮ TRƯỚC')).toBeInTheDocument();
    expect(screen.getByText('012')).toBeInTheDocument();
    expect(screen.getByText('Biểu Phí Niêm Yết')).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();

    // Valid actions for CONFIRMED appointment
    expect(screen.getByRole('button', { name: /In Phiếu Khám/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đổi Lịch Khám/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hủy lịch hẹn/i })).toBeInTheDocument();
  });

  it('hides reschedule and cancel actions for COMPLETED appointments and shows clinical conclusion', async () => {
    renderAppointmentDetailPage('/appointments/apt-0815-0830');

    expect(await screen.findByText('ĐÃ HOÀN TẤT KHÁM')).toBeInTheDocument();
    expect(
      screen.getByText(/Viêm dạ dày trợt hang vị mức độ nhẹ, đã kê đơn thuốc 14 ngày/i),
    ).toBeInTheDocument();

    // Invalid actions for COMPLETED must be hidden
    expect(screen.queryByRole('button', { name: /Đổi Lịch Khám/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Hủy lịch hẹn/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Cập nhật triệu chứng/i })).toBeNull();

    // Valid actions for COMPLETED remain available
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

  it('opens SCR-APT-10 reschedule modal and updates schedule slot when confirmed', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const rescheduleBtn = await screen.findByRole('button', { name: /Đổi Lịch Khám/i });
    fireEvent.click(rescheduleBtn);

    expect(screen.getByText('Đổi Lịch Hẹn Khám Ngoại Trú')).toBeInTheDocument();
    expect(screen.getByText('Không phát sinh phí đổi lịch (0 VNĐ)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Xác Nhận Đổi Sang Lịch Mới/i }));

    await waitFor(() => {
      expect(screen.getByText(/Đổi lịch khám thành công sang khung giờ/i)).toBeInTheDocument();
    });
    expect(screen.getByText('005')).toBeInTheDocument();
  });

  it('validates cancellation reason in cancel modal and transitions appointment to CANCELLED state', async () => {
    renderAppointmentDetailPage('/appointments/apt-1013-0902');

    const cancelBtn = await screen.findByRole('button', { name: /Hủy lịch hẹn/i });
    fireEvent.click(cancelBtn);

    expect(screen.getByText('Xác Nhận Hủy Lịch Hẹn Khám')).toBeInTheDocument();

    // Submit empty reason -> triggers Zod validation error
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lịch hẹn' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Vui lòng nhập lý do hủy lịch tối thiểu 10 ký tự.',
    );

    // Select a valid preset reason and confirm
    fireEvent.click(
      screen.getByRole('button', {
        name: /Người bệnh bận công tác đột xuất, xin chủ động dời sang kíp khám sau/i,
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy lịch hẹn' }));

    await waitFor(() => {
      expect(screen.getByText('ĐÃ HỦY BỞI NGƯỜI BỆNH')).toBeInTheDocument();
    });

    // After cancellation, modify and print buttons are hidden
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
});
