import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DoctorDetailModal } from '../components/doctor-detail-modal';
import { useBookingStore } from '../store/booking.store';

function renderDoctorDetailModal({
  doctorId = 'doc-01',
  isOpen = true,
  onClose = vi.fn(),
}: {
  doctorId?: string;
  isOpen?: boolean;
  onClose?: () => void;
}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <DoctorDetailModal doctorId={doctorId} isOpen={isOpen} onClose={onClose} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorDetailModal (SCR-APT-01-DETAIL)', () => {
  beforeEach(() => {
    useBookingStore.getState().reset();
  });

  it('renders modal header, summary info, education, experience and weekly schedule', async () => {
    renderDoctorDetailModal({ doctorId: 'doc-01' });

    // 1. Header
    expect(screen.getByText('Hồ sơ Chuyên môn Bác sĩ')).toBeInTheDocument();
    expect(screen.getByText(/lĩnh vực chuyên sâu & lịch thăm khám/i)).toBeInTheDocument();

    // 2. Summary Info
    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(
      screen.getByText(/Bác sĩ Trưởng kíp khám - Khoa Tiêu Hóa - Gan Mật/),
    ).toBeInTheDocument();
    expect(screen.getByText('Phòng 204 - Tầng 2, Dãy B')).toBeInTheDocument();
    expect(screen.getByText('Đang tiếp nhận lịch khám ngoại trú')).toBeInTheDocument();
    expect(screen.getByText('250.000 VNĐ')).toBeInTheDocument();

    // 3. Education & Credentials
    expect(screen.getByText('QUÁ TRÌNH ĐÀO TẠO & BẰNG CẤP')).toBeInTheDocument();
    expect(screen.getByText('Bác sĩ Đa khoa')).toBeInTheDocument();
    expect(screen.getByText('Tốt nghiệp loại Giỏi')).toBeInTheDocument();
    expect(screen.getByText(/Chuyên ngành Nội Tiêu Hóa/)).toBeInTheDocument();
    expect(screen.getByText(/Chứng chỉ Nội soi Tiêu hóa Can thiệp/)).toBeInTheDocument();

    // 4. Clinical Experience & Hashtags
    expect(screen.getByText('KINH NGHIỆM & LĨNH VỰC CHUYÊN SÂU')).toBeInTheDocument();
    expect(screen.getByText(/Hơn 15 năm kinh nghiệm công tác lâm sàng/)).toBeInTheDocument();
    expect(screen.getByText('#NoiSoiTieuHoa')).toBeInTheDocument();
    expect(screen.getByText('#NộiSoiNBI')).toBeInTheDocument();

    // 5. Weekly Schedule
    expect(screen.getByText('LỊCH TRỰC KHÁM TẠI VIỆN TRONG TUẦN')).toBeInTheDocument();
    expect(screen.getByText('Thứ Hai')).toBeInTheDocument();
    expect(screen.getByText('Thứ Tư')).toBeInTheDocument();
    expect(screen.getByText('Thứ Năm')).toBeInTheDocument();
    expect(screen.getAllByText('Đang nhận bệnh').length).toBeGreaterThanOrEqual(2);
  });

  it('calls onClose when clicking Đóng / Quay lại danh bạ or close button', async () => {
    const handleClose = vi.fn();
    renderDoctorDetailModal({ doctorId: 'doc-01', onClose: handleClose });

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const closeBtn = screen.getByRole('button', {
      name: 'Đóng / Quay lại danh bạ',
    });
    fireEvent.click(closeBtn);

    expect(handleClose).toHaveBeenCalledTimes(1);

    const xBtn = screen.getByLabelText('Đóng hồ sơ bác sĩ');
    fireEvent.click(xBtn);

    expect(handleClose).toHaveBeenCalledTimes(2);
  });

  it('closes modal when pressing Escape key', async () => {
    const handleClose = vi.fn();
    renderDoctorDetailModal({ doctorId: 'doc-01', onClose: handleClose });

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    fireEvent.keyDown(window, { key: 'Escape' });

    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('selects doctor in booking store when clicking Xem lịch & Đặt khám ngay', async () => {
    const handleClose = vi.fn();
    renderDoctorDetailModal({ doctorId: 'doc-01', onClose: handleClose });

    expect(await screen.findByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();

    const bookBtn = screen.getByRole('button', {
      name: 'Xem lịch & Đặt khám ngay',
    });
    fireEvent.click(bookBtn);

    expect(useBookingStore.getState().selectedDoctorId).toBe('doc-01');
    expect(handleClose).toHaveBeenCalled();
  });

  it('renders error state when doctor profile is not found', async () => {
    renderDoctorDetailModal({ doctorId: 'non-existent-id' });

    expect(
      await screen.findByText('Không thể tải thông tin hồ sơ bác sĩ. Vui lòng thử lại sau.'),
    ).toBeInTheDocument();
  });
});
