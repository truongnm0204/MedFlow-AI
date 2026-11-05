import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  fetchVisitCompatibleRooms,
  transferVisitToCompatibleRoom,
} from '../api/visit-room-transfer.api';
import { TransferVisitRoomModal } from '../components/transfer-visit-room-modal';
import { COMPATIBLE_TRANSFER_ROOMS_MOCK } from '../data/reception-mock-data';
import type {
  VisitCompatibleRoomsResponseDto,
  VisitRoomTransferResponseDto,
} from '../types/visit-room-transfer.types';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

const MOCK_TRANSFER_RESPONSE: VisitRoomTransferResponseDto = {
  visitId: 100,
  visitCode: 'VIS-202610-0002',
  patientId: 2,
  patientCode: 'BN-2024-0042',
  patientName: 'Trần Thị Bích',
  visitStatus: 'WAITING',
  previousRoomId: 1,
  previousRoomCode: 'RM-001',
  previousRoomName: 'Khám Tiêu hóa 101',
  currentRoomId: 2,
  currentRoomCode: 'RM-002',
  currentRoomName: 'Khám Tiêu hóa 102',
  departmentId: 2,
  departmentCode: 'GI',
  departmentName: 'Khoa Tiêu hóa',
  previousDoctorId: 1,
  assignedDoctorId: 1,
  assignedDoctorCode: 'DOC-001',
  assignedDoctorName: 'BS Nguyễn Minh Đức',
  queueNumber: 13,
  queueStatus: 'WAITING',
  transferReason: 'Phòng khám hiện tại đang quá tải',
  notes: 'Bệnh nhân đồng ý chuyển sang phòng 102',
  transferredByUserId: 5,
  transferredByStaffCode: 'STF-003',
  transferredByName: 'Lễ tân Lê Lan',
  transferredAtUtc: '2026-10-27T02:00:00Z',
  transferHistory: [
    {
      auditLogId: 10,
      fromRoomId: 1,
      fromRoomCode: 'RM-001',
      fromRoomName: 'Khám Tiêu hóa 101',
      toRoomId: 2,
      toRoomCode: 'RM-002',
      toRoomName: 'Khám Tiêu hóa 102',
      previousDoctorId: 1,
      assignedDoctorId: 1,
      reason: 'Phòng khám hiện tại đang quá tải',
      notes: 'Bệnh nhân đồng ý chuyển sang phòng 102',
      transferredByUserId: 5,
      transferredByStaffCode: 'STF-003',
      transferredByName: 'Lễ tân Lê Lan',
      transferredAtUtc: '2026-10-27T02:00:00Z',
    },
  ],
};

function setup() {
  const onClose = vi.fn();
  const onTransferred = vi.fn();
  const onSubmitOverride = vi.fn().mockResolvedValue(MOCK_TRANSFER_RESPONSE);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const compatibleResponse: VisitCompatibleRoomsResponseDto = {
    visitId: 100,
    visitCode: 'VIS-202610-0002',
    patientId: 2,
    patientCode: 'BN-2024-0042',
    patientName: 'Trần Thị Bích',
    visitStatus: 'CALLED',
    currentRoomId: 1,
    currentRoomCode: 'RM-001',
    currentRoomName: 'Khám Tiêu hóa 101',
    departmentId: 2,
    departmentCode: 'GI',
    departmentName: 'Khoa Tiêu hóa',
    assignedDoctorId: 1,
    assignedDoctorCode: 'DOC-001',
    assignedDoctorName: 'BS Nguyễn Minh Đức',
    queueNumber: 13,
    queueStatus: 'CALLED',
    compatibleRooms: COMPATIBLE_TRANSFER_ROOMS_MOCK,
    transferHistory: [],
  };

  vi.mocked(apiClient.get).mockResolvedValue({ data: compatibleResponse });

  render(
    <QueryClientProvider client={queryClient}>
      <TransferVisitRoomModal
        open
        target={{
          visitIdOrCode: 'VIS-202610-0002',
          patientCode: 'BN-2024-0042',
          patientName: 'Trần Thị Bích',
          currentRoomCode: 'RM-001',
          ticketNo: 'A013',
        }}
        onClose={onClose}
        onTransferred={onTransferred}
        onSubmitOverride={onSubmitOverride}
      />
    </QueryClientProvider>,
  );

  return {
    onClose,
    onTransferred,
    onSubmitOverride,
    user: userEvent.setup(),
  };
}

