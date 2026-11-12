import { RotateCcw, Search } from 'lucide-react';
import { type FormEvent, useState } from 'react';

import { Button } from '@/shared/ui/button';

import { diagnosticServiceKeywordSchema } from '../schemas/diagnostic-service-filter.schema';
import type {
  DiagnosticServiceDepartmentOption,
  DiagnosticServiceFilterValue,
  DiagnosticServiceStatus,
} from '../types/diagnostic-service.types';

interface DiagnosticServiceFiltersProps {
  value: DiagnosticServiceFilterValue;
  departmentOptions: DiagnosticServiceDepartmentOption[];
  isDepartmentOptionsLoading: boolean;
  onChange: (next: DiagnosticServiceFilterValue) => void;
  onReset: () => void;
}

export function DiagnosticServiceFilters({
  value,
  departmentOptions,
  isDepartmentOptionsLoading,
  onChange,
  onReset,
}: DiagnosticServiceFiltersProps) {
  const [draftKeyword, setDraftKeyword] = useState(value.keyword);
  const [keywordError, setKeywordError] = useState<string | null>(null);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = diagnosticServiceKeywordSchema.safeParse(draftKeyword);
    if (!parsed.success) {
      setKeywordError(parsed.error.issues[0]?.message ?? 'Từ khóa tìm kiếm tối đa 100 ký tự.');
      return;
    }
    setKeywordError(null);
    onChange({
      ...value,
      keyword: parsed.data,
    });
  };

  const handleReset = () => {
    setDraftKeyword('');
    setKeywordError(null);
    onReset();
  };

  return (
    <form
      role="search"
      aria-label="Bộ lọc dịch vụ cận lâm sàng"
      onSubmit={handleSubmit}
      className="rounded-xl border border-border bg-white p-4 shadow-sm"
    >
      <div className="flex flex-col gap-3 lg:flex-row lg:items-end">
        <div className="min-w-0 flex-1">
          <label htmlFor="diagnostic-keyword-input" className="sr-only">
            Tìm theo mã, tên hoặc mô tả dịch vụ
          </label>
          <div className="flex gap-2">
            <div className="relative flex-1">
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
              />
              <input
                id="diagnostic-keyword-input"
                type="text"
                value={draftKeyword}
                onChange={(e) => {
                  setDraftKeyword(e.target.value);
                  if (keywordError) {
                    setKeywordError(null);
                  }
                }}
                placeholder="Tìm theo mã, tên hoặc mô tả dịch vụ"
                aria-invalid={Boolean(keywordError)}
                aria-describedby={keywordError ? 'diagnostic-keyword-error' : undefined}
                className="w-full rounded-lg border border-border bg-white py-2 pr-3 pl-9 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
              />
            </div>
            <Button type="submit" variant="primary" size="sm" className="shrink-0">
              Tìm kiếm
            </Button>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2.5">
          <select
            aria-label="Lọc theo khoa phòng"
            disabled={isDepartmentOptionsLoading}
            value={value.departmentId !== undefined ? String(value.departmentId) : ''}
            onChange={(e) => {
              const raw = e.target.value;
              onChange({
                ...value,
                departmentId: raw ? Number(raw) : undefined,
              });
            }}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 disabled:cursor-not-allowed disabled:opacity-60"
          >
            <option value="">Tất cả khoa phòng</option>
            {departmentOptions.map((dept) => (
              <option key={dept.id} value={String(dept.id)}>
                {dept.name}
              </option>
            ))}
          </select>

          <select
            aria-label="Lọc theo trạng thái"
            value={value.status ?? ''}
            onChange={(e) => {
              const raw = e.target.value;
              onChange({
                ...value,
                status: raw ? (raw as DiagnosticServiceStatus) : undefined,
              });
            }}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
          >
            <option value="">Tất cả trạng thái</option>
            <option value="ACTIVE">Đang hoạt động</option>
            <option value="INACTIVE">Ngừng hoạt động</option>
          </select>

          <select
            aria-label="Lọc theo yêu cầu xác nhận kết quả"
            value={
              value.requiresResultVerification === undefined
                ? ''
                : String(value.requiresResultVerification)
            }
            onChange={(e) => {
              const raw = e.target.value;
              onChange({
                ...value,
                requiresResultVerification: raw === '' ? undefined : raw === 'true',
              });
            }}
            className="rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/30"
          >
            <option value="">Tất cả</option>
            <option value="true">Cần xác nhận</option>
            <option value="false">Không cần xác nhận</option>
          </select>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={handleReset}
            className="shrink-0"
          >
            <RotateCcw aria-hidden="true" className="h-3.5 w-3.5" />
            <span>Đặt lại bộ lọc</span>
          </Button>
        </div>
      </div>

      {keywordError && (
        <p
          id="diagnostic-keyword-error"
          role="alert"
          className="mt-2 text-xs font-medium text-rose-600"
        >
          {keywordError}
        </p>
      )}
    </form>
  );
}
