import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { apiClient } from '@/shared/lib/axios';

import { DIAGNOSTIC_SERVICES_MOCK } from '../data/diagnostic-services-data';
import { DiagnosticServiceListPage } from '../pages/diagnostic-service-list-page';
import type {
  DiagnosticServiceFormOptions,
  DiagnosticServiceItem,
} from '../types/diagnostic-service.types';

const FORM_OPTIONS: DiagnosticServiceFormOptions = {
  hospitalId: 1,
  departments: [
    { id: 4, departmentCode: 'DEP-LAB', name: 'Khoa Xét nghiệm' },
    { id: 5, departmentCode: 'DEP-IMG', name: 'Khoa Chẩn đoán hình ảnh' },
  ],
  specialties: [{ id: 3, specialtyCode: 'SPC-HEM', name: 'Huyết học - Truyền máu' }],
};

function renderPage(explicitPermissions?: readonly string[]) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DiagnosticServiceListPage explicitPermissions={explicitPermissions} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function savedItem(overrides: Partial<DiagnosticServiceItem> = {}): DiagnosticServiceItem {
  return {
    ...DIAGNOSTIC_SERVICES_MOCK.data[0],
    id: 77,
    serviceCode: 'DIA-CT-01',
    name: 'CT mới',
    description: null,
    departmentId: 4,
    specialtyId: null,
    requiresResultVerification: true,
    price: 350000,
    priceFormatted: '350.000 đ',
    status: 'ACTIVE',
    isActive: true,
    ...overrides,
  };
}

