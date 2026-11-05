import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import {
  assignExaminationRoom,
  fetchCompatibleExaminationRooms,
} from '../api/visit-room-assignment.api';
import { AssignExaminationRoomModal } from '../components/assign-examination-room-modal';
import { COMPATIBLE_EXAMINATION_ROOMS_MOCK } from '../data/reception-mock-data';
import type {
  AssignExaminationRoomResponseDto,
  CompatibleExaminationRoomListResponseDto,
} from '../types/assign-examination-room.types';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
  },
}));

const MOCK_RESPONSE: AssignExaminationRoomResponseDto = {
  visitId: 100,
  visitCode: 'VIS-202610-0003',
  patientId: 3,
  patientCode: 'BN-2024-0071',
  patientName: 'Lê Minh Cường',
  appointmentId: 3,
  appointmentCode: 'APT-202610-0003',
  assignedDoctorId: 1,
  assignedDoctorCode: 'DOC-001',
  assignedDoctorName: 'BS Nguyễn Minh Đức',
  examinationRoomId: 2,
  roomCode: 'RM-002',
  roomName: 'Khám Tiêu hóa 102',
  roomType: 'EXAMINATION',
  location: 'Tầng 1, Khu Demo',
  departmentId: 2,
  departmentCode: 'GI',
  departmentName: 'Khoa Tiêu hóa',
  hospitalId: 1,
  visitStatus: 'WAITING',
  previousRoomId: null,
  previousRoomCode: null,
  queueEntryId: 21,
  queueNumber: 1,
  queueCode: 'B001',
  queueStatus: 'WAITING',
  priority: 'URGENT',
  servicePointId: 1,
  servicePointName: 'Hàng đợi bác sĩ 1',
  roomCapacity: 2,
  currentRoomOccupancy: 1,
  checkedInByStaffId: 3,
  checkedInAt: '2026-10-27T08:45:00',
  assignedAtUtc: '2026-10-27T01:50:00Z',
};

function setup() {
  const onClose = vi.fn();
  const onAssigned = vi.fn();
  const onSubmitOverride = vi.fn().mockResolvedValue(MOCK_RESPONSE);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const compatibleResponse: CompatibleExaminationRoomListResponseDto = {
    visitId: 100,
    visitCode: 'VIS-202610-0003',
    visitStatus: 'CHECKED_IN',
    hospitalId: 1,
    assignedDoctorId: 1,
    assignedDoctorCode: 'DOC-001',
    assignedDoctorName: 'BS Nguyễn Minh Đức',
    currentExaminationRoomId: null,
    currentExaminationRoomCode: null,
    requiredDepartmentId: 2,
    requiredDepartmentName: 'Khoa Tiêu hóa',
    rooms: COMPATIBLE_EXAMINATION_ROOMS_MOCK,
  };

  vi.mocked(apiClient.get).mockResolvedValue({ data: compatibleResponse });

  render(
    <QueryClientProvider client={queryClient}>
      <AssignExaminationRoomModal
        open
        target={{
          visitIdOrCode: 'VIS-202610-0003',
          patientCode: 'BN-2024-0071',
          patientName: 'Lê Minh Cường',
          currentRoomCode: 'P.103',
          priority: 'NORMAL',
        }}
        onClose={onClose}
        onAssigned={onAssigned}
        onSubmitOverride={onSubmitOverride}
      />
    </QueryClientProvider>,
  );

  return {
    onClose,
    onAssigned,
    onSubmitOverride,
    user: userEvent.setup(),
  };
}

describe('AssignExaminationRoomModal & visit-room-assignment API', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('goi dung endpoint GET compatible-rooms va PUT examination-room', async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({
      data: {
        visitId: 100,
        visitCode: 'VIS-202610-0003',
        visitStatus: 'CHECKED_IN',
        hospitalId: 1,
        assignedDoctorId: 1,
        assignedDoctorCode: 'DOC-001',
        assignedDoctorName: 'BS Nguyễn Minh Đức',
        currentExaminationRoomId: null,
        currentExaminationRoomCode: null,
        requiredDepartmentId: 2,
        requiredDepartmentName: 'Khoa Tiêu hóa',
        rooms: COMPATIBLE_EXAMINATION_ROOMS_MOCK,
      },
    });
    vi.mocked(apiClient.put).mockResolvedValueOnce({ data: MOCK_RESPONSE });

    const roomsResp = await fetchCompatibleExaminationRooms('VIS-202610-0003');
    expect(apiClient.get).toHaveBeenCalledWith('/reception/visits/VIS-202610-0003/available-rooms');
    expect(roomsResp.rooms).toHaveLength(3);

    const assignResp = await assignExaminationRoom('VIS-202610-0003', {
      roomCode: 'RM-002',
      priority: 'URGENT',
      reason: 'Điều phối sang phòng 102',
    });
    expect(apiClient.put).toHaveBeenCalledWith(
      '/reception/visits/VIS-202610-0003/examination-room',
      {
        roomCode: 'RM-002',
        priority: 'URGENT',
        reason: 'Điều phối sang phòng 102',
      },
    );
    expect(assignResp.roomCode).toBe('RM-002');
  });

  it('hien thi thong tin benh nhan, danh sach buong kham va chan buong khong tuong thich', () => {
    setup();

    expect(screen.getByText('Phân buồng khám lâm sàng (UC27)')).toBeInTheDocument();
    expect(screen.getByText('Lê Minh Cường')).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0071')).toBeInTheDocument();
    expect(screen.getByText('Khám Tiêu hóa 101')).toBeInTheDocument();
    expect(screen.getByText('Khám Tiêu hóa 102')).toBeInTheDocument();
    expect(screen.getByText('Khám Tim mạch 201')).toBeInTheDocument();

    const incompatibleRoomBtn = screen.getByText('Khám Tim mạch 201').closest('button');
    expect(incompatibleRoomBtn).toBeDisabled();
  });

  it('bao loi validation neu chua chon buong kham', async () => {
    const { user, onSubmitOverride } = setup();

    await user.click(screen.getByRole('button', { name: 'Xác nhận phân phòng' }));

    expect(await screen.findByText('Vui lòng chọn buồng khám lâm sàng')).toBeInTheDocument();
    expect(onSubmitOverride).not.toHaveBeenCalled();
  });

  it('chon buong kham tuong thich va submit thanh cong', async () => {
    const { user, onSubmitOverride, onAssigned } = setup();

    await user.click(screen.getByText('Khám Tiêu hóa 102'));
    await user.selectOptions(screen.getByLabelText('Mức độ ưu tiên'), 'URGENT');
    await user.type(
      screen.getByLabelText('Lý do / ghi chú điều phối (tùy chọn)'),
      'Điều phối vào phòng Tiêu hóa 102',
    );
    await user.click(screen.getByRole('button', { name: 'Xác nhận phân phòng' }));

    await waitFor(() => expect(onSubmitOverride).toHaveBeenCalledTimes(1));
    expect(onSubmitOverride).toHaveBeenCalledWith('VIS-202610-0003', {
      examinationRoomId: 2,
      roomCode: 'RM-002',
      priority: 'URGENT',
      reason: 'Điều phối vào phòng Tiêu hóa 102',
    });
    expect(onAssigned).toHaveBeenCalledWith(
      expect.objectContaining({
        visitCode: 'VIS-202610-0003',
        roomCode: 'RM-002',
        roomName: 'Khám Tiêu hóa 102',
        priority: 'URGENT',
      }),
    );
  });
});
