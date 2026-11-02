import type { AppointmentListItem } from '../types/appointment-list.types';
import type {
  AppointmentQueueStatus,
  QueueTrackingStatus,
  QueueVisitStep,
} from '../types/appointment-queue.types';

export function buildMockQueueStatus(appointment: AppointmentListItem): AppointmentQueueStatus {
  const queueNumValue = Number(appointment.queueNumber ?? '12');
  const queueNumber = String(Number.isNaN(queueNumValue) ? 12 : queueNumValue).padStart(3, '0');
  const servingQueueNumber = queueNumValue > 2 ? String(queueNumValue - 2).padStart(3, '0') : null;
  const nextQueueNumber = queueNumValue > 1 ? String(queueNumValue - 1).padStart(3, '0') : null;
  const visitCode = `VIS-${appointment.appointmentCode.replace(/^APT-/, '')}`;

  let status: QueueTrackingStatus = 'AWAITING_CHECK_IN';
  let statusLabel = 'CHỜ CHECK-IN';
  let visitSteps: QueueVisitStep[];

  if (appointment.status === 'CHECKED_IN') {
    status = 'WAITING';
    statusLabel = 'ĐANG CHỜ KHÁM';
    visitSteps = [
      {
        key: 'CHECK_IN',
        title: '1. Tiếp đón & Check-in',
        state: 'done',
        statusLabel: 'Đã check-in',
        note: 'Kiosk / Quầy Tiếp đón',
      },
      {
        key: 'CONSULTATION',
        title: '2. Khám Lâm sàng ban đầu',
        state: 'current',
        statusLabel: 'CHƯA BẮT ĐẦU',
        note: 'Đã check-in, chưa được gọi. Chỉ bắt đầu khi bác sĩ khám thực tế.',
      },
      {
        key: 'COMPLETE',
        title: '3. Hoàn thành lượt khám',
        state: 'upcoming',
        statusLabel: null,
        note: 'Nhận đơn thuốc chỉ khi bác sĩ kê đơn.',
      },
    ];
  } else if (appointment.status === 'COMPLETED') {
    status = 'COMPLETED';
    statusLabel = 'ĐÃ HOÀN THÀNH';
    visitSteps = [
      {
        key: 'CHECK_IN',
        title: '1. Tiếp đón & Check-in',
        state: 'done',
        statusLabel: 'Đã check-in',
        note: 'Kiosk / Quầy Tiếp đón',
      },
      {
        key: 'CONSULTATION',
        title: '2. Khám Lâm sàng ban đầu',
        state: 'done',
        statusLabel: 'Đã hoàn tất',
        note: 'Bác sĩ đã hoàn tất thăm khám.',
      },
      {
        key: 'COMPLETE',
        title: '3. Hoàn thành lượt khám',
        state: 'done',
        statusLabel: 'Đã hoàn thành',
        note: 'Nhận đơn thuốc chỉ khi bác sĩ kê đơn.',
      },
    ];
  } else {
    visitSteps = [
      {
        key: 'CHECK_IN',
        title: '1. Tiếp đón & Check-in',
        state: 'current',
        statusLabel: 'CHƯA CHECK-IN',
        note: `Có mặt trước ${appointment.checkInDeadlineTime ?? '--:--'} tại ${
          appointment.checkInCounterLabel ?? 'Quầy Tiếp đón'
        }.`,
      },
      {
        key: 'CONSULTATION',
        title: '2. Khám Lâm sàng ban đầu',
        state: 'upcoming',
        statusLabel: null,
        note: 'Chỉ bắt đầu khi bác sĩ khám thực tế.',
      },
      {
        key: 'COMPLETE',
        title: '3. Hoàn thành lượt khám',
        state: 'upcoming',
        statusLabel: null,
        note: 'Nhận đơn thuốc chỉ khi bác sĩ kê đơn.',
      },
    ];
  }

  return {
    appointmentId: appointment.id,
    appointmentCode: appointment.appointmentCode,
    visitCode,
    queueNumber,
    status,
    statusLabel,
    roomName: appointment.roomName,
    doctorName: appointment.doctorName,
    departmentName: appointment.departmentName,
    servingQueueNumber,
    nextQueueNumber,
    estimatedWaitLabel: null,
    visitSteps,
    guidanceNote: `Quý khách vui lòng ngồi tại sảnh chờ trước cửa ${appointment.roomName}. Khi hệ thống gọi số thứ tự ${queueNumber}, mời quý khách vào phòng khám. Nếu quý khách có việc rời sảnh chờ quá 15 phút sau khi được gọi, số thứ tự sẽ tự động chuyển sang hàng đợi điều phối trễ.`,
  };
}
