import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ServiceCard } from '../components/service-card';
import type { ExaminationService } from '../types/service-search.types';

const mockService: ExaminationService = {
  id: 'srv-th-01',
  serviceCode: 'DV-TH-01',
  name: 'Khám Chuyên khoa Tiêu Hóa',
  departmentId: 'dept-th',
  departmentName: 'Khoa Tiêu Hóa - Gan Mật',
  badgeType: 'online',
  badgeLabel: 'Đặt online nhanh',
  durationMinutes: 25,
  durationDescription: '25 phút thăm khám trực tiếp',
  description: 'Khám chuyên sâu dạ dày, đại tràng với bác sĩ chuyên khoa.',
  tags: ['Bác sĩ CKII', 'Tư vấn chế độ ăn'],
  price: 250000,
  priceFormatted: '250.000 VNĐ',
  priceNote: 'Giá theo cơ sở/ngày khám',
  category: 'specialty',
};

describe('ServiceCard', () => {
  it('renders service information correctly', () => {
    const handleViewDetail = vi.fn();
    const handleSelectService = vi.fn();

    render(
      <ServiceCard
        service={mockService}
        onViewDetail={handleViewDetail}
        onSelectService={handleSelectService}
      />,
    );

    expect(screen.getByText('DV-TH-01 • Khoa Tiêu Hóa - Gan Mật')).toBeInTheDocument();
    expect(screen.getByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText('Đặt online nhanh')).toBeInTheDocument();
    expect(screen.getByText(/25 phút thăm khám trực tiếp/)).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();
    expect(screen.getByText('Giá theo cơ sở/ngày khám')).toBeInTheDocument();
    expect(screen.getByText('Bác sĩ CKII')).toBeInTheDocument();
    expect(screen.getByText('Tư vấn chế độ ăn')).toBeInTheDocument();
  });

  it('calls onViewDetail when clicking Chi tiết dịch vụ', () => {
    const handleViewDetail = vi.fn();
    const handleSelectService = vi.fn();

    render(
      <ServiceCard
        service={mockService}
        onViewDetail={handleViewDetail}
        onSelectService={handleSelectService}
      />,
    );

    const detailBtn = screen.getByRole('button', { name: 'Chi tiết dịch vụ' });
    fireEvent.click(detailBtn);

    expect(handleViewDetail).toHaveBeenCalledWith(mockService);
    expect(handleSelectService).not.toHaveBeenCalled();
  });

  it('calls onSelectService when clicking Chọn dịch vụ', () => {
    const handleViewDetail = vi.fn();
    const handleSelectService = vi.fn();

    render(
      <ServiceCard
        service={mockService}
        onViewDetail={handleViewDetail}
        onSelectService={handleSelectService}
      />,
    );

    const selectBtn = screen.getByRole('button', { name: /Chọn dịch vụ/ });
    fireEvent.click(selectBtn);

    expect(handleSelectService).toHaveBeenCalledWith(mockService);
    expect(handleViewDetail).not.toHaveBeenCalled();
  });
});
