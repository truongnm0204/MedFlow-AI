import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import * as notificationsApi from '../api/notifications.api';
import { AppointmentReminderBanner } from '../components/appointment-reminder-banner';
import { NotificationBellPopover } from '../components/notification-bell-popover';
import { buildNotificationsQueryKey } from '../hooks/use-notifications';
import type { NotificationFeedResponse } from '../types/appointment-reminder.types';

function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

function renderWithQueryClient(ui: ReactNode, queryClient: QueryClient) {
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const SAMPLE_FEED_PATIENT_1: NotificationFeedResponse = {
  unreadCount: 2,
  activeReminder: {
    id: 'rem-101',
    appointmentId: '101',
    appointmentCode: 'APT-202611-0101',
    patientName: 'Nguyễn Văn An',
    doctorName: 'BS.CKI Nguyễn Minh Đức',
    specialtyName: 'Tiêu hóa',
    roomName: 'Khám Tiêu hóa 101 • Tầng 1',
    scheduledDate: '2026-11-26',
    scheduledTime: '08:30',
    hoursRemaining: 3,
    urgency: 'URGENT',
    preparationNotes: ['Có mặt trước 15 phút tại quầy tiếp đón.'],
    documentsRequired: ['CCCD gắn chip hoặc VNeID mức 2.'],
    isConfirmedByPatient: false,
    notifiedAt: 'Vừa xong',
  },
  items: [
    {
      id: '501',
      title: 'Nhắc lịch khám sắp diễn ra #APT-202611-0101',
      message: 'Bạn có lịch khám Tiêu hóa lúc 08:30.',
      category: 'APPOINTMENT_REMINDER',
      notificationType: 'APPOINTMENT_REMINDER',
      createdAt: '5 phút trước',
      isRead: false,
    },
    {
      id: '502',
      title: 'Xác nhận đặt lịch khám #APT-202611-0101',
      message: 'Lịch khám đã được ghi nhận thành công.',
      category: 'APPOINTMENT_REMINDER',
      notificationType: 'APPOINTMENT_BOOKED',
      createdAt: '10 phút trước',
      isRead: false,
    },
  ],
};

describe('Notifications Live API Integration, Cache Isolation & Optimistic Rollback', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    act(() => {
      useAuthStore.getState().setAuth({
        user: {
          id: 1,
          email: 'patient01@medflow.test',
          fullName: 'Nguyễn Văn An',
          roles: ['PATIENT'],
          patientCode: 'PAT-0001',
        },
        accessToken: 'fake-jwt-patient-1',
        refreshToken: 'fake-refresh-patient-1',
      });
    });
  });

  afterEach(() => {
    act(() => {
      useAuthStore.getState().clearAuth();
    });
  });

  it('fetches live notifications, marks all as read, and keeps AppointmentReminderBanner visible independently of ReadAt', async () => {
    const fetchSpy = vi
      .spyOn(notificationsApi, 'fetchNotificationsApi')
      .mockResolvedValue(SAMPLE_FEED_PATIENT_1);
    const markAllSpy = vi
      .spyOn(notificationsApi, 'markAllNotificationsReadApi')
      .mockResolvedValue({ markedCount: 2, unreadCount: 0 });

    const qc = createTestQueryClient();
    renderWithQueryClient(
      <div>
        <NotificationBellPopover />
        <AppointmentReminderBanner />
      </div>,
      qc,
    );

    await waitFor(() => {
      expect(screen.getByTestId('unread-badge')).toHaveTextContent('2');
    });
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    // Banner renders upcoming appointment details
    expect(screen.getByLabelText('Thông báo nhắc lịch khám')).toBeInTheDocument();
    expect(screen.getByText('APT-202611-0101')).toBeInTheDocument();

    // Click bell -> mark all as read
    fireEvent.click(screen.getByRole('button', { name: /Thông báo/i }));
    fireEvent.click(screen.getByRole('button', { name: /Đọc tất cả/i }));

    await waitFor(() => {
      expect(screen.queryByTestId('unread-badge')).not.toBeInTheDocument();
    });
    expect(markAllSpy).toHaveBeenCalledTimes(1);

    // Banner MUST remain visible even after all notifications are read!
    expect(screen.getByLabelText('Thông báo nhắc lịch khám')).toBeInTheDocument();
    expect(screen.getByText('APT-202611-0101')).toBeInTheDocument();
  });

  it('rolls back optimistic read state when markNotificationReadApi fails', async () => {
    vi.spyOn(notificationsApi, 'fetchNotificationsApi').mockResolvedValue(SAMPLE_FEED_PATIENT_1);
    vi.spyOn(notificationsApi, 'markNotificationReadApi').mockRejectedValue(
      new Error('Network failure'),
    );

    const qc = createTestQueryClient();
    renderWithQueryClient(<NotificationBellPopover />, qc);

    await waitFor(() => {
      expect(screen.getByTestId('unread-badge')).toHaveTextContent('2');
    });

    fireEvent.click(screen.getByRole('button', { name: /Thông báo/i }));
    fireEvent.click(screen.getByText(/Nhắc lịch khám sắp diễn ra/i));

    // After API rejection, unread badge rolls back cleanly to 2
    await waitFor(() => {
      expect(screen.getByTestId('unread-badge')).toHaveTextContent('2');
    });
  });

  it('isolates notification cache per userId and purges previous user cache on logout or account switch', async () => {
    const fetchSpy = vi
      .spyOn(notificationsApi, 'fetchNotificationsApi')
      .mockImplementation(async () => {
        const currentUserId = useAuthStore.getState().user?.id;
        if (currentUserId === 1) {
          return SAMPLE_FEED_PATIENT_1;
        }
        return {
          unreadCount: 0,
          activeReminder: null,
          items: [
            {
              id: '999',
              title: 'Thông báo riêng của Trần Thị Bình',
              message: 'Hồ sơ PAT-0002',
              category: 'SYSTEM',
              createdAt: 'Vừa xong',
              isRead: true,
            },
          ],
        };
      });

    const qc = createTestQueryClient();
    renderWithQueryClient(<NotificationBellPopover />, qc);

    await waitFor(() => {
      expect(screen.getByTestId('unread-badge')).toHaveTextContent('2');
    });
    expect(qc.getQueryData(buildNotificationsQueryKey(1))).toBeDefined();

    // Switch directly to Patient 2 -> Patient 1 cache is purged and Patient 2 gets isolated cache
    act(() => {
      useAuthStore.getState().setAuth({
        user: {
          id: 2,
          email: 'patient02@medflow.test',
          fullName: 'Trần Thị Bình',
          roles: ['PATIENT'],
          patientCode: 'PAT-0002',
        },
        accessToken: 'fake-jwt-patient-2',
        refreshToken: 'fake-refresh-patient-2',
      });
    });

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(qc.getQueryData(buildNotificationsQueryKey(2))).toBeDefined();
    });
    expect(qc.getQueryData(buildNotificationsQueryKey(1))).toBeUndefined();
    expect(screen.queryByTestId('unread-badge')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Thông báo' }));
    expect(await screen.findByText('Thông báo riêng của Trần Thị Bình')).toBeInTheDocument();
    expect(screen.queryByText(/APT-202611-0101/)).not.toBeInTheDocument();

    // Logout -> purges all notification queries
    act(() => {
      useAuthStore.getState().clearAuth();
    });

    await waitFor(() => {
      expect(qc.getQueryData(buildNotificationsQueryKey(2))).toBeUndefined();
    });
  });
});
