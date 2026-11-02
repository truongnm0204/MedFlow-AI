import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { INITIAL_DOCTORS } from '../data/doctor-demo-data';
import { DoctorEditModal } from '../pages/doctor-edit-modal';
import { useDoctorDemoStore } from '../store/doctor-demo.store';

describe('DoctorEditModal (SCR-ADM-DOC-03)', () => {
  beforeEach(() => {
    useDoctorDemoStore.setState({
      doctors: [...INITIAL_DOCTORS],
      draft: null,
    });
  });

  const renderModal = (id = 'DOC-0142') =>
    render(
      <MemoryRouter initialEntries={[`/admin/doctors/${id}/edit`]}>
        <Routes>
          <Route path="/admin/doctors/:doctorId/edit" element={<DoctorEditModal />} />
          <Route path="/admin/doctors" element={<div>Danh sách Mock</div>} />
        </Routes>
      </MemoryRouter>,
    );

  it('prefills doctor profile fields and marks practitioner ID as readonly', () => {
    renderModal('DOC-0142');

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Hồ sơ Bác sĩ - BS. CKI Lê Hoàng Minh' }),
    ).toBeInTheDocument();
    expect(screen.getByText('● Active')).toBeInTheDocument();

    const nameInput = screen.getByLabelText(/Họ và tên Bác sĩ/) as HTMLInputElement;
    expect(nameInput.value).toBe('BS. CKI Lê Hoàng Minh');

    const idInput = screen.getByLabelText(/Mã Bác sĩ \(Practitioner ID\)/) as HTMLInputElement;
    expect(idInput.value).toBe('DOC-0142');
    expect(idInput).toHaveAttribute('readonly');

    const deptSelect = screen.getByLabelText(/Khoa công tác trực thuộc/) as HTMLSelectElement;
    expect(deptSelect.value).toBe('Khoa Nội Tiêu hóa');
  });

  it('renders not-found view for unknown doctor ID and navigates back on click', () => {
    renderModal('DOC-9999');

    expect(screen.getByText('Không tìm thấy hồ sơ bác sĩ minh họa.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Quay lại danh sách' }));
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
  });

  it('saves updated fields while preserving out-of-form properties (history, portrait)', async () => {
    renderModal('DOC-0142');

    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), {
      target: { value: 'BS. CKI Lê Hoàng Minh (Cập nhật)' },
    });
    fireEvent.change(screen.getByLabelText(/Buồng khám chỉ định mặc định/), {
      target: { value: 'PK-999' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });

    const updated = useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142');
    expect(updated?.name).toBe('BS. CKI Lê Hoàng Minh (Cập nhật)');
    expect(updated?.room).toBe('PK-999');
    expect(updated?.history).toHaveLength(2);
    expect(updated?.portrait).toBe('/assets/admin-doctors/list-02.png');
  });

  it('blocks save and shows validation messages when inputs are invalid', async () => {
    renderModal('DOC-0142');

    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), {
      target: { value: 'A' },
    });
    fireEvent.change(screen.getByLabelText(/Chứng chỉ hành nghề/), {
      target: { value: '123' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Vui lòng nhập họ tên')).toBeInTheDocument();
      expect(screen.getByText('Vui lòng nhập số chứng chỉ hành nghề')).toBeInTheDocument();
    });

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    const current = useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142');
    expect(current?.name).toBe('BS. CKI Lê Hoàng Minh');
  });

  it('stages suspend status in form and persists only on save', async () => {
    renderModal('DOC-0142');

    fireEvent.click(screen.getByRole('button', { name: '⊖ Tạm ngừng nhận lịch' }));

    expect(screen.getByText('● Tạm ngừng')).toBeInTheDocument();
    const activeCheckbox = screen.getByRole('checkbox', {
      name: /Đang nhận lịch khám/,
    }) as HTMLInputElement;
    expect(activeCheckbox.checked).toBe(false);

    expect(useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142')?.active).toBe(
      true,
    );

    fireEvent.click(screen.getByRole('button', { name: /Lưu hồ sơ Bác sĩ/ }));

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });
    expect(useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142')?.active).toBe(
      false,
    );
  });

  it('closes on Escape key, close button, or backdrop mouseDown, but not dialog mouseDown', () => {
    const first = renderModal('DOC-0142');
    fireEvent.mouseDown(screen.getByRole('dialog'));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    first.unmount();

    const second = renderModal('DOC-0142');
    fireEvent.click(screen.getByRole('button', { name: 'Đóng chỉnh sửa' }));
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    second.unmount();

    renderModal('DOC-0142');
    fireEvent.mouseDown(screen.getByRole('dialog').parentElement!);
    expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
  });

  it('submits changes via Ctrl+S shortcut', async () => {
    renderModal('DOC-0142');

    fireEvent.change(screen.getByLabelText(/Họ và tên Bác sĩ/), {
      target: { value: 'BS. Lê Hoàng Minh Shortcut' },
    });
    fireEvent.keyDown(window, { key: 's', ctrlKey: true });

    await waitFor(() => {
      expect(screen.getByText('Danh sách Mock')).toBeInTheDocument();
    });
    expect(useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142')?.name).toBe(
      'BS. Lê Hoàng Minh Shortcut',
    );
  });

  it('displays SSO notice status when clicking Đổi button', () => {
    renderModal('DOC-0142');

    fireEvent.click(screen.getByRole('button', { name: 'Đổi' }));

    expect(screen.getByRole('status')).toHaveTextContent(
      'Liên kết SSO chỉ thay đổi tại danh mục Nhân sự.',
    );
  });
});
