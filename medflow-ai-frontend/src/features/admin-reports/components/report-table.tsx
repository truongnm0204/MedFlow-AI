import { Clock, FileText, MoreVertical, RefreshCw } from 'lucide-react';

import type { OperationalReportItem } from '../types/operational-report.types';

interface ReportTableProps {
  reports: OperationalReportItem[];
  selectedIds: string[];
  onToggleSelection: (id: string) => void;
  onToggleAll: (visibleIds: string[]) => void;
  onRefreshReport: (id: string) => void;
}

export function ReportTable({
  reports,
  selectedIds,
  onToggleSelection,
  onToggleAll,
  onRefreshReport,
}: ReportTableProps) {
  const visibleIds = reports.map((report) => report.id);
  const allSelected = visibleIds.length > 0 && visibleIds.every((id) => selectedIds.includes(id));

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[1240px] table-fixed text-left text-xs">
        <colgroup>
          <col className="w-12" />
          <col className="w-[7%]" />
          <col className="w-[18%]" />
          <col className="w-[11%]" />
          <col className="w-[11%]" />
          <col className="w-[10%]" />
          <col className="w-[10%]" />
          <col className="w-[13%]" />
          <col className="w-[11%]" />
          <col className="w-[8%]" />
        </colgroup>
        <thead className="border-b border-border bg-slate-50/80 text-[11px] font-bold uppercase tracking-wide text-sub">
          <tr className="h-14">
            <th className="px-4 text-center">
              <input
                type="checkbox"
                checked={allSelected}
                onChange={() => onToggleAll(visibleIds)}
                aria-label="Chọn tất cả báo cáo trên trang"
                className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
              />
            </th>
            <th className="px-4">MÃ BC</th>
            <th className="px-4">BÁO CÁO / PHẠM VI</th>
            <th className="px-4">PHÂN NHÓM</th>
            <th className="px-4">CHỈ SỐ</th>
            <th className="px-4">CƠ SỞ</th>
            <th className="px-4">LƯỢT TIẾP NHẬN</th>
            <th className="px-4">CHỜ TRUNG VỊ</th>
            <th className="px-4">KỲ / TRẠNG THÁI</th>
            <th className="px-4 text-center">THAO TÁC</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border bg-white">
          {reports.map((report) => {
            const isSelected = selectedIds.includes(report.id);
            const isAggregated = report.aggregationStatus === 'AGGREGATED';

            const statusBadge =
              report.status === 'COMPLETED'
                ? {
                    label: 'Hoàn tất',
                    dotClass: 'bg-primary',
                    textClass: 'text-emerald-700',
                    bgClass: 'border-slate-100 bg-canvas',
                  }
                : report.status === 'NEEDS_REFRESH'
                  ? {
                      label: 'Cần làm mới',
                      dotClass: 'bg-amber-500',
                      textClass: 'text-amber-700',
                      bgClass: 'border-amber-200 bg-amber-50/70',
                    }
                  : {
                      label: 'Cần rà soát',
                      dotClass: 'bg-amber-500',
                      textClass: 'text-amber-700',
                      bgClass: 'border-amber-200 bg-amber-50/70',
                    };

            return (
              <tr
                key={report.id}
                className={`h-[76px] transition-colors hover:bg-sky-50/40 ${
                  isSelected ? 'bg-sky-50/30' : ''
                }`}
              >
                <td className="px-4 text-center">
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => onToggleSelection(report.id)}
                    aria-label={`Chọn báo cáo ${report.code} - ${report.name}`}
                    className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                  />
                </td>

                {/* MÃ BC */}
                <td className="px-4">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-[13px] font-bold text-ink">{report.code}</span>
                    <FileText className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-hidden="true" />
                  </div>
                </td>

                {/* BÁO CÁO / PHẠM VI */}
                <td className="px-4">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-bold text-ink">{report.name}</p>
                    <p className="mt-0.5 text-[11px] font-semibold tracking-wide text-sub">
                      {report.periodLabel}
                    </p>
                  </div>
                </td>

                {/* PHÂN NHÓM */}
                <td className="px-4">
                  <span className="inline-flex items-center rounded border border-border bg-canvas px-2.5 py-1 text-xs font-bold text-ink">
                    {report.category}
                  </span>
                </td>

                {/* CHỈ SỐ (Facility column per Figma row layout) */}
                <td className="px-4">
                  <span className="text-sm font-medium text-ink">{report.facility}</span>
                </td>

                {/* CƠ SỞ (Intake volume column per Figma row layout) */}
                <td className="px-4">
                  <span className="text-sm text-sub">{report.intakeVolumeLabel}</span>
                </td>

                {/* LƯỢT TIẾP NHẬN (Median wait column per Figma row layout) */}
                <td className="px-4">
                  <div className="flex items-center gap-1.5">
                    <span className="text-[13px] font-medium text-ink">
                      {report.medianWaitLabel}
                    </span>
                    {report.medianWaitLabel !== '—' && (
                      <Clock className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
                    )}
                  </div>
                </td>

                {/* CHỜ TRUNG VỊ (Aggregation status badge per Figma row layout) */}
                <td className="px-4">
                  <span className="inline-flex items-center gap-1.5 rounded border border-border bg-canvas px-3 py-1 text-xs font-medium text-ink">
                    <span
                      className={`h-1.5 w-1.5 rounded-full ${
                        isAggregated ? 'bg-primary' : 'bg-amber-500'
                      }`}
                      aria-hidden="true"
                    />
                    {isAggregated ? 'Đã tổng hợp' : 'Cần làm mới'}
                  </span>
                </td>

                {/* KỲ / TRẠNG THÁI */}
                <td className="px-4">
                  <span
                    className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-bold ${statusBadge.bgClass} ${statusBadge.textClass}`}
                  >
                    <span
                      className={`h-2 w-2 rounded-full ${statusBadge.dotClass}`}
                      aria-hidden="true"
                    />
                    {statusBadge.label}
                  </span>
                </td>

                {/* THAO TÁC */}
                <td className="px-4 text-center">
                  <div className="flex items-center justify-center gap-1">
                    {report.status !== 'COMPLETED' && (
                      <button
                        type="button"
                        onClick={() => onRefreshReport(report.id)}
                        className="inline-flex h-8 w-8 items-center justify-center rounded text-sub transition-colors hover:bg-sky-50 hover:text-primary"
                        aria-label={`Làm mới báo cáo ${report.code}`}
                        title="Làm mới tổng hợp"
                      >
                        <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => onRefreshReport(report.id)}
                      className="inline-flex h-8 w-8 items-center justify-center rounded text-sub transition-colors hover:bg-slate-100 hover:text-ink"
                      aria-label={`Thao tác báo cáo ${report.code}`}
                      title="Tùy chọn báo cáo"
                    >
                      <MoreVertical className="h-4 w-4" aria-hidden="true" />
                    </button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {reports.length === 0 && (
        <p className="py-16 text-center text-sm text-sub">
          Không tìm thấy báo cáo vận hành phù hợp với bộ lọc hiện tại.
        </p>
      )}
    </div>
  );
}
