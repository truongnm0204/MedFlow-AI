import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { AppointmentReminderBanner } from '../components/appointment-reminder-banner';
import { AppointmentReminderModal } from '../components/appointment-reminder-modal';
import { NotificationBellPopover } from '../components/notification-bell-popover';
import { APPOINTMENT_REMINDERS_MOCK } from '../data/appointment-reminder-mock';

describe('Appointment Reminder Components', () => {
  const mockReminder = APPOINTMENT_REMINDERS_MOCK[0];

  describe('AppointmentReminderBanner', () => {
    it('renders appointment details and urgency badge', () => {
      render(<AppointmentReminderBanner reminder={mockReminder} />);

      expect(screen.getByText(/APT-2024-0891/)).toBeInTheDocument();
      expect(screen.getByText(/Nội tổng quát/)).toBeInTheDocument();
      expect(screen.getByText(/BS. CKII Trần Khánh/)).toBeInTheDocument();
      expect(screen.getByText(/CÒN 2 GIỜ/)).toBeInTheDocument();
    });

    it('opens detail modal when clicking action button', () => {
      render(<AppointmentReminderBanner reminder={mockReminder} />);

      const actionBtn = screen.getByRole('button', { name: /Xem hướng dẫn & Lưu ý/i });
      fireEvent.click(actionBtn);

      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: /Nhắc Lịch Khám Bệnh Ngoại Trú/i })).toBeInTheDocument();
    });
  });

  describe('AppointmentReminderModal', () => {
    it('renders all preparations and documents required', () => {
      render(
        <AppointmentReminderModal
          isOpen={true}
          onClose={vi.fn()}
          reminder={mockReminder}
        />
      );

      expect(screen.getByText(/P.102 • Tầng 1 - Khu Khám Đa Khoa/)).toBeInTheDocument();
      expect(screen.getByText(/Nhịn ăn sáng ít nhất 6 tiếng/)).toBeInTheDocument();
      expect(screen.getByText(/Căn cước công dân gắn chip/)).toBeInTheDocument();
    });

    it('handles confirmation of attendance', () => {
      const handleConfirm = vi.fn();
      render(
        <AppointmentReminderModal
          isOpen={true}
          onClose={vi.fn()}
          reminder={mockReminder}
          onConfirmAttendance={handleConfirm}
        />
      );

      const confirmBtn = screen.getByRole('button', { name: /Xác nhận sẽ đến khám/i });
      fireEvent.click(confirmBtn);

      expect(handleConfirm).toHaveBeenCalledWith(mockReminder.id);
      expect(screen.getByText(/Đã xác nhận có mặt/i)).toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent(/Đã ghi nhận bạn sẽ đến đúng giờ/i);
    });

    it('calls onClose when close button clicked', () => {
      const handleClose = vi.fn();
      render(
        <AppointmentReminderModal
          isOpen={true}
          onClose={handleClose}
          reminder={mockReminder}
        />
      );

      const closeBtn = screen.getByRole('button', { name: /Đóng hộp thoại nhắc lịch/i });
      fireEvent.click(closeBtn);

      expect(handleClose).toHaveBeenCalled();
    });
  });

  describe('NotificationBellPopover', () => {
    it('renders unread badge and opens notification list on click', () => {
      render(<NotificationBellPopover />);

      const badge = screen.getByTestId('unread-badge');
      expect(badge).toHaveTextContent('1');

      const bellBtn = screen.getByRole('button', { name: /Thông báo/i });
      fireEvent.click(bellBtn);

      expect(screen.getByRole('menu', { name: /Danh sách thông báo/i })).toBeInTheDocument();
      expect(screen.getByText(/Nhắc lịch khám sắp diễn ra/i)).toBeInTheDocument();
    });

    it('marks all as read when clicking mark all button', () => {
      render(<NotificationBellPopover />);

      const bellBtn = screen.getByRole('button', { name: /Thông báo/i });
      fireEvent.click(bellBtn);

      const markAllBtn = screen.getByRole('button', { name: /Đọc tất cả/i });
      fireEvent.click(markAllBtn);

      expect(screen.queryByTestId('unread-badge')).not.toBeInTheDocument();
    });

    it('opens reminder modal when clicking on appointment notification', () => {
      render(<NotificationBellPopover />);

      const bellBtn = screen.getByRole('button', { name: /Thông báo/i });
      fireEvent.click(bellBtn);

      const reminderItem = screen.getByText(/Nhắc lịch khám sắp diễn ra/i);
      fireEvent.click(reminderItem);

      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByText(/APT-2024-0891/)).toBeInTheDocument();
    });
  });
});
