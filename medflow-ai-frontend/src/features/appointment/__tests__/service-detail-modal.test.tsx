import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ServiceDetailModal } from '../components/service-detail-modal';
import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';

const mockService = MOCK_EXAMINATION_SERVICES[0]; // Khám Chuyên khoa Tiêu Hóa

describe('ServiceDetailModal (SCR-APT-03-DETAIL)', () => {
  it('renders nothing when isOpen is false', () => {
    const handleClose = vi.fn();
    const handleSelect = vi.fn();

    render(
      <ServiceDetailModal
        service={mockService}
        isOpen={false}
        onClose={handleClose}
        onSelectService={handleSelect}
      />,
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renders complete service detail modal matching SCR-APT-03-DETAIL design', () => {
    const handleClose = vi.fn();
    const handleSelect = vi.fn();

    render(
      <ServiceDetailModal
        service={mockService}
        isOpen={true}
        onClose={handleClose}
        onSelectService={handleSelect}
      />,
    );

    // Modal Dialog & Header
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Thông tin Chi tiết Dịch vụ Khám')).toBeInTheDocument();
    expect(
      screen.getByText('Thông tin niêm yết, chỉ định và quy trình tiếp đón ngoại trú tự nguyện'),
    ).toBeInTheDocument();

    // Hero banner
    expect(screen.getByText('Đang mở nhận lịch trực tuyến')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText('Khoa Tiêu Hóa - Gan Mật')).toBeInTheDocument();
    expect(screen.getByText('Khu Khám Ngoại trú - Tầng 2, Nhà A')).toBeInTheDocument();
    expect(
      screen.getByText('25 phút thăm khám trực tiếp cùng bác sĩ chuyên khoa'),
    ).toBeInTheDocument();
    expect(screen.getByText('GIÁ KHÁM NIÊM YẾT')).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();

    // Section 1: Purpose & Target audience
    expect(screen.getByText('MỤC ĐÍCH KHÁM & ĐỐI TƯỢNG CHỈ ĐỊNH')).toBeInTheDocument();
    expect(screen.getByText('Đau tức vùng thượng vị')).toBeInTheDocument();
    expect(screen.getByText('trào ngược dạ dày thực quản (GERD)')).toBeInTheDocument();
    expect(
      screen.getByText(/Phù hợp cho mọi người bệnh có nhu cầu khám trực tiếp/),
    ).toBeInTheDocument();

    // Section 2: Standard Steps (4 steps)
    expect(screen.getByText('QUY TRÌNH THĂM KHÁM TIÊU CHUẨN (4 BƯỚC)')).toBeInTheDocument();
    expect(screen.getByText('Tiếp đón & Đo sinh hiệu')).toBeInTheDocument();
    expect(screen.getByText('Khám lâm sàng chuyên khoa')).toBeInTheDocument();
    expect(screen.getByText('Chỉ định cận lâm sàng')).toBeInTheDocument();
    expect(screen.getByText('Kết luận & Đơn thuốc số')).toBeInTheDocument();

    // Footer note & buttons
    expect(screen.getByText('Đặt lịch theo khung giờ do cơ sở khai báo.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Đóng / Quay lại danh mục' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Xem lịch & Đặt dịch vụ này/ })).toBeInTheDocument();
  });

  it('triggers onClose when clicking close button or header X', () => {
    const handleClose = vi.fn();
    const handleSelect = vi.fn();

    render(
      <ServiceDetailModal
        service={mockService}
        isOpen={true}
        onClose={handleClose}
        onSelectService={handleSelect}
      />,
    );

    // Click header X button
    const closeXBtn = screen.getByRole('button', { name: 'Đóng cửa sổ' });
    fireEvent.click(closeXBtn);
    expect(handleClose).toHaveBeenCalledTimes(1);

    // Click footer close button
    const footerCloseBtn = screen.getByRole('button', { name: 'Đóng / Quay lại danh mục' });
    fireEvent.click(footerCloseBtn);
    expect(handleClose).toHaveBeenCalledTimes(2);
  });

  it('triggers onSelectService when clicking booking action button', () => {
    const handleClose = vi.fn();
    const handleSelect = vi.fn();

    render(
      <ServiceDetailModal
        service={mockService}
        isOpen={true}
        onClose={handleClose}
        onSelectService={handleSelect}
      />,
    );

    const bookingBtn = screen.getByRole('button', { name: /Xem lịch & Đặt dịch vụ này/ });
    fireEvent.click(bookingBtn);

    expect(handleSelect).toHaveBeenCalledWith(mockService);
    expect(handleClose).toHaveBeenCalledTimes(1);
  });
});