describe('TransferVisitRoomModal & visit-room-transfer API', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('goi dung endpoint GET compatible-rooms va POST transfer-room', async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({
      data: {
        visitId: 100,
        visitCode: 'VIS-202610-0002',
        patientId: 2,
        patientCode: 'BN-2024-0042',
        patientName: 'Trần Thị Bích',
        visitStatus: 'CALLED',
        currentRoomId: 1,
        currentRoomCode: 'RM-001',
        currentRoomName: 'Khám Tiêu hóa 101',
        departmentId: 2,
        departmentCode: 'GI',
        departmentName: 'Khoa Tiêu hóa',
        assignedDoctorId: 1,
        assignedDoctorCode: 'DOC-001',
        assignedDoctorName: 'BS Nguyễn Minh Đức',
        queueNumber: 13,
        queueStatus: 'CALLED',
        compatibleRooms: COMPATIBLE_TRANSFER_ROOMS_MOCK,
        transferHistory: [],
      },
    });
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: MOCK_TRANSFER_RESPONSE });

    const roomsResp = await fetchVisitCompatibleRooms('VIS-202610-0002');
    expect(apiClient.get).toHaveBeenCalledWith(
      '/reception/visits/VIS-202610-0002/compatible-rooms',
    );
    expect(roomsResp.compatibleRooms).toHaveLength(4);

    const transferResp = await transferVisitToCompatibleRoom('VIS-202610-0002', {
      targetRoomId: 2,
      targetRoomCode: 'RM-002',
      reason: 'Phòng khám hiện tại đang quá tải',
      notes: 'Bệnh nhân đồng ý chuyển sang phòng 102',
    });
    expect(apiClient.post).toHaveBeenCalledWith('/reception/visits/VIS-202610-0002/transfer-room', {
      targetRoomId: 2,
      targetRoomCode: 'RM-002',
      reason: 'Phòng khám hiện tại đang quá tải',
      notes: 'Bệnh nhân đồng ý chuyển sang phòng 102',
    });
    expect(transferResp.currentRoomCode).toBe('RM-002');
    expect(transferResp.previousRoomCode).toBe('RM-001');
  });

  it('hien thi thong tin benh nhan va khoa chon phong hien tai / bao tri / khac khoa', () => {
    setup();

    expect(
      screen.getByText('Chuyển bệnh nhân sang phòng khám tương thích (UC27)'),
    ).toBeInTheDocument();
    expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();

    const currentRoomBtn = screen.getByText('Khám Tiêu hóa 101').closest('button');
    expect(currentRoomBtn).toBeDisabled();

    const maintenanceRoomBtn = screen.getByText('Khám Tiêu hóa 103 (Bảo trì)').closest('button');
    expect(maintenanceRoomBtn).toBeDisabled();

    const incompatibleRoomBtn = screen.getByText('Khám Tim mạch 201').closest('button');
    expect(incompatibleRoomBtn).toBeDisabled();

    const availableRoomBtn = screen.getByText('Khám Tiêu hóa 102').closest('button');
    expect(availableRoomBtn).not.toBeDisabled();
  });

  it('bao loi validation neu chua chon phong dich hoac chua nhap ly do', async () => {
    const { user, onSubmitOverride } = setup();

    await user.click(screen.getByRole('button', { name: 'Xác nhận chuyển phòng' }));

    expect(
      await screen.findByText('Vui lòng chọn phòng khám đích tương thích'),
    ).toBeInTheDocument();
    expect(screen.getByText('Vui lòng nhập lý do chuyển phòng')).toBeInTheDocument();
    expect(onSubmitOverride).not.toHaveBeenCalled();
  });

  it('chon phong tuong thich RM-002, nhap ly do va submit thanh cong', async () => {
    const { user, onSubmitOverride, onTransferred } = setup();

    const availableRoomBtn = screen.getByText('Khám Tiêu hóa 102').closest('button')!;
    await user.click(availableRoomBtn);

    await user.type(
      screen.getByLabelText('Lý do chuyển phòng (bắt buộc)'),
      'Phòng khám hiện tại đang quá tải',
    );
    await user.type(
      screen.getByLabelText('Ghi chú điều phối (tùy chọn)'),
      'Bệnh nhân đồng ý chuyển sang phòng 102',
    );

    await user.click(screen.getByRole('button', { name: 'Xác nhận chuyển phòng' }));

    await waitFor(() => {
      expect(onSubmitOverride).toHaveBeenCalledWith('VIS-202610-0002', {
        targetRoomId: 2,
        targetRoomCode: 'RM-002',
        reason: 'Phòng khám hiện tại đang quá tải',
        notes: 'Bệnh nhân đồng ý chuyển sang phòng 102',
      });
    });

    expect(onTransferred).toHaveBeenCalledWith(
      expect.objectContaining({
        visitCode: 'VIS-202610-0002',
        previousRoomCode: 'RM-001',
        currentRoomCode: 'RM-002',
        currentRoomName: 'Khám Tiêu hóa 102',
        queueNumber: 13,
      }),
    );
  });
});
