import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import { ExaminationServiceCreatePage } from '../pages/examination-service-create-page';
import { useExaminationServiceStore } from '../store/examination-service.store';

describe('ExaminationServiceCreatePage (SCR-ADM-ESV-02)', () => {
  beforeEach(() => {
    useExaminationServiceStore.setState({
      services: [...INITIAL_SERVICES],
      draft: null,
    });
  });

  const renderComponent = () =>
    render(
      <MemoryRouter initialEntries={['/admin/services/new']}>
        <Routes>
          <Route path="/admin/services/new" element={<ExaminationServiceCreatePage />} />
          <Route path="/admin/services" element={<div>Danh mục Dịch vụ Page Mock</div>} />
        </Routes>
      </MemoryRouter>,
    );

  it('renders title, breadcrumbs, and operational guidance banner', () => {
    renderComponent();

    expect(
      screen.getByRole('heading', { name: /Thêm mới Dịch vụ Khám Ngoại trú/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Thêm mới Dịch vụ Khám (SCR-ADM-ESV-02)')).toBeInTheDocument();
    expect(screen.getByText(/Lưu ý vận hành Khám Ngoại trú:/i)).toBeInTheDocument();
    expect(screen.getByText(/Mã màn hình: SCR-ADM-ESV-02/i)).toBeInTheDocument();
    expect(screen.getByText(/Tạo mới \(Draft Mode\)/i)).toBeInTheDocument();
  });

  it('renders default values matching the Figma specification', () => {
    renderComponent();

    const codeInput = screen.getByPlaceholderText('EX-2-09') as HTMLInputElement;
    expect(codeInput.value).toBe('EX-2-09');

    const nameInput = screen.getByPlaceholderText(
      'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu',
    ) as HTMLInputElement;
    expect(nameInput.value).toBe('Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu');

    expect(screen.getByText(/Khám Lâm sàng \(EXAMINATION\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Cận lâm sàng kỹ thuật \(DIAGNOSTIC\)/i)).toBeInTheDocument();
  });

  it('renders UC52 pricing and UC54 booking policy context cards in sidebar', () => {
    renderComponent();

    expect(screen.getByText('Giá dịch vụ theo cơ sở')).toBeInTheDocument();
    expect(screen.getByText('Chỉ xem - UC52')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Mở bảng giá theo cơ sở/i })).toBeInTheDocument();

    expect(screen.getByText('Chính sách đặt hẹn theo cơ sở')).toBeInTheDocument();
    expect(screen.getByText('Quản lý riêng - UC54')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Mở chính sách đặt lịch/i })).toBeInTheDocument();
  });

  it('allows switching category between Clinical and Diagnostic', () => {
    renderComponent();

    const diagnosticBtn = screen
      .getByText(/Cận lâm sàng kỹ thuật \(DIAGNOSTIC\)/i)
      .closest('button');
    expect(diagnosticBtn).toBeInTheDocument();
    fireEvent.click(diagnosticBtn!);

    expect(diagnosticBtn?.className).toContain('border-primary');
  });

  it('allows changing online booking select option', () => {
    renderComponent();

    const bookingSelect = screen.getByRole('combobox', {
      name: /Cho phép đặt lịch trực tuyến/i,
    }) as HTMLSelectElement;
    expect(bookingSelect.value).toBe('true');

    fireEvent.change(bookingSelect, { target: { value: 'false' } });
    expect(bookingSelect.value).toBe('false');
  });

  it('validates invalid service code pattern on submission', async () => {
    renderComponent();

    const codeInput = screen.getByPlaceholderText('EX-2-09');
    fireEvent.change(codeInput, { target: { value: 'INVALID_CODE' } });

    const submitBtn = screen.getByRole('button', { name: /Tạo dịch vụ nháp/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Mã dịch vụ theo chuẩn EX-x-xx/i)).toBeInTheDocument();
    });
  });

  it('validates empty service name', async () => {
    renderComponent();

    const nameInput = screen.getByPlaceholderText(
      'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật Chuyên sâu',
    );
    fireEvent.change(nameInput, { target: { value: '' } });

    const submitBtn = screen.getByRole('button', { name: /Tạo dịch vụ nháp/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Tên dịch vụ phải có ít nhất 3 ký tự/i)).toBeInTheDocument();
    });
  });

  it('saves draft to store when clicking Lưu bản nháp', () => {
    renderComponent();

    const draftBtn = screen.getByRole('button', { name: /Lưu bản nháp/i });
    fireEvent.click(draftBtn);

    expect(screen.getAllByText(/Đã lưu bản nháp/i).length).toBeGreaterThanOrEqual(1);
    const storedDraft = useExaminationServiceStore.getState().draft;
    expect(storedDraft?.code).toBe('EX-2-09');
  });

  it('navigates back to service list on Escape key', () => {
    renderComponent();

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
  });

  it('successfully creates a new service and redirects to service list', async () => {
    renderComponent();

    const codeInput = screen.getByPlaceholderText('EX-2-09');
    fireEvent.change(codeInput, { target: { value: 'EX-9-99' } });

    const submitBtn = screen.getByRole('button', { name: /Tạo dịch vụ nháp/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText('Danh mục Dịch vụ Page Mock')).toBeInTheDocument();
    });

    const services = useExaminationServiceStore.getState().services;
    expect(services.some((s) => s.code === 'EX-9-99')).toBe(true);
  });

  it('shows error if service code already exists in catalog', async () => {
    renderComponent();

    // EX-1-01 exists in INITIAL_SERVICES
    const codeInput = screen.getByPlaceholderText('EX-2-09');
    fireEvent.change(codeInput, { target: { value: 'EX-1-01' } });

    const submitBtn = screen.getByRole('button', { name: /Tạo dịch vụ nháp/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText(/Mã dịch vụ "EX-1-01" đã tồn tại/i)).toBeInTheDocument();
    });
  });
});
