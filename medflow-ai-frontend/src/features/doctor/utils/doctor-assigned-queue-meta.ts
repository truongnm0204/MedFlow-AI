import type {
  DoctorAssignedQueueStatsDto,
  DoctorCallNextSelectionReason,
  DoctorQueueArrivalClass,
  DoctorQueueGroup,
  DoctorQueueGroupFilter,
  DoctorQueueIneligibleReason,
  DoctorQueuePresenceStatus,
} from '../types/doctor-assigned-queue.types';
import type { VisitPriority } from '../types/doctor.types';

export type DoctorQueueBadgeTone = 'sky' | 'emerald' | 'amber' | 'rose' | 'slate';

export const QUEUE_GROUP_META: Record<
  DoctorQueueGroup,
  { label: string; tone: DoctorQueueBadgeTone }
> = {
  IN_PROGRESS: { label: 'Đang khám', tone: 'emerald' },
  CALLED: { label: 'Đã gọi số', tone: 'sky' },
  ELIGIBLE: { label: 'Đủ điều kiện gọi', tone: 'sky' },
  NOT_ELIGIBLE: { label: 'Chưa đủ điều kiện', tone: 'amber' },
  SKIPPED: { label: 'Qua lượt', tone: 'rose' },
  COMPLETED: { label: 'Hoàn tất', tone: 'slate' },
};

export const INELIGIBLE_REASON_META: Record<
  DoctorQueueIneligibleReason,
  { label: string; tone: DoctorQueueBadgeTone }
> = {
  NOT_ARRIVED: { label: 'Chưa đến', tone: 'amber' },
  LEFT: { label: 'Đã rời khỏi khu chờ', tone: 'rose' },
  BLOCKED: { label: 'Giữ chỗ theo giờ hẹn', tone: 'amber' },
  AWAITING_RESULTS: { label: 'Chờ kết quả CLS', tone: 'rose' },
};

export const PRIORITY_META: Record<VisitPriority, { label: string; tone: DoctorQueueBadgeTone }> = {
  NORMAL: { label: 'Bình thường', tone: 'slate' },
  ELDERLY: { label: 'Người cao tuổi', tone: 'amber' },
  PREGNANT: { label: 'Thai phụ', tone: 'sky' },
};

export const PRESENCE_META: Record<
  DoctorQueuePresenceStatus,
  { label: string; tone: DoctorQueueBadgeTone }
> = {
  PRESENT: { label: 'Có mặt', tone: 'emerald' },
  NOT_ARRIVED: { label: 'Chưa đến', tone: 'amber' },
  LEFT: { label: 'Vắng mặt', tone: 'rose' },
};

export const ARRIVAL_CLASS_META: Record<
  DoctorQueueArrivalClass,
  { label: string; tone: DoctorQueueBadgeTone }
> = {
  ON_TIME_OR_GRACE: { label: 'Đúng giờ', tone: 'emerald' },
  EARLY: { label: 'Đến sớm', tone: 'sky' },
  LATE: { label: 'Đến muộn', tone: 'amber' },
  WALK_IN: { label: 'Vãng lai', tone: 'slate' },
};

export const CALL_NEXT_SELECTION_REASON_META: Record<
  DoctorCallNextSelectionReason,
  { label: string; description: string; tone: DoctorQueueBadgeTone }
> = {
  APPOINTMENT_DUE: {
    label: 'Đúng giờ hẹn',
    description: 'Lịch hẹn được bảo vệ đã đến giờ khám',
    tone: 'emerald',
  },
  RETURN_CONSULTATION: {
    label: 'Đọc KQ CLS',
    description: 'Bệnh nhân quay lại đọc kết quả cận lâm sàng đã sẵn sàng',
    tone: 'emerald',
  },
  PRIORITY: {
    label: 'Ưu tiên',
    description: 'Nhóm ưu tiên (Người cao tuổi / Thai phụ) theo tỷ lệ luân phiên công bằng',
    tone: 'amber',
  },
  SAFE_BACKFILL: {
    label: 'Safe Backfill',
    description: 'Khám sớm an toàn trong khoảng trống trước lịch hẹn được bảo vệ kế tiếp',
    tone: 'sky',
  },
  FIFO: {
    label: 'FIFO',
    description: 'Thứ tự sẵn sàng khám theo quy tắc công bằng (FIFO)',
    tone: 'slate',
  },
};

export const QUEUE_GROUP_FILTER_OPTIONS: {
  id: DoctorQueueGroupFilter;
  label: string;
  statKey: keyof DoctorAssignedQueueStatsDto;
}[] = [
  { id: 'ALL', label: 'Tất cả', statKey: 'totalEntries' },
  { id: 'IN_PROGRESS', label: 'Đang khám', statKey: 'inProgressCount' },
  { id: 'CALLED', label: 'Đã gọi số', statKey: 'calledCount' },
  { id: 'ELIGIBLE', label: 'Đủ điều kiện gọi', statKey: 'eligibleCount' },
  { id: 'NOT_ELIGIBLE', label: 'Chưa đủ điều kiện', statKey: 'notEligibleCount' },
  { id: 'SKIPPED', label: 'Qua lượt', statKey: 'skippedCount' },
  { id: 'COMPLETED', label: 'Hoàn tất', statKey: 'completedCount' },
];
