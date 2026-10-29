import { CheckCircle2, History, Pencil, Power } from 'lucide-react';
import { Link } from 'react-router-dom';

import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ExaminationService } from '../types/examination-service.types';

interface ServiceTableProps {
  services: ExaminationService[];
  onViewHistory: (service: ExaminationService) => void;
}

export function ServiceTable({ services, onViewHistory }: ServiceTableProps) {
  const selectedIds = useExaminationServiceStore((state) => state.selectedIds);
  const visibleColumns = useExaminationServiceStore((state) => state.visibleColumns);
  const toggleSelection = useExaminationServiceStore((state) => state.toggleSelection);
  const toggleAll = useExaminationServiceStore((state) => state.toggleAll);
  const toggleStatus = useExaminationServiceStore((state) => state.toggleStatus);
  const clearSelection = useExaminationServiceStore((state) => state.clearSelection);
  const batchToggleStatus = useExaminationServiceStore((state) => state.batchToggleStatus);

  const visibleIds = services.map((s) => s.id);
  const allSelected = visibleIds.length > 0 && visibleIds.every((id) => selectedIds.includes(id));
  const formatVnd = (num: number) => num.toLocaleString('vi-VN') + ' đ';

  return (
    <div className="overflow-x-auto">
      {selectedIds.length > 0 && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-primary/20 bg-sky-50/70 px-6 py-2.5 text-xs text-primary font-medium">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="h-4 w-4" />
            <span>
              Đã chọn <b>{selectedIds.length}</b> dịch vụ
            </span>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => batchToggleStatus('ACTIVE')}
              className="rounded bg-white px-2.5 py-1 text-emerald-700 shadow-xs border border-emerald-200 hover:bg-emerald-50"
            >
              Kích hoạt hàng loạt
            </button>
            <button
              type="button"
              onClick={() => batchToggleStatus('SUSPENDED')}
              className="rounded bg-white px-2.5 py-1 text-amber-700 shadow-xs border border-amber-200 hover:bg-amber-50"
            >
              Tạm dừng hàng loạt
            </button>
            <button
              type="button"
              onClick={clearSelection}
              className="rounded bg-white px-2.5 py-1 text-slate-600 shadow-xs border border-slate-200 hover:bg-slate-50"
            >
              Bỏ chọn
            </button>
          </div>
        </div>
      )}

      <table className="w-full min-w-[1100px] table-fixed text-left text-xs">
        <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wide text-sub">
          <tr>
            <th className="w-10 px-4 py-4">
              <input
                aria-label="Chọn tất cả dịch vụ trên trang"
                type="checkbox"
                checked={allSelected}
                onChange={() => toggleAll(visibleIds)}
                className="rounded border-slate-300 text-primary focus:ring-primary"
              />
            </th>
            {visibleColumns.code && <th className="w-[10%] px-3">MÃ DỊCH VỤ</th>}
            {visibleColumns.name && <th className="w-[28%] px-3">TÊN DỊCH VỤ KHÁM NGOẠI TRÚ</th>}
            {visibleColumns.category && <th className="w-[13%] px-3">PHÂN LOẠI</th>}
            {visibleColumns.department && <th className="w-[15%] px-3">KHOA PHỤ TRÁCH</th>}
            {visibleColumns.price && <th className="w-[11%] px-3">GIÁ HIỆU LỰC</th>}
            {visibleColumns.booking && <th className="w-[12%] px-3">ĐẶT LỊCH ONLINE</th>}
            {visibleColumns.status && <th className="w-[11%] px-3">TRẠNG THÁI</th>}
            {visibleColumns.actions && <th className="w-[10%] px-3 text-right pr-5">THAO TÁC</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {services.map((service) => {
            const isSelected = selectedIds.includes(service.id);
            return (
              <tr
                key={service.id}
                className={`transition-colors hover:bg-sky-50/30 ${isSelected ? 'bg-sky-50/50' : 'bg-white'}`}
              >
                <td className="px-4 py-3.5">
                  <input
                    aria-label={`Chọn ${service.name}`}
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => toggleSelection(service.id)}
                    className="rounded border-slate-300 text-primary focus:ring-primary"
                  />
                </td>

                {visibleColumns.code && (
                  <td className="px-3 py-3.5 font-mono text-xs font-bold text-slate-800">
                    {service.code}
                  </td>
                )}

                {visibleColumns.name && (
                  <td className="px-3 py-3.5">
                    <div className="font-bold text-ink leading-snug">{service.name}</div>
                    <div className="text-[11px] text-slate-500 mt-0.5 line-clamp-1">
                      {service.description}
                    </div>
                  </td>
                )}

                {visibleColumns.category && (
                  <td className="px-3 py-3.5">
                    <span className="inline-block rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-medium text-slate-700">
                      {service.category}
                    </span>
                  </td>
                )}

                {visibleColumns.department && (
                  <td className="px-3 py-3.5 font-medium text-slate-700">{service.department}</td>
                )}

                {visibleColumns.price && (
                  <td className="px-3 py-3.5 font-bold text-slate-900">
                    {formatVnd(service.price)}
                  </td>
                )}

                {visibleColumns.booking && (
                  <td className="px-3 py-3.5">
                    {service.onlineBooking ? (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-[10px] font-semibold text-primary">
                        <span className="h-1.5 w-1.5 rounded-full bg-primary" />
                        Cho phép Online
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-50 px-2.5 py-0.5 text-[10px] font-semibold text-slate-600">
                        <span className="h-1.5 w-1.5 rounded-full bg-slate-400" />
                        Chỉ tại quầy
                      </span>
                    )}
                  </td>
                )}

                {visibleColumns.status && (
                  <td className="px-3 py-3.5">
                    {service.status === 'ACTIVE' ? (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-[10px] font-semibold text-emerald-700">
                        <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                        Hoạt động
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-2.5 py-0.5 text-[10px] font-semibold text-amber-700">
                        <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
                        Tạm dừng
                      </span>
                    )}
                  </td>
                )}

                {visibleColumns.actions && (
                  <td className="px-3 py-3.5 text-right pr-5">
                    <div className="flex items-center justify-end gap-1 text-slate-500">
                      <Link
                        to={`/admin/services/${service.id}/edit`}
                        title="Chỉnh sửa dịch vụ"
                        aria-label={`Chỉnh sửa ${service.name}`}
                        className="p-1 hover:text-primary transition-colors"
                      >
                        <Pencil className="h-4 w-4" />
                      </Link>
                      <button
                        type="button"
                        onClick={() => onViewHistory(service)}
                        title="Xem lịch sử giá & chính sách UC52"
                        aria-label={`Xem lịch sử giá ${service.name}`}
                        className="p-1 hover:text-primary transition-colors"
                      >
                        <History className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => toggleStatus(service.id)}
                        title={
                          service.status === 'ACTIVE' ? 'Tạm dừng dịch vụ' : 'Kích hoạt dịch vụ'
                        }
                        aria-label={`Đổi trạng thái ${service.name}`}
                        className="p-1 hover:text-primary transition-colors"
                      >
                        <Power
                          className={`h-4 w-4 ${service.status === 'ACTIVE' ? 'text-emerald-600' : 'text-slate-400'}`}
                        />
                      </button>
                    </div>
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      {services.length === 0 && (
        <div className="py-16 text-center text-sub">
          <p className="text-sm font-medium">Không tìm thấy dịch vụ khám phù hợp với bộ lọc.</p>
          <p className="mt-1 text-xs text-slate-400">
            Hãy thử xóa hoặc nới lỏng các điều kiện tìm kiếm.
          </p>
        </div>
      )}
    </div>
  );
}
