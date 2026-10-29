import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { AppointmentCard } from '../components/appointment-card';
import { MOCK_APPOINTMENTS } from '../data/appointment-list-mock';

describe('AppointmentCard (SCR-APT-08)', () => {
  it('renders upcoming confirmed appointment card with queue number, reception warning, and hides staff-only actions', () => {
    const onViewTicketModal = vi.fn();
    const upcomingApt = MOCK_APPOINTMENTS[0];

    render(<AppointmentCard appointment={upcomingApt} onViewTicketModal={onViewTicketModal} />);

    expect(screen.getByText('ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM')).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-1013-0902')).toBeInTheDocument();
    expect(screen.getByText('012')).toBeInTheDocument();
    expect(screen.getByText('BS.CKII Trần Quốc Tuấn')).toBeInTheDocument();
    expect(screen.getByText(/PAT-8921/)).toBeInTheDocument();
    expect(screen.getByText(/Vui lòng có mặt lúc/)).toBeInTheDocument();

    // Unauthorized clinical/reception staff actions must never be rendered on Patient card
    expect(screen.queryByText(/Tiếp nhận Check-in/i)).toBeNull();
    expect(screen.queryByText(/Gọi số vào phòng/i)).toBeNull();

    fireEvent.click(screen.getByText(/Xem chi tiết phiếu hẹn & Mã QR/i));
    expect(onViewTicketModal).toHaveBeenCalledWith(upcomingApt);
  });

  it('renders completed appointment card with clinical summary and VietQR payment badge', () => {
    const onViewTicketModal = vi.fn();
    const completedApt = MOCK_APPOINTMENTS[1];

    render(<AppointmentCard appointment={completedApt} onViewTicketModal={onViewTicketModal} />);

    expect(screen.getByText('ĐÃ HOÀN TẤT KHÁM')).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-0815-0830')).toBeInTheDocument();
    expect(screen.getByText('Đã thanh toán 250.000 VNĐ qua VietQR')).toBeInTheDocument();
    expect(screen.getByText(/Viêm dạ dày trợt hang vị mức độ nhẹ/)).toBeInTheDocument();

    fireEvent.click(screen.getByText('Xem hồ sơ kết quả'));
    expect(onViewTicketModal).toHaveBeenCalledWith(completedApt);
  });

  it('renders cancelled appointment card with cancellation reason and rebook specialty action', () => {
    const onViewTicketModal = vi.fn();
    const onRebookSpecialty = vi.fn();
    const cancelledApt = MOCK_APPOINTMENTS[2];

    render(
      <AppointmentCard
        appointment={cancelledApt}
        onViewTicketModal={onViewTicketModal}
        onRebookSpecialty={onRebookSpecialty}
      />,
    );

    expect(screen.getByText('ĐÃ HỦY BỞI NGƯỜI BỆNH')).toBeInTheDocument();
    expect(screen.getByText('#APT-2026-0610-1400')).toBeInTheDocument();
    expect(screen.getByText(/Người bệnh bận công tác đột xuất/)).toBeInTheDocument();

    fireEvent.click(screen.getByText('Đặt lại chuyên khoa Tim Mạch'));
    expect(onRebookSpecialty).toHaveBeenCalledWith(cancelledApt);
  });
});
