import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import type { PatientProfileResponseDto } from '../api/patient-profile.api';
import { PatientProfilePage } from '../pages/patient-profile-page';

const SAMPLE_PROFILE_DTO: PatientProfileResponseDto = {
  id: 10,
  userId: 10,
  patientCode: 'PAT-2026-0010',
  fullName: 'Nguyễn Văn An',
  dateOfBirth: '1988-08-15',
  age: 38,
  gender: 'MALE',
  phone: '0912345678',
  email: 'nguyenvana.patient@gmail.com',
  isEmailVerified: true,
  isIdentityVerified: true,
  maskedIdentification: '****** (Đã xác thực CCCD)',
  status: 'ACTIVE',
  address: 'Số 45 Tràng Tiền, Hoàn Kiếm, Hà Nội',
  bloodType: null,
  allowedActions: ['UPDATE_PERSONAL_PROFILE'],
};

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <PatientProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PatientProfilePage', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_PROFILE_DTO,
    } as AxiosResponse<PatientProfileResponseDto>);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('hiển thị thông tin bệnh nhân từ API thật và không gán nhầm nhóm máu O+ khi null', async () => {
    renderPage();

    expect((await screen.findAllByText('NGUYỄN VĂN AN')).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Đã xác thực danh tính (CCCD)')).toBeInTheDocument();
    expect(screen.queryByText('O+')).not.toBeInTheDocument();
    expect(screen.getAllByText('Chưa cập nhật').length).toBeGreaterThan(0);
    expect(screen.getByText('Hồ sơ người thân')).toBeInTheDocument();
    expect(screen.getByText('Lịch sử khám ngoại trú')).toBeInTheDocument();
  });

  it('hiển thị chính xác thẻ thông tin định danh y tế (Read-only)', async () => {
    renderPage();

    expect(await screen.findByText('Thông tin định danh y tế')).toBeInTheDocument();
    expect(screen.getByText('Chỉ đọc (Read-only)')).toBeInTheDocument();
    expect(screen.getByText('15/08/1988')).toBeInTheDocument();
    expect(screen.getByText('Tuổi: 38')).toBeInTheDocument();
    expect(screen.getByText('Nam')).toBeInTheDocument();
  });

  it('hiển thị thẻ thông tin liên hệ và mở modal cập nhật', async () => {
    renderPage();

    expect(await screen.findByText('Thông tin liên hệ & cư trú')).toBeInTheDocument();
    expect(screen.getByText('0912345678')).toBeInTheDocument();
    expect(screen.getByText('nguyenvana.patient@gmail.com')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Chỉnh sửa thông tin liên hệ'));
    expect(screen.getByText('Cập nhật Thông tin Liên hệ Cá nhân')).toBeInTheDocument();
  });

  it('lưu cập nhật hồ sơ qua PUT /patients/me/profile và đồng bộ lại giao diện thật', async () => {
    let currentDto: PatientProfileResponseDto = SAMPLE_PROFILE_DTO;
    const updatedDto: PatientProfileResponseDto = {
      ...SAMPLE_PROFILE_DTO,
      phone: '0988112233',
      address: '99 Nguyễn Huệ, Quận 1, TP.HCM',
      bloodType: 'AB+',
      emergencyContact: {
        fullName: 'Trần Thị Mai',
        relationship: 'Vợ',
        phone: '0933445566',
      },
    };
    vi.spyOn(apiClient, 'get').mockImplementation(
      async () =>
        ({
          data: currentDto,
        }) as AxiosResponse<PatientProfileResponseDto>,
    );
    const putSpy = vi.spyOn(apiClient, 'put').mockImplementation(async () => {
      currentDto = updatedDto;
      return { data: updatedDto } as AxiosResponse<PatientProfileResponseDto>;
    });

    renderPage();

    expect(await screen.findByText('Thông tin liên hệ & cư trú')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Chỉnh sửa thông tin liên hệ'));

    fireEvent.change(screen.getByLabelText(/Số điện thoại liên hệ chính/i), {
      target: { value: '0988112233' },
    });
    fireEvent.change(screen.getByLabelText(/Nhóm máu \(tự khai báo\)/i), {
      target: { value: 'AB+' },
    });
    fireEvent.change(screen.getByLabelText(/Địa chỉ thường trú/i), {
      target: { value: '99 Nguyễn Huệ, Quận 1, TP.HCM' },
    });
    fireEvent.change(screen.getByLabelText(/Họ và tên người liên hệ/i), {
      target: { value: 'Trần Thị Mai' },
    });
    fireEvent.change(screen.getByLabelText(/Mối quan hệ/i), {
      target: { value: 'Vợ' },
    });
    fireEvent.change(screen.getByLabelText(/Số điện thoại khẩn cấp/i), {
      target: { value: '0933445566' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Lưu thay đổi/i }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith('/patients/me/profile', {
        phone: '0988112233',
        address: '99 Nguyễn Huệ, Quận 1, TP.HCM',
        bloodType: 'AB+',
        emergencyContact: {
          fullName: 'Trần Thị Mai',
          relationship: 'Vợ',
          phone: '0933445566',
        },
      });
    });

    expect(await screen.findByText('0988112233')).toBeInTheDocument();
    expect(screen.getByText('99 Nguyễn Huệ, Quận 1, TP.HCM')).toBeInTheDocument();
    expect(screen.getByText(/Tự khai báo/i)).toBeInTheDocument();
    expect(screen.getByText(/Trần Thị Mai/i)).toBeInTheDocument();
  });

  it('hiển thị trạng thái lỗi minh bạch khi API thất bại thay vì trả dữ liệu giả', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('Phiên làm việc hết hạn'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Không thể tải thông tin hồ sơ bệnh nhân',
    );
  });
});
