import { ArrowUpRight, RotateCcw } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/shared/ui/badge';

import type {
  HospitalConfigCategory,
  HospitalConfigStatus,
  HospitalConfigWorklistItem,
} from '../types/hospital-admin.types';

interface HospitalConfigWorklistTableProps {
  entries: HospitalConfigWorklistItem[];
  selectedItemId?: string;
  onSelectItem?: (item: HospitalConfigWorklistItem) => void;
  onResetFilters?: () => void;
}

const CATEGORY_LABELS: Record<HospitalConfigCategory, string> = {
  DEPARTMENT_ROOM: 'Khoa & Phòng',
  DOCTOR_SERVICE: 'Bác sĩ & Dịch vụ',
  SLOT_SCHEDULE: 'Lịch & Slot khám',
  POLICY_REPORT: 'Chính sách & Báo cáo',
};

const STATUS_META: Record<
  HospitalConfigStatus,
  { label: string; tone: 'emerald' | 'amber' | 'rose' }
> = {
  ACTIVE: { label: 'Đang hoạt động', tone: 'emerald' },
  NEEDS_REVIEW: { label: 'Cần rà soát', tone: 'amber' },
  MAINTENANCE: { label: 'Tạm ngừng / Bảo trì', tone: 'rose' },
};

export function HospitalConfigWorklistTable({
  entries,
  selectedItemId,
  onSelectItem,
  onResetFilters,
}: HospitalConfigWorklistTableProps) {
  if (entries.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-border bg-canvas px-4 py-10 text-center">
        <p className="text-xs font-medium text-sub">
          Không tìm thấy hạng mục cấu hình phù hợp với bộ lọc hiện tại.
        </p>
        {onResetFilters && (
          <button
            type="button"
            onClick={onResetFilters}
            className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-ink shadow-2xs transition-colors hover:bg-slate-50 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <RotateCcw aria-hidden="true" className="h-3.5 w-3.5 text-primary" />
            Đặt lại bộ lọc
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-xs">
        <thead>
          <tr className="border-b border-border font-mono text-[10px] tracking-wider uppercase text-sub">
            <th className="py-2.5 pr-3">Mã cấu hình</th>
            <th className="py-2.5 pr-3">Hạng mục &amp; Phạm vi</th>
            <th className="py-2.5 pr-3">Phân nhóm</th>
            <th className="py-2.5 pr-3">Chỉ số vận hành</th>
            <th className="py-2.5 pr-3">Trạng thái</th>
            <th className="py-2.5 text-right">Điều hướng</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {entries.map((item) => {
            const statusMeta = STATUS_META[item.status];
            const isSelected = selectedItemId === item.id;

            return (
              <tr
                key={item.id}
                onClick={() => onSelectItem?.(item)}
                className={`cursor-pointer transition-colors ${
                  isSelected ? 'bg-sky-50/70' : 'hover:bg-slate-50/80'
                }`}
              >
                <td className="py-3 pr-3 whitespace-nowrap">
                  <div className="font-mono text-xs font-bold text-primary">{item.code}</div>
                  <div className="font-mono text-[10px] text-sub">{item.updatedAt}</div>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-semibold text-ink">{item.name}</div>
                  <div className="text-[11px] text-sub">{item.departmentOrScope}</div>
                </td>
                <td className="py-3 pr-3 whitespace-nowrap">
                  <Badge tone="slate">{CATEGORY_LABELS[item.category]}</Badge>
                </td>
                <td className="py-3 pr-3">
                  <div className="font-medium text-ink">{item.metricLabel}</div>
                </td>
                <td className="py-3 pr-3 whitespace-nowrap">
                  <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                </td>
                <td className="py-3 text-right whitespace-nowrap">
                  <Link
                    to={item.route}
                    onClick={(e) => e.stopPropagation()}
                    className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-primary transition-colors hover:border-sky-300 hover:bg-sky-50"
                  >
                    <span>Mở cấu hình</span>
                    <ArrowUpRight aria-hidden="true" className="h-3.5 w-3.5" />
                  </Link>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
