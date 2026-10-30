import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { INITIAL_EXAMINATION_SLOTS } from '../data/examination-slots-mock';
import { ExaminationSlotCreatePage } from '../pages/examination-slot-create-page';
import { useExaminationSlotStore } from '../store/examination-slot.store';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

describe('ExaminationSlotCreatePage (SCR-ADM-SLT-02)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useExaminationSlotStore.setState({
      slots: [...INITIAL_EXAMINATION_SLOTS],
      draft: null,
    });
  });

  it('renders header, subheader, breadcrumbs, and operational banner matching Figma specs', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    // Breadcrumb and subheader
    expect(screen.getByText('← Quay lại Danh sách Slot khám (SCR-ADM-SLT-01)')).toBeInTheDocument();
    expect(screen.getByText(/Tạo mới \(Draft Mode\)/i)).toBeInTheDocument();
    expect(screen.getByText('Mã màn hình: SCR-ADM-SLT-02')).toBeInTheDocument();
    expect(screen.getByText('Thay đổi chưa lưu')).toBeInTheDocument();

    // Heading
    expect(
      screen.getByRole('heading', { level: 1, name: 'Tạo khung giờ khám' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Gán dịch vụ, bác sĩ, thời gian, phòng và sức chứa cho slot.'),
    ).toBeInTheDocument();

    // Operational Notice Banner
    expect(screen.getByText('Lưu ý vận hành:')).toBeInTheDocument();
    expect(
      screen.getByText(/Mở slot khi dịch vụ hợp lệ, giờ kết thúc sau giờ bắt đầu/i),
    ).toBeInTheDocument();
  });

  it('renders Form Card 1 (Main info & scope) and Card 2 (Operational config)', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    // Card 1
    expect(screen.getByText('1. Thông tin chính & Phạm vi')).toBeInTheDocument();
    expect(screen.getByText('Mã slot do hệ thống sinh')).toBeInTheDocument();
    expect(screen.getByDisplayValue('SLT-HN-20261027-01')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Khám ngoại trú' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Kỹ thuật' })).toBeInTheDocument();

    // Card 2
    expect(screen.getByText('2. Cấu hình vận hành')).toBeInTheDocument();
    expect(screen.getByText('Theo quy trình cơ sở')).toBeInTheDocument();
    expect(screen.getByText('Giá trị / số lượng')).toBeInTheDocument();
    expect(screen.getByText('Không thể giảm sức chứa dưới số lượt đã đặt.')).toBeInTheDocument();
    expect(
      screen.getByText('Xung đột được kiểm tra theo bác sĩ, phòng và thời gian.'),
    ).toBeInTheDocument();
  });

  it('renders Sidebar Card 3 (Resource verification) and Card 4 (Capacity & slot closing)', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    // Card 3
    expect(screen.getByText('Xác nhận tài nguyên')).toBeInTheDocument();
    expect(screen.getByText('Quản lý bởi · UC53')).toBeInTheDocument();
    expect(screen.getByText('Điều kiện mở slot')).toBeInTheDocument();
    expect(screen.getByText('Bác sĩ · nếu phân công')).toBeInTheDocument();
    expect(screen.getByText('Dịch vụ · được phép đặt')).toBeInTheDocument();
    expect(screen.getByText('Phòng · nếu phân công, không trùng lịch')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Kiểm tra xung đột' })).toBeInTheDocument();

    // Card 4
    expect(screen.getByText('Sức chứa & đóng slot')).toBeInTheDocument();
    expect(screen.getByText('Kiểm soát thay đổi')).toBeInTheDocument();
    expect(screen.getByText('Không giảm dưới lượt đã đặt')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Xem lượt đã đặt' })).toBeInTheDocument();
  });

  it('renders bottom fixed action bar with cancel, save draft, and submit buttons', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    expect(
      screen.getByText('Đóng slot chỉ ngăn lượt đặt mới; lịch đã đặt được giữ nguyên.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Hủy bỏ/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lưu bản nháp' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lưu khung giờ' })).toBeInTheDocument();
  });

  it('switches service type between Outpatient and Diagnostic', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const techBtn = screen.getByRole('button', { name: 'Kỹ thuật' });
    fireEvent.click(techBtn);

    // Option should now contain Diagnostic procedures
    expect(screen.getByText(/Chụp X-quang Ngực thẳng/i)).toBeInTheDocument();
  });

  it('updates capacity when clicking quick preset buttons', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const capacityInput = screen.getByLabelText(/Sức chứa slot/i) as HTMLInputElement;
    expect(capacityInput.value).toBe('8');

    const tenSlotBtn = screen.getByRole('button', { name: '10 lượt' });
    fireEvent.click(tenSlotBtn);

    expect(capacityInput.value).toBe('10');
  });

  it('detects doctor schedule conflict when doctor already has slot at overlapping time', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    // Default form has BS. Lan (doc-001) at 08:00-08:20 on 2026-10-27
    // slt-001 is already registered for doc-001 at 08:00-08:30 on 2026-10-27
    const checkBtn = screen.getByRole('button', { name: 'Kiểm tra xung đột' });
    fireEvent.click(checkBtn);

    expect(
      screen.getByText(/Xung đột lịch bác sĩ: BS. Nguyễn Thu Lan đã có slot/i),
    ).toBeInTheDocument();
  });

  it('confirms no conflict when time is set to an open non-overlapping window', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const startTimeInput = screen.getByLabelText('Giờ bắt đầu');
    const endTimeInput = screen.getByLabelText('Giờ kết thúc');

    // Change to non-conflicting time 11:00-11:20
    fireEvent.change(startTimeInput, { target: { value: '11:00' } });
    fireEvent.change(endTimeInput, { target: { value: '11:20' } });

    const checkBtn = screen.getByRole('button', { name: 'Kiểm tra xung đột' });
    fireEvent.click(checkBtn);

    expect(screen.getByText(/Tài nguyên hợp lệ, không phát hiện xung đột/i)).toBeInTheDocument();
  });

  it('allows manual draft saving into store', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const draftBtn = screen.getByRole('button', { name: 'Lưu bản nháp' });
    fireEvent.click(draftBtn);

    expect(
      screen.getByText('Đã lưu bản nháp khung giờ khám vào bộ nhớ phiên làm việc.'),
    ).toBeInTheDocument();
    expect(useExaminationSlotStore.getState().draft?.code).toBe('SLT-HN-20261027-01');
  });

  it('navigates to /admin/slots when clicking cancel button', () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const cancelBtn = screen.getByRole('button', { name: /Hủy bỏ/i });
    fireEvent.click(cancelBtn);

    expect(mockNavigate).toHaveBeenCalledWith('/admin/slots');
  });

  it('validates time order: shows error if end time is before or equal to start time', async () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const startTimeInput = screen.getByLabelText('Giờ bắt đầu');
    const endTimeInput = screen.getByLabelText('Giờ kết thúc');

    fireEvent.change(startTimeInput, { target: { value: '09:00' } });
    fireEvent.change(endTimeInput, { target: { value: '08:00' } });

    const submitBtn = screen.getByRole('button', { name: 'Lưu khung giờ' });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByText('Giờ kết thúc phải sau giờ bắt đầu')).toBeInTheDocument();
    });
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('submits valid slot form, adds to store, and navigates back to /admin/slots', async () => {
    render(
      <MemoryRouter>
        <ExaminationSlotCreatePage />
      </MemoryRouter>,
    );

    const codeInput = screen.getByLabelText(/Mã slot/i);
    fireEvent.change(codeInput, { target: { value: 'SLT-HN-20261027-99' } });

    const submitBtn = screen.getByRole('button', { name: 'Lưu khung giờ' });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/admin/slots');
    });

    const storeSlots = useExaminationSlotStore.getState().slots;
    const addedSlot = storeSlots.find((s) => s.code === 'SLT-HN-20261027-99');
    expect(addedSlot).toBeDefined();
    expect(addedSlot?.serviceName).toBe('Khám Nội tiết');
    expect(addedSlot?.capacityBooked).toBe(0);
    expect(addedSlot?.status).toBe('OPEN');
  });
});
