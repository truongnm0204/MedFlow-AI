import { HubConnectionState } from '@microsoft/signalr';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { ExaminationQueuePage } from '@/features/reception/pages/examination-queue-page';
import type { ExaminationQueueResponseDto } from '@/features/reception/types/examination-queue.types';
import { apiClient } from '@/shared/lib/axios';
import {
  resetRealtimeClientForTests,
  setRealtimeConnectionFactoryForTests,
  type HubConnectionLike,
} from '@/shared/lib/realtime-client';

import { AppointmentQueueTrackingPage } from '../pages/appointment-queue-tracking-page';
import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

vi.mock('@/shared/components/layout/app-header', () => ({
  AppHeader: () => <div data-testid="app-header" />,
}));
vi.mock('@/shared/components/layout/patient-footer', () => ({
  PatientFooter: () => <div data-testid="patient-footer" />,
}));

class TestQueueHub implements HubConnectionLike {
  state: HubConnectionState = HubConnectionState.Disconnected;
  readonly handlers = new Map<string, Array<(payload: unknown) => void>>();
  readonly invoked: Array<{ method: string; args: unknown[] }> = [];

  async start(): Promise<void> {
    this.state = HubConnectionState.Connected;
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected;
  }

  async invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T> {
    this.invoked.push({ method: methodName, args });
    return {
      resourceKey: `${methodName}:${String(args[0] ?? 'hospital')}`,
      expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
    } as T;
  }

  on(methodName: string, newMethod: (...args: never[]) => void): void {
    const list = this.handlers.get(methodName) ?? [];
    list.push(newMethod as unknown as (payload: unknown) => void);
    this.handlers.set(methodName, list);
  }

  off(methodName: string): void {
    this.handlers.delete(methodName);
  }

  onreconnecting(): void {}
  onreconnected(): void {}
  onclose(): void {}

  emitQueueChanged(servicePointId = 11): void {
    for (const cb of this.handlers.get('QueueChanged') ?? []) {
      cb({ eventType: 'QueueChanged', servicePointId });
    }
  }
}

