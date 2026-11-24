import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { DOCTOR_CREATE_DRAFT_STORAGE_KEY, DoctorCreatePage } from '../pages/doctor-create-page';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    post: vi.fn(),
  },
}));

describe('DoctorCreatePage (SCR-ADM-DOC-02 real API, no prefilled SAMPLE)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    vi.mocked(apiClient.post).mockResolvedValue({
      data: {
        id: 245,
        doctorCode: 'DOC-0245',
        staffCode: 'STF-0142',
        fullName: 'BS. Nguyễn Văn An',
        isActive: true,
      },
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const renderPage = () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/doctors/new']}>
          <Routes>
            <Route path="/admin/doctors/new" element={<DoctorCreatePage />} />
            <Route path="/admin/doctors" element={<div>Danh sách Bác sĩ</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  };

  it('starts with an empty form and 0 hardcoded history entries when no draft exists', () => {
    renderPage();

    const idInput = screen.getByLabelText(/Mã Bác sĩ HIS Định danh/) as HTMLInputElement;
    expect(idInput.value).toBe('');
    expect(screen.queryByDisplayValue('DOC-0245')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('BS. Phạm Quang Duy')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Năm mốc 1')).not.toBeInTheDocument();
  });

  it('restores form values and work history from sessionStorage draft and submits POST /admin/doctors', async () => {
    window.sessionStorage.setItem(
      DOCTOR_CREATE_DRAFT_STORAGE_KEY,
      JSON.stringify({
        id: 'DOC-0500',
        staffId: 'STF-1500',
        name: 'BS. Bản Nháp',
        email: 'nhap@medflow.vn',
        degree: 'Bác sĩ Chuyên khoa I',
        specialty: 'Nội Tiêu hóa',
        department: 'Khoa Nội Tiêu hóa',
        license: '018492/BYT-CCHN',
        room: 'PK-101 (Phòng Tiêu hóa 1)',
        phone: '02438258899',
        experience: 5,
        scope: 'Khám Nội',
        biography: 'Bác sĩ điều trị',
        active: true,
        history: [{ year: '2020', description: 'Tốt nghiệp ĐHY' }],
      }),
    );

    renderPage();

    expect((screen.getByLabelText(/Mã Bác sĩ HIS Định danh/) as HTMLInputElement).value).toBe(
      'DOC-0500',
    );
    expect((screen.getByLabelText('Năm mốc 1') as HTMLInputElement).value).toBe('2020');

    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/admin/doctors',
        expect.objectContaining({
          doctorCode: 'DOC-0500',
          staffCode: 'STF-1500',
          department: 'Khoa Nội Tiêu hóa',
        }),
      );
      expect(screen.getByText('Danh sách Bác sĩ')).toBeInTheDocument();
    });
    expect(window.sessionStorage.getItem(DOCTOR_CREATE_DRAFT_STORAGE_KEY)).toBeNull();
  });

  it('surfaces backend error message when POST /admin/doctors rejects and validates bad inputs', async () => {
    renderPage();

    // Validation on empty/invalid form
    fireEvent.change(screen.getByLabelText(/Mã Bác sĩ HIS Định danh/), {
      target: { value: 'DOC-12' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Mã bác sĩ có dạng DOC-0000')).toBeInTheDocument();
      expect(screen.getByText('Vui lòng chọn khoa công tác')).toBeInTheDocument();
    });
    expect(apiClient.post).not.toHaveBeenCalled();

    // Valid form hitting backend 409 error
    vi.mocked(apiClient.post).mockRejectedValueOnce(
      new Error('Mã bác sĩ DOC-0142 đã tồn tại trên hệ thống.'),
    );

    fireEvent.change(screen.getByLabelText(/Mã Bác sĩ HIS Định danh/), {
      target: { value: 'DOC-0142' },
    });
    fireEvent.change(screen.getByLabelText(/Mã Nhân sự liên kết/), {
      target: { value: 'STF-0142' },
    });
    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), {
      target: { value: 'BS. Nguyễn Văn An' },
    });
    fireEvent.change(screen.getByLabelText(/Email tiếp nhận ca khám/), {
      target: { value: 'an.nv@medflow.vn' },
    });
    fireEvent.change(screen.getByLabelText(/Khoa công tác trực thuộc/), {
      target: { value: 'Khoa Nội Tiêu hóa' },
    });
    fireEvent.change(screen.getByLabelText(/Buồng khám mặc định/), {
      target: { value: 'PK-101 (Phòng Tiêu hóa 1)' },
    });
    fireEvent.change(screen.getByLabelText(/Học hàm \/ Học vị/), {
      target: { value: 'Bác sĩ Chuyên khoa I' },
    });
    fireEvent.change(screen.getByLabelText(/Chuyên khoa Chuyên sâu/), {
      target: { value: 'Nội Tiêu hóa' },
    });
    fireEvent.change(screen.getByLabelText(/Số Chứng chỉ Hành nghề CCHN/), {
      target: { value: '018492/BYT-CCHN' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Mã bác sĩ DOC-0142 đã tồn tại trên hệ thống.',
      );
    });
  });

  it('manages portrait preview, work history milestones, manual/auto draft, and Escape navigation', async () => {
    vi.useFakeTimers();
    const { container, unmount } = renderPage();

    // Auto-save after 30s when dirty
    fireEvent.change(screen.getByLabelText(/Chuyên khoa Chuyên sâu/), {
      target: { value: 'Tiêu hóa Test' },
    });
    act(() => {
      vi.advanceTimersByTime(30_000);
    });
    expect(
      JSON.parse(window.sessionStorage.getItem(DOCTOR_CREATE_DRAFT_STORAGE_KEY) || '{}').specialty,
    ).toBe('Tiêu hóa Test');

    // Add and remove milestone
    fireEvent.click(screen.getByRole('button', { name: '⊕ Thêm mốc đào tạo' }));
    expect(screen.getByLabelText('Năm mốc 1')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Xóa mốc 1' }));
    expect(screen.queryByLabelText('Năm mốc 1')).not.toBeInTheDocument();

    // Manual draft save
    fireEvent.click(screen.getByRole('button', { name: 'Lưu bản nháp' }));
    expect(screen.getByRole('status')).toHaveTextContent('Đã lưu bản nháp trong phiên hiện tại.');

    // Portrait file validation
    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(fileInput, {
      target: { files: [new File(['x'], 'a.gif', { type: 'image/gif' })] },
    });
    expect(screen.getByText('Chọn ảnh JPG/PNG tối đa 2MB.')).toBeInTheDocument();

    // Escape navigates back
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh sách Bác sĩ')).toBeInTheDocument();
    unmount();
  });
});
