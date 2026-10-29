import { X } from 'lucide-react';

import { useExaminationServiceStore } from '../store/examination-service.store';
import type { ColumnKey } from '../types/examination-service.types';

const COLUMNS: { key: ColumnKey; label: string; required?: boolean }[] = [
  { key: 'code', label: 'Mã Dịch Vụ', required: true },
  { key: 'name', label: 'Tên Dịch Vụ & Mô Tả', required: true },
  { key: 'category', label: 'Phân Loại' },
  { key: 'department', label: 'Khoa Phụ Trách' },
  { key: 'price', label: 'Giá Hiệu Lực' },
  { key: 'booking', label: 'Đặt Lịch Online' },
  { key: 'status', label: 'Trạng Thái' },
  { key: 'actions', label: 'Thao Tác' },
];

export function ServiceColumnModal() {
  const isOpen = useExaminationServiceStore((state) => state.isColumnModalOpen);
  const setIsOpen = useExaminationServiceStore((state) => state.setIsColumnModalOpen);
  const visibleColumns = useExaminationServiceStore((state) => state.visibleColumns);
  const toggleColumn = useExaminationServiceStore((state) => state.toggleColumn);

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="column-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
    >
      <div className="w-full max-w-md rounded-xl border border-border bg-white shadow-xl animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <h2 id="column-modal-title" className="text-sm font-bold text-ink">
            Tùy Chỉnh Cột Hiển Thị
          </h2>
          <button
            type="button"
            onClick={() => setIsOpen(false)}
            className="rounded p-1 text-slate-400 hover:text-slate-600"
            aria-label="Đóng cửa sổ"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="space-y-3 p-5">
          <p className="text-xs text-sub">
            Chọn các cột thông tin hiển thị trên bảng danh mục dịch vụ khám ngoại trú:
          </p>
          <div className="space-y-2.5 pt-1">
            {COLUMNS.map((col) => {
              const isChecked = visibleColumns[col.key];
              return (
                <label
                  key={col.key}
                  className={`flex items-center justify-between rounded-lg border px-3.5 py-2.5 text-xs transition-colors ${
                    isChecked
                      ? 'border-primary/30 bg-sky-50/40 text-ink'
                      : 'border-border bg-white text-sub'
                  } ${col.required ? 'opacity-80 cursor-not-allowed' : 'cursor-pointer hover:bg-slate-50'}`}
                >
                  <span className="font-medium">{col.label}</span>
                  <input
                    type="checkbox"
                    checked={isChecked}
                    disabled={col.required}
                    onChange={() => toggleColumn(col.key)}
                    className="rounded border-slate-300 text-primary focus:ring-primary"
                  />
                </label>
              );
            })}
          </div>
        </div>

        <div className="flex justify-end border-t border-border px-5 py-3 bg-slate-50 rounded-b-xl">
          <button
            type="button"
            onClick={() => setIsOpen(false)}
            className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white hover:bg-primary-dark transition-colors"
          >
            Áp Dụng & Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
