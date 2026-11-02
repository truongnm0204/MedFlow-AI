import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { INITIAL_DOCTORS } from '../data/doctor-demo-data';
import { DoctorCreatePage } from '../pages/doctor-create-page';
import { useDoctorDemoStore } from '../store/doctor-demo.store';

describe('DoctorCreatePage (SCR-ADM-DOC-02)', () => {
  beforeEach(() => {
    useDoctorDemoStore.setState({
      doctors: [...INITIAL_DOCTORS],
      draft: null,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const renderPage = () =>
    render(
      <MemoryRouter initialEntries={['/admin/doctors/new']}>
        <Routes>
          <Route path="/admin/doctors/new" element={<DoctorCreatePage />} />
          <Route path="/admin/doctors" element={<div>Danh sách Mock</div>} />
        </Routes>
      </MemoryRouter>,
    );

  it('populates default sample values and 3 work history milestones when draft is null', () => {
    renderPage();

    const idInput = screen.getByLabelText(/Mã Bác sĩ HIS Định danh/) as HTMLInputElement;
    expect(idInput.value).toBe('DOC-0245');

    const milestone3 = screen.getByLabelText('Năm mốc 3') as HTMLInputElement;
    expect(milestone3.value).toBe('2018 - 2024');
  });

  it('restores form values and work history from saved draft', () => {
    useDoctorDemoStore.setState({
      draft: {
        ...INITIAL_DOCTORS[0],
        id: 'DOC-0500',
        staffId: 'NV-1500',
        name: 'BS. Bản Nháp',
        history: [{ year: '2020', description: 'Mốc nháp' }],
      },
    });

    renderPage();

    const idInput = screen.getByLabelText(/Mã Bác sĩ HIS Định danh/) as HTMLInputElement;
    expect(idInput.value).toBe('DOC-0500');

    const milestone1 = screen.getByLabelText('Năm mốc 1') as HTMLInputElement;
    expect(milestone1.value).toBe('2020');
    expect(screen.queryByLabelText('Năm mốc 2')).not.toBeInTheDocument();
  });

  it('creates a new doctor, prepends to store, clears draft, and navigates back', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });

    const state = useDoctorDemoStore.getState();
    expect(state.doctors).toHaveLength(9);
    expect(state.doctors[0].id).toBe('DOC-0245');
    expect(state.doctors[0].history).toHaveLength(3);
    expect(state.draft).toBeNull();
  });

  it('rejects duplicate practitioner ID', async () => {
    renderPage();

    fireEvent.change(screen.getByLabelText(/Mã Bác sĩ HIS Định danh/), {
      target: { value: 'DOC-0142' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Mã bác sĩ hoặc nhân sự đã được sử dụng trong danh sách minh họa.',
      );
    });
    expect(useDoctorDemoStore.getState().doctors).toHaveLength(8);
    expect(screen.queryByText('Danh sách Mock')).not.toBeInTheDocument();
  });

  it('rejects duplicate staffId loaded via draft', async () => {
    useDoctorDemoStore.setState({
      draft: {
        ...INITIAL_DOCTORS[0],
        id: 'DOC-0600',
        staffId: 'NV-1142',
      },
    });

    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Mã bác sĩ hoặc nhân sự đã được sử dụng trong danh sách minh họa.',
      );
    });
    expect(useDoctorDemoStore.getState().doctors).toHaveLength(8);
  });

  it('validates practitioner ID format and required department', async () => {
    renderPage();

    fireEvent.change(screen.getByLabelText(/Mã Bác sĩ HIS Định danh/), {
      target: { value: 'DOC-12' },
    });
    fireEvent.change(screen.getByLabelText(/Khoa công tác trực thuộc/), {
      target: { value: '' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Mã bác sĩ có dạng DOC-0000')).toBeInTheDocument();
      expect(screen.getByText('Vui lòng chọn khoa công tác')).toBeInTheDocument();
    });
    expect(useDoctorDemoStore.getState().doctors).toHaveLength(8);
  });

  it('validates portrait upload type and size and previews valid PNG', async () => {
    const { container } = renderPage();
    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;

    fireEvent.change(fileInput, {
      target: { files: [new File(['x'], 'a.gif', { type: 'image/gif' })] },
    });
    expect(screen.getByText('Chọn ảnh JPG/PNG tối đa 2MB.')).toBeInTheDocument();

    fireEvent.change(fileInput, {
      target: { files: [new File([new Uint8Array(2_000_001)], 'big.png', { type: 'image/png' })] },
    });
    expect(screen.getByText('Chọn ảnh JPG/PNG tối đa 2MB.')).toBeInTheDocument();

    fireEvent.change(fileInput, {
      target: { files: [new File(['x'], 'ok.png', { type: 'image/png' })] },
    });
    expect(await screen.findByAltText('Ảnh chân dung đã chọn')).toBeInTheDocument();
  });

  it('removes a work history entry before creating doctor', async () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Xóa mốc 1' }));
    fireEvent.click(screen.getByRole('button', { name: /Tạo Hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });

    const created = useDoctorDemoStore.getState().doctors[0];
    expect(created.history).toHaveLength(2);
    expect(created.history[0].year).toBe('2017');
  });

  it('adds a new work history entry when clicking ⊕ Thêm mốc đào tạo', () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: '⊕ Thêm mốc đào tạo' }));

    expect(screen.getByLabelText('Năm mốc 4')).toBeInTheDocument();
  });

  it('saves draft manually when clicking Lưu bản nháp', () => {
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Lưu bản nháp' }));

    expect(screen.getByRole('status')).toHaveTextContent('Đã lưu bản nháp trong phiên hiện tại.');
    const { draft } = useDoctorDemoStore.getState();
    expect(draft?.id).toBe('DOC-0245');
    expect(draft?.history).toHaveLength(3);
  });

  it('auto-saves draft after 30s only when form is dirty', () => {
    vi.useFakeTimers();

    const first = renderPage();
    act(() => {
      vi.advanceTimersByTime(30_000);
    });
    expect(useDoctorDemoStore.getState().draft).toBeNull();
    first.unmount();

    renderPage();
    fireEvent.change(screen.getByLabelText(/Chuyên khoa Chuyên sâu/), {
      target: { value: 'Tiêu hóa Test' },
    });

    act(() => {
      vi.advanceTimersByTime(30_000);
    });

    expect(useDoctorDemoStore.getState().draft?.specialty).toBe('Tiêu hóa Test');
  });

  it('navigates back on Escape key and Hủy bỏ [Esc] button', () => {
    const first = renderPage();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    first.unmount();

    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ [Esc]' }));
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
  });

  it('submits form via Ctrl+S shortcut', async () => {
    renderPage();

    fireEvent.keyDown(window, { key: 's', ctrlKey: true });

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });
    expect(useDoctorDemoStore.getState().doctors).toHaveLength(9);
  });
});
