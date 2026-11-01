import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { AppointmentReviewPage } from '../pages/appointment-review-page';
import { useBookingStore } from '../store/booking.store';

function renderAppointmentReviewPage(
  initialEntry = '/appointment/review?mode=doctor&doctorId=doc-01&serviceCode=DV-TH-01&patientId=pat-self&date=2026-10-13&slotId=slot-0900',
) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/appointment/review" element={<AppointmentReviewPage />} />
          <Route
            path="/appointment/doctors/:doctorId/booking"
            element={<div>Quay lại Chọn Khung Giờ Bác Sĩ Mock</div>}
          />
          <Route
            path="/appointment/services/:serviceId/booking"
            element={<div>Quay lại Chọn Ca Khám Dịch Vụ Mock</div>}
          />
          <Route
            path="/appointments/:appointmentId"
            element={<div>Chi Tiết Phiếu Hẹn Đã Cấp STT Mock</div>}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentReviewPage — Soát xét Đặt lịch (SCR-APT-06 · Figma 14:1717)', () => {
  beforeEach(() => {
    useBookingStore.getState().reset();
  });

  it('renders full SCR-APT-06 layout matching Figma 14:1717 in doctor mode', async () => {
    renderAppointmentReviewPage();

    // Header & Stepper
    expect(await screen.findByText('BƯỚC CHUẨN BỊ CUỐI CÙNG')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Soát xét Thông tin Đặt khám Ngoại trú' }),
    ).toBeInTheDocument();

    const currentStep = screen.getByText('Soát xét thông tin').closest('li');
    expect(currentStep).toHaveAttribute('aria-current', 'step');
    expect(screen.getByText('Khoa & Bác sĩ')).toBeInTheDocument();
    expect(screen.getByText('Hoàn tất & Cấp STT')).toBeInTheDocument();

    // Card 1: Patient EHR Info
    expect(await screen.findByText('1. THÔNG TIN NGƯỜI BỆNH')).toBeInTheDocument();
    expect(screen.getByText('Chính chủ tài khoản')).toBeInTheDocument();
    expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('PAT-8921')).toBeInTheDocument();
    expect(screen.getByText('15/08/1986')).toBeInTheDocument();
    expect(screen.getByText('(40 tuổi)')).toBeInTheDocument();
    expect(screen.getByText('0912 345 678')).toBeInTheDocument();
    expect(screen.getByText('001086001234')).toBeInTheDocument();
    expect(screen.getByText('Phường Hàng Trống, Quận Hoàn Kiếm, TP. Hà Nội')).toBeInTheDocument();
    expect(
      screen.getByText(
        /Đau tức vùng thượng vị, ợ chua kéo dài, tái khám theo dõi dạ dày trào ngược GERD\./i,
      ),
    ).toBeInTheDocument();

    // Card 2: Schedule & Location
    expect(screen.getByText('2. THỜI GIAN KHÁM & ĐỊA ĐIỂM TIẾP NHẬN')).toBeInTheDocument();
    expect(screen.getByText('25 phút/lượt khám')).toBeInTheDocument();
    expect(screen.getByText('TRƯỞNG KÍP KHÁM - BÁC SĨ ĐIỀU TRỊ CAO CẤP')).toBeInTheDocument();
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText('Khoa Tiêu Hóa - Gan Mật')).toBeInTheDocument();
    expect(screen.getByText('(Mã DV-TH-01)')).toBeInTheDocument();
    expect(
      screen.getByText('09:00 - 09:30, Thứ Ba, ngày 13/10/2026 (Ca Sáng)'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Phòng 204 - Tầng 2, Nhà A (Khu Khám Ngoại Trú Tự Nguyện)'),
    ).toBeInTheDocument();

    // Card 3: Listed Fee Breakdown
    expect(screen.getByText('3. CHI PHÍ DỊCH VỤ NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('Dịch vụ Ngoại trú Tự nguyện')).toBeInTheDocument();
    expect(screen.getByText('Phí khám chuyên khoa Tiêu Hóa (BS.CKII)')).toBeInTheDocument();
    expect(screen.getByText('0 VNĐ (Đã bao gồm trọn gói)')).toBeInTheDocument();
    expect(screen.getAllByText('250.000 VNĐ').length).toBeGreaterThanOrEqual(2);

    // Card 4: Reception & Pre-allocated Queue Policy
    expect(
      screen.getByText('Chính sách Tiếp đón & Cấp Số Thứ Tự (STT) Giữ Trước'),
    ).toBeInTheDocument();
    expect(screen.getByText('Chính xác theo phút')).toBeInTheDocument();
    expect(screen.getByText('15 phút (08:45)')).toBeInTheDocument();
  });

  it('supports service mode and dependent family member profile', async () => {
    renderAppointmentReviewPage(
      '/appointment/review?mode=service&serviceId=srv-th-01&serviceCode=DV-TH-01&patientId=pat-child&date=2026-10-13&shiftId=shift-morning',
    );

    expect(await screen.findByText('Ngày & Ca khám')).toBeInTheDocument();
    expect(screen.getAllByText('Dịch vụ khám').length).toBeGreaterThanOrEqual(2);
    expect(await screen.findByText('Nguyễn Bảo Châu')).toBeInTheDocument();
    expect(screen.getByText('PAT-9012')).toBeInTheDocument();
    expect(screen.getByText('Hồ sơ người thân • Con gái')).toBeInTheDocument();
  });

  it('validates and updates declared symptom note inline on the review screen', async () => {
    renderAppointmentReviewPage();

    const editBtn = await screen.findByRole('button', { name: /Chỉnh sửa lý do khám/i });
    fireEvent.click(editBtn);

    const textarea = screen.getByLabelText('Lý do khám và triệu chứng khai báo ban đầu');
    fireEvent.change(textarea, { target: { value: 'a' } });

    fireEvent.click(screen.getByRole('button', { name: 'Lưu cập nhật' }));
    expect(
      await screen.findByText('Vui lòng mô tả lý do khám tối thiểu 3 ký tự.'),
    ).toBeInTheDocument();

    fireEvent.change(textarea, {
      target: { value: 'Đau rát thượng vị sau ăn cay, cần nội soi kiểm tra.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu cập nhật' }));

    expect(
      await screen.findByText(/Đau rát thượng vị sau ăn cay, cần nội soi kiểm tra\./),
    ).toBeInTheDocument();
    expect(useBookingStore.getState().symptomNote).toBe(
      'Đau rát thượng vị sau ăn cay, cần nội soi kiểm tra.',
    );
  });

  it('navigates back to slot selection when clicking back button', async () => {
    renderAppointmentReviewPage();

    const backBtn = await screen.findByRole('button', {
      name: /Quay lại chọn khung giờ khác/i,
    });
    fireEvent.click(backBtn);

    expect(await screen.findByText('Quay lại Chọn Khung Giờ Bác Sĩ Mock')).toBeInTheDocument();
  });

  it('confirms booking and navigates to appointment detail page with queue number', async () => {
    renderAppointmentReviewPage();

    const confirmBtn = await screen.findByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/i,
    });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(screen.getByText('Chi Tiết Phiếu Hẹn Đã Cấp STT Mock')).toBeInTheDocument();
    });
  });

  it('displays an accessible error alert when slot is already full/conflicted', async () => {
    renderAppointmentReviewPage(
      '/appointment/review?mode=doctor&doctorId=doc-01&serviceCode=DV-TH-01&patientId=pat-self&date=2026-10-13&slotId=slot-0730',
    );

    const confirmBtn = await screen.findByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/i,
    });
    fireEvent.click(confirmBtn);

    const alertBox = await screen.findByRole('alert');
    expect(alertBox).toHaveTextContent(
      /Khung giờ khám vừa hết chỗ do bệnh nhân khác vừa hoàn tất đặt lịch/i,
    );
  });
});
