import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ServiceDeactivateModal } from '../components/service-deactivate-modal';
import type { ExaminationService } from '../types/examination-service.types';

const mockService: ExaminationService = {
  id: 'EX-1-01',
  code: 'EX-1-01',
  name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe',
  description: 'Đánh giá sức khỏe ban đầu, chỉ định CLS cơ bản',
  category: 'Khám Lâm sàng',
  department: 'Khoa Khám bệnh',
  price: 300000,
  onlineBooking: true,
  status: 'ACTIVE',
  hasActiveBookings: false,
  activeBookingsCount: 0,
};

describe('ServiceDeactivateModal (SCR-ADM-ESV-05)', () => {
  it('does not render when open is false or service is null', () => {
    const { container: c1 } = render(
      <ServiceDeactivateModal
        service={mockService}
        open={false}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    expect(c1.firstChild).toBeNull();

    const { container: c2 } = render(
      <ServiceDeactivateModal service={null} open={true} onClose={vi.fn()} onConfirm={vi.fn()} />,
    );
    expect(c2.firstChild).toBeNull();
  });

  it('renders modal with title, service identity, and impact description', () => {
    render(
      <ServiceDeactivateModal
        service={mockService}
        open={true}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(
      screen.getByRole('heading', { name: 'Ngừng hoạt động dịch vụ khám?' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Khám Nội Tổng quát & Tư vấn Sức khỏe/)).toBeInTheDocument();
    expect(screen.getByText('EX-1-01')).toBeInTheDocument();
    expect(screen.getByText(/Khoa Khám bệnh • Khám Lâm sàng/)).toBeInTheDocument();
    expect(
      screen.getByText(
        /Dịch vụ sẽ không thể đặt lịch khám mới trên Cổng thông tin người bệnh hoặc tiếp nhận tại quầy/i,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('Trước khi xác nhận')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc lịch tương lai chưa được giải quyết.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /Xóa cứng không áp dụng: hồ sơ danh mục và mọi liên kết lịch sử được bảo toàn/i,
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Giữ dịch vụ' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeInTheDocument();
  });

  it('calls onClose when "Giữ dịch vụ" button is clicked', () => {
    const onClose = vi.fn();
    render(
      <ServiceDeactivateModal
        service={mockService}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Giữ dịch vụ' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onConfirm with service ID when "Ngừng hoạt động" button is clicked', async () => {
    const onConfirm = vi.fn();
    render(
      <ServiceDeactivateModal
        service={mockService}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));
    expect(onConfirm).toHaveBeenCalledWith('EX-1-01');
  });

  it('disables confirmation button and shows warning when service has active bookings', () => {
    const blockedService: ExaminationService = {
      ...mockService,
      hasActiveBookings: true,
      activeBookingsCount: 5,
    };
    const onConfirm = vi.fn();

    render(
      <ServiceDeactivateModal
        service={blockedService}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    const confirmButton = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmButton).toBeDisabled();
    expect(
      screen.getByText(
        /Không thể ngừng hoạt động khi còn 5 lịch hẹn khám hoặc ca tiếp nhận đang chờ xử lý\./i,
      ),
    ).toBeInTheDocument();

    fireEvent.click(confirmButton);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('closes on Escape key press', () => {
    const onClose = vi.fn();
    render(
      <ServiceDeactivateModal
        service={mockService}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalled();
  });
});