describe('DiagnosticServiceListPage manage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    useAuthStore.getState().setAuth({
      user: {
        id: 11,
        email: 'hospital_admin01@medflow.test',
        fullName: 'TS. BS Vũ Quốc Tuấn',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
        staffCode: 'STF-009',
      },
      accessToken: 'jwt-hospital-admin-token',
      refreshToken: 'jwt-refresh-token',
    });
    vi.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
      if (String(url).includes('form-options')) return { data: FORM_OPTIONS };
      return { data: DIAGNOSTIC_SERVICES_MOCK };
    });
  });

  it('tạo dịch vụ và hiện notice', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: savedItem() });
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Thêm dịch vụ' }));
    await screen.findByRole('option', { name: 'Khoa Xét nghiệm' });
    fireEvent.change(screen.getByLabelText('Mã dịch vụ'), { target: { value: 'dia-ct-01' } });
    fireEvent.change(screen.getByLabelText('Tên dịch vụ'), { target: { value: 'CT mới' } });
    fireEvent.change(screen.getByLabelText('Khoa thực hiện'), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText('Đơn giá (đ)'), { target: { value: '350000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu dịch vụ' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/admin/diagnostic-services', {
        serviceCode: 'DIA-CT-01',
        name: 'CT mới',
        description: null,
        departmentId: 4,
        specialtyId: null,
        requiresResultVerification: true,
        price: 350000,
      });
    });
    expect(await screen.findByText('Đã tạo dịch vụ DIA-CT-01.')).toBeInTheDocument();
  });

  it('chặn mã có dấu và không gọi post', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValue({ data: savedItem() });
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Thêm dịch vụ' }));
    fireEvent.change(screen.getByLabelText('Mã dịch vụ'), { target: { value: 'có dấu' } });
    fireEvent.change(screen.getByLabelText('Tên dịch vụ'), { target: { value: 'Sai mã' } });
    fireEvent.change(screen.getByLabelText('Khoa thực hiện'), { target: { value: '4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu dịch vụ' }));

    expect(
      await screen.findByText('Mã dịch vụ chỉ gồm chữ in hoa, số và các ký tự - _ .'),
    ).toBeInTheDocument();
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('hiện detail 409 và giữ modal tạo', async () => {
    vi.spyOn(apiClient, 'post').mockRejectedValue(
      new AxiosError('Conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
        status: 409,
        data: { detail: "Mã dịch vụ 'DIA-CBC' đã tồn tại trong cơ sở." },
      } as AxiosResponse),
    );
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Thêm dịch vụ' }));
    await screen.findByRole('option', { name: 'Khoa Xét nghiệm' });
    fireEvent.change(screen.getByLabelText('Mã dịch vụ'), { target: { value: 'DIA-CBC' } });
    fireEvent.change(screen.getByLabelText('Tên dịch vụ'), { target: { value: 'Trùng' } });
    fireEvent.change(screen.getByLabelText('Khoa thực hiện'), { target: { value: '4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu dịch vụ' }));

    expect(
      await screen.findByText("Mã dịch vụ 'DIA-CBC' đã tồn tại trong cơ sở."),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Thêm dịch vụ cận lâm sàng' })).toBeInTheDocument();
  });

  it('sửa tên và không gửi mã hoặc giá', async () => {
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValue({
      data: savedItem({ id: 5, serviceCode: 'DIA-CBC', name: 'Công thức máu mới' }),
    });
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Sửa dịch vụ DIA-CBC' }));
    const codeInput = screen.getByLabelText('Mã dịch vụ');
    expect(codeInput).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Tên dịch vụ'), {
      target: { value: 'Công thức máu mới' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu dịch vụ' }));

    await waitFor(() => {
      expect(putSpy).toHaveBeenCalledWith('/admin/diagnostic-services/5', {
        name: 'Công thức máu mới',
        description: 'Công thức máu (dịch vụ thử nghiệm)',
        departmentId: 4,
        specialtyId: 3,
        requiresResultVerification: true,
      });
    });
    const payload = putSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(payload).not.toHaveProperty('serviceCode');
    expect(payload).not.toHaveProperty('price');
  });

  it('ngừng hoạt động: hiện 409 rồi thành công', async () => {
    const patchSpy = vi
      .spyOn(apiClient, 'patch')
      .mockRejectedValueOnce(
        new AxiosError('Conflict', 'ERR_BAD_REQUEST', undefined, undefined, {
          status: 409,
          data: {
            detail:
              'Không thể ngừng hoạt động: còn 2 phiếu chỉ định cận lâm sàng chưa hoàn tất sử dụng dịch vụ này.',
          },
        } as AxiosResponse),
      )
      .mockResolvedValueOnce({
        data: savedItem({ id: 5, serviceCode: 'DIA-CBC', status: 'INACTIVE', isActive: false }),
      });
    renderPage();

    fireEvent.click(await screen.findByRole('button', { name: 'Ngừng hoạt động dịch vụ DIA-CBC' }));
    fireEvent.change(screen.getByLabelText('Lý do ngừng hoạt động'), {
      target: { value: 'Ngừng vì trùng danh mục' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận ngừng hoạt động' }));

    expect(
      await screen.findByText(
        'Không thể ngừng hoạt động: còn 2 phiếu chỉ định cận lâm sàng chưa hoàn tất sử dụng dịch vụ này.',
      ),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận ngừng hoạt động' }));

    expect(await screen.findByText('Đã ngừng hoạt động dịch vụ DIA-CBC.')).toBeInTheDocument();
    expect(patchSpy).toHaveBeenLastCalledWith('/admin/diagnostic-services/5/deactivate', {
      reason: 'Ngừng vì trùng danh mục',
    });
  });

  it('ẩn thao tác khi không có HOSPITAL_CONFIG', async () => {
    renderPage(['QUEUE_MANAGE']);

    await screen.findByText('DIA-CBC');
    expect(screen.queryByRole('button', { name: 'Thêm dịch vụ' })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Thao tác' })).not.toBeInTheDocument();
  });

  it('không hiện nút ngừng hoạt động cho dịch vụ INACTIVE', async () => {
    renderPage();

    await screen.findByText('DIA-HP');
    expect(
      screen.queryByRole('button', { name: 'Ngừng hoạt động dịch vụ DIA-HP' }),
    ).not.toBeInTheDocument();
  });
});
