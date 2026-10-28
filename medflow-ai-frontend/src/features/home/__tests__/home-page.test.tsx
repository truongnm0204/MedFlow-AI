import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppRouter } from '@/app/router/app-router';
import { useAuthStore } from '@/features/auth/store/auth.store';
import type { UserDto } from '@/features/auth/types/auth.types';

import * as homeApi from '../api/home.api';
import { MOCK_PATIENT_HOME_SUMMARY, MOCK_QUICK_SLOTS } from '../data/patient-home-mock';
import { HomePage } from '../pages/home-page';

const patientUser: UserDto = {
  id: 10,
  email: 'an.nguyen@medflow.vn',
  fullName: 'Nguyễn Văn An',
  roles: ['PATIENT'],
  patientCode: 'PAT-2026-0001',
};

const diagnosticStaffUser: UserDto = {
  id: 21,
  email: 'cls@medflow.vn',
  fullName: 'KTV. Trần Thị Mai',
  roles: ['DIAGNOSTIC_STAFF'],
  patientCode: null,
};

function renderWithProviders(initialEntry = '/') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AppRouter />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function renderHomePage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('HomePage & PatientHomeHub (M1-009)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.setState({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
    });
  });

  it('trạng thái Guest: hiển thị trang chủ công khai và ẩn bảng điều khiển bệnh nhân + HIS Workbench', () => {
    renderHomePage();

    expect(
      screen.getByText('Đặt Lịch Khám Chuyên Khoa Nhanh Chóng, Không Chờ Đợi'),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText('Bảng điều khiển Bệnh nhân')).toBeNull();
    expect(screen.queryByText(/Mở Bàn làm việc Bác sĩ/i)).toBeNull();
  });

  it('trạng thái Patient đã đăng nhập: hiển thị PatientHomeHub, các lối vào hợp lệ và ẩn HIS Workbench', async () => {
    vi.spyOn(homeApi, 'fetchPatientHomeSummary').mockResolvedValue(MOCK_PATIENT_HOME_SUMMARY);
    useAuthStore.setState({
      user: patientUser,
      accessToken: 'token-patient',
      refreshToken: 'refresh-patient',
      isAuthenticated: true,
    });

    renderHomePage();

    expect(screen.getByText('Xin chào, Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Mã BN: PAT-2026-0001')).toBeInTheDocument();
    expect(screen.queryByText(/Mở Bàn làm việc Bác sĩ/i)).toBeNull();

    await waitFor(() => {
      expect(screen.getByText('#MF-261028-08')).toBeInTheDocument();
    });

    expect(screen.getAllByText('Đặt lịch khám mới').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Hồ sơ cá nhân').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Hồ sơ người thân').length).toBeGreaterThan(0);
  });

  it('trạng thái Nhân viên y tế (DIAGNOSTIC_STAFF): được phép thấy thanh chuyển đổi HIS Workbench', () => {
    useAuthStore.setState({
      user: diagnosticStaffUser,
      accessToken: 'token-cls',
      refreshToken: 'refresh-cls',
      isAuthenticated: true,
    });

    renderHomePage();

    expect(screen.getByText(/Mở Bàn làm việc Bác sĩ/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Bảng điều khiển Bệnh nhân')).toBeNull();
  });

  it('xử lý trạng thái Empty và Error khi tải dữ liệu tổng quan Patient Home', async () => {
    const spy = vi
      .spyOn(homeApi, 'fetchPatientHomeSummary')
      .mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ'))
      .mockResolvedValueOnce({
        ...MOCK_PATIENT_HOME_SUMMARY,
        upcomingAppointments: [],
      });

    useAuthStore.setState({
      user: patientUser,
      accessToken: 'token-patient',
      refreshToken: 'refresh-patient',
      isAuthenticated: true,
    });

    renderHomePage();

    await waitFor(() => {
      expect(screen.getByText('Lỗi kết nối máy chủ')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Thử lại'));

    await waitFor(() => {
      expect(
        screen.getByText('Bạn chưa có lịch hẹn khám ngoại trú nào sắp tới.'),
      ).toBeInTheDocument();
    });
    expect(spy).toHaveBeenCalledTimes(2);
  });

  it('form Quick Booking và AI Triage kiểm tra hợp lệ, xóa kết quả cũ khi lỗi và hiển thị slot', async () => {
    vi.spyOn(homeApi, 'searchQuickSlots').mockResolvedValue(MOCK_QUICK_SLOTS.gastro);
    renderHomePage();

    // 1. Tìm slot hợp lệ trước -> hiển thị danh sách khung giờ trống và chọn slot
    fireEvent.change(screen.getByLabelText('Chuyên Khoa'), { target: { value: 'gastro' } });
    fireEvent.click(screen.getByText('Tìm Lịch Khám (AI)'));

    await waitFor(() => {
      expect(screen.getByText('08:45 - 09:15')).toBeInTheDocument();
    });

    fireEvent.click(screen.getAllByText('Chọn khung giờ')[0]);
    expect(screen.getByText(/Đã chọn khung giờ 08:45 - 09:15/i)).toBeInTheDocument();

    // 2. Chọn chuyên khoa rỗng rồi submit lại -> báo lỗi validation và ẩn kết quả slot cũ
    fireEvent.change(screen.getByLabelText('Chuyên Khoa'), { target: { value: '' } });
    fireEvent.click(screen.getByText('Tìm Lịch Khám (AI)'));
    expect(screen.getByText('Vui lòng chọn chuyên khoa khám.')).toBeInTheDocument();
    expect(screen.queryByText('08:45 - 09:15')).toBeNull();

    // 3. AI Triage: nhập quá ngắn -> báo lỗi, nhập hợp lệ -> hiện gợi ý
    fireEvent.change(screen.getByLabelText('Mô tả triệu chứng'), { target: { value: 'ho' } });
    fireEvent.click(screen.getByText('Gợi ý ngay'));
    expect(
      screen.getByText('Vui lòng nhập mô tả triệu chứng tối thiểu 3 ký tự để AI phân luồng.'),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Mô tả triệu chứng'), {
      target: { value: 'đau bụng âm ỉ vùng thượng vị' },
    });
    fireEvent.click(screen.getByText('Gợi ý ngay'));
    expect(screen.getByText(/Chuyên khoa Nội Tiêu Hóa/i)).toBeInTheDocument();
  });

  it('route /patient yêu cầu đăng nhập (redirect về /login khi chưa xác thực)', () => {
    renderWithProviders('/patient');
    expect(screen.getByText('Cổng Đăng Nhập Duy Nhất')).toBeInTheDocument();
  });
});