describe('Queue Realtime Sync & Dirty-Form Preservation (#831 + #825)', () => {
  beforeEach(async () => {
    localStorage.clear();
    await resetRealtimeClientForTests();
  });

  afterEach(async () => {
    await resetRealtimeClientForTests();
    useAuthStore.getState().clearAuth();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('AppointmentQueueTrackingPage jumps immediately to called status and refreshes scope when transferred to a new room', async () => {
    const hub = new TestQueueHub();
    setRealtimeConnectionFactoryForTests(() => hub);

    useAuthStore.getState().setAuth({
      user: {
        id: 1,
        email: 'patient@medflow.vn',
        fullName: 'Bệnh Nhân A',
        roles: ['PATIENT'],
        patientCode: 'PAT-01',
        staffCode: null,
      },
      accessToken: 'jwt-patient',
      refreshToken: 'ref-patient',
    });

    let queueSnapshot: AppointmentQueueStatus = {
      appointmentId: '500',
      appointmentCode: 'APT-500',
      visitCode: 'VIS-500',
      queueNumber: '015',
      status: 'WAITING',
      statusLabel: 'ĐANG CHỜ KHÁM',
      roomName: 'Phòng Nội 101',
      doctorName: 'BS. Trần Văn B',
      departmentName: 'Khoa Nội',
      servingQueueNumber: '014',
      nextQueueNumber: '015',
      estimatedWaitLabel: 'Ước tính chờ ~15 phút',
      visitSteps: [
        {
          key: 'CHECK_IN',
          title: '1. Tiếp đón & Check-in',
          state: 'done',
          statusLabel: 'Đã hoàn tất',
          note: 'Quầy 1',
        },
        {
          key: 'CONSULTATION',
          title: '2. Khám Lâm sàng ban đầu',
          state: 'current',
          statusLabel: 'Đang chờ gọi',
          note: 'Đang chờ gọi',
        },
        {
          key: 'COMPLETE',
          title: '3. Hoàn thành lượt khám',
          state: 'upcoming',
          statusLabel: 'Chưa bắt đầu',
          note: '',
        },
      ],
      guidanceNote: 'Quý khách vui lòng ngồi tại sảnh chờ.',
    };

    vi.spyOn(apiClient, 'get').mockImplementation(async () => ({ data: queueSnapshot }));

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/appointments/APT-500/queue']}>
          <Routes>
            <Route
              path="/appointments/:appointmentId/queue"
              element={<AppointmentQueueTrackingPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('QUEUE: ĐANG CHỜ KHÁM')).toBeInTheDocument();
    await waitFor(() =>
      expect(hub.invoked.filter((i) => i.method === 'JoinAppointmentQueueGroup')).toHaveLength(1),
    );
    expect(screen.getByText(/Thời gian thực SignalR/)).toBeInTheDocument();

    // Doctor calls next AND transfers room -> SignalR emits minimal QueueChanged
    queueSnapshot = {
      ...queueSnapshot,
      statusLabel: 'ĐÃ GỌI SỐ - MỜI VÀO PHÒNG',
      roomName: 'Phòng Nội 102',
      servingQueueNumber: '015',
      nextQueueNumber: '016',
      estimatedWaitLabel: 'Đã đến lượt vào khám',
    };

    act(() => {
      hub.emitQueueChanged(11);
    });

    expect(await screen.findByText('QUEUE: ĐÃ GỌI SỐ - MỜI VÀO PHÒNG')).toBeInTheDocument();
    expect(screen.getByText('Phòng Nội 102')).toBeInTheDocument();

    // Room change triggers scope re-join so patient migrates to Room 102's bucket
    await waitFor(() =>
      expect(
        hub.invoked.filter((i) => i.method === 'JoinAppointmentQueueGroup').length,
      ).toBeGreaterThanOrEqual(2),
    );
  });

  it('ExaminationQueuePage refreshes live queue rows on QueueChanged WITHOUT wiping dirty modal form inputs (#825)', async () => {
    const user = userEvent.setup();
    const hub = new TestQueueHub();
    setRealtimeConnectionFactoryForTests(() => hub);

    useAuthStore.getState().setAuth({
      user: {
        id: 20,
        email: 'reception@medflow.vn',
        fullName: 'NV Tiếp đón',
        roles: ['RECEPTION'],
        patientCode: null,
        staffCode: 'STF-20',
      },
      accessToken: 'jwt-reception',
      refreshToken: 'ref-reception',
    });

    const buildBoard = (
      generatedAtUtc: string,
      secondEntryStatus: 'WAITING' | 'CALLED',
    ): ExaminationQueueResponseDto => ({
      queueDate: '2026-11-25',
      facilityName: 'Bệnh viện MedFlow',
      shiftLabel: 'Ca sáng',
      generatedAtUtc,
      stats: {
        totalEntries: 2,
        waitingCount: secondEntryStatus === 'WAITING' ? 2 : 1,
        calledOrServingCount: secondEntryStatus === 'CALLED' ? 1 : 0,
        completedCount: 0,
        skippedCount: 0,
        activeServicePoints: 1,
      },
      servicePoints: [
        {
          servicePointId: 'sp-11',
          servicePointName: 'Phòng Nội 101',
          roomCode: 'P101',
          roomName: 'Phòng Nội 101',
          departmentName: 'Khoa Nội',
          doctorName: 'BS. Nguyễn Văn C',
          isActive: true,
          currentServingTicketNo: secondEntryStatus === 'CALLED' ? 'A002' : null,
          nextTicketNo: 'A001',
          waitingCount: secondEntryStatus === 'WAITING' ? 2 : 1,
          inProgressCount: secondEntryStatus === 'CALLED' ? 1 : 0,
          completedCount: 0,
          skippedCount: 0,
          entries: [
            {
              id: 'q-001',
              queueNumber: 1,
              ticketNo: 'A001',
              servingOrder: 1,
              visitCode: 'VIS-001',
              patientName: 'Trần Thị Hoa',
              patientCode: 'PAT-001',
              visitType: 'SCHEDULED',
              servicePointId: 'sp-11',
              roomCode: 'P101',
              roomName: 'Phòng Nội 101',
              departmentName: 'Khoa Nội',
              doctorName: 'BS. Nguyễn Văn C',
              status: 'WAITING',
              priority: 'NORMAL',
              queueType: 'EXAMINATION',
              presenceStatus: 'PRESENT',
              arrivalClass: 'ON_TIME_OR_GRACE',
              scheduledAt: '08:30',
              checkedInAt: '08:20',
              calledAt: null,
              startedAt: null,
              blockedReason: null,
              isBackfilled: false,
            },
            {
              id: 'q-002',
              queueNumber: 2,
              ticketNo: 'A002',
              servingOrder: 2,
              visitCode: 'VIS-002',
              patientName: 'Lê Văn Minh',
              patientCode: 'PAT-002',
              visitType: 'WALK_IN',
              servicePointId: 'sp-11',
              roomCode: 'P101',
              roomName: 'Phòng Nội 101',
              departmentName: 'Khoa Nội',
              doctorName: 'BS. Nguyễn Văn C',
              status: secondEntryStatus,
              priority: 'NORMAL',
              queueType: 'EXAMINATION',
              presenceStatus: 'PRESENT',
              arrivalClass: 'WALK_IN',
              scheduledAt: null,
              checkedInAt: '08:25',
              calledAt: secondEntryStatus === 'CALLED' ? '08:40' : null,
              startedAt: null,
              blockedReason: null,
              isBackfilled: false,
            },
          ],
        },
      ],
    });

    let currentBoard = buildBoard('2026-11-25T01:30:00Z', 'WAITING');
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (String(url).includes('/queue-entries/')) {
        return {
          data: {
            queueEntryId: 1,
            visitId: 1,
            visitCode: 'VIS-001',
            ticketNo: 'A001',
            queueNumber: 1,
            queueType: 'EXAMINATION',
            currentStatus: 'WAITING',
            presenceStatus: 'PRESENT',
            priority: 'NORMAL',
            arrivalClass: 'ON_TIME_OR_GRACE',
            stateVersion: 1,
            allowedTargetStatuses: ['READY', 'CALLED', 'SKIPPED'],
            statusHistory: [],
          },
        };
      }
      return { data: currentBoard };
    });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/reception/queue']}>
          <Routes>
            <Route path="/reception/queue" element={<ExaminationQueuePage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('Trần Thị Hoa')).toBeInTheDocument();
    await waitFor(() =>
      expect(hub.invoked.some((i) => i.method === 'JoinHospitalQueueGroup')).toBe(true),
    );

    // Open status update modal for A001 and type unsaved clinical notes (#825 dirty form)
    const updateButtons = screen.getAllByRole('button', { name: /Cập nhật trạng thái/i });
    await user.click(updateButtons[0]!);

    const textboxes = await screen.findAllByRole('textbox');
    const dirtyTextbox = textboxes[textboxes.length - 1]!;
    await user.type(dirtyTextbox, 'Bệnh nhân xin ra ngoài nghe điện thoại 2 phút');
    expect(dirtyTextbox).toHaveValue('Bệnh nhân xin ra ngoài nghe điện thoại 2 phút');

    // Meanwhile Doctor calls A002 on another workstation -> SignalR QueueChanged fires!
    currentBoard = buildBoard('2026-11-25T01:31:00Z', 'CALLED');
    act(() => {
      hub.emitQueueChanged(11);
    });

    // Background board updates to show A002 as called, while the open modal's dirty input is 100% preserved!
    await waitFor(() => expect(screen.getAllByText('A002').length).toBeGreaterThanOrEqual(2));
    expect(dirtyTextbox).toHaveValue('Bệnh nhân xin ra ngoài nghe điện thoại 2 phút');
  });
});
