import { Calendar, Filter, RotateCcw, Search } from 'lucide-react';
import type { FormEvent } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';

import type { AuditLogFilterFormInputs } from '../schemas/audit-log.schema';
import type { AuditLogHospitalOptionDto } from '../types/audit-log.types';

interface AuditLogFilterBarProps {
  inputs: AuditLogFilterFormInputs;
  availableActions: string[];
  availableEntityTypes: string[];
  availableHospitals: AuditLogHospitalOptionDto[];
  isSystemAdmin: boolean;
  validationError: string | null;
  onInputChange: (patch: Partial<AuditLogFilterFormInputs>) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onReset: () => void;
}

export function AuditLogFilterBar({
  inputs,
  availableActions,
  availableEntityTypes,
  availableHospitals,
  isSystemAdmin,
  validationError,
  onInputChange,
  onSubmit,
  onReset,
}: AuditLogFilterBarProps) {
  return (
    <section
      aria-label="Bộ lọc tìm kiếm nhật ký kiểm toán"
      className="rounded-2xl border border-border bg-white p-5 shadow-sm"
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-6">
          {/* Keyword Search */}
          <div className="space-y-1 xl:col-span-2">
            <label htmlFor="audit-filter-keyword" className="block text-xs font-semibold text-sub">
              Từ khóa tìm kiếm
            </label>
            <div className="relative">
              <Search
                aria-hidden="true"
                className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
              />
              <input
                id="audit-filter-keyword"
                type="search"
                value={inputs.keyword}
                onChange={(e) => onInputChange({ keyword: e.target.value })}
                placeholder="Mã AUD-0001, email, nhân viên, thực thể, lý do..."
                className="h-9 w-full rounded-lg border border-border bg-canvas pr-3 pl-9 text-xs text-ink placeholder:text-sub/70 focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
              />
            </div>
          </div>

          {/* Action Filter */}
          <div className="space-y-1">
            <label htmlFor="audit-filter-action" className="block text-xs font-semibold text-sub">
              Hành động (Action)
            </label>
            <select
              id="audit-filter-action"
              value={inputs.action}
              onChange={(e) => onInputChange({ action: e.target.value })}
              className="h-9 w-full rounded-lg border border-border bg-canvas px-3 font-mono text-xs text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
            >
              <option value="">Tất cả hành động</option>
              {availableActions.map((act) => (
                <option key={act} value={act}>
                  {act}
                </option>
              ))}
            </select>
          </div>

          {/* Entity Type Filter */}
          <div className="space-y-1">
            <label
              htmlFor="audit-filter-entity-type"
              className="block text-xs font-semibold text-sub"
            >
              Loại đối tượng (Entity)
            </label>
            <select
              id="audit-filter-entity-type"
              value={inputs.entityType}
              onChange={(e) => onInputChange({ entityType: e.target.value })}
              className="h-9 w-full rounded-lg border border-border bg-canvas px-3 font-mono text-xs text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
            >
              <option value="">Tất cả đối tượng</option>
              {availableEntityTypes.map((entity) => (
                <option key={entity} value={entity}>
                  {entity}
                </option>
              ))}
            </select>
          </div>

          {/* From Date (UTC+7) */}
          <div className="space-y-1">
            <label
              htmlFor="audit-filter-from-date"
              className="flex items-center gap-1 text-xs font-semibold text-sub"
            >
              <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Từ ngày (UTC+7)</span>
            </label>
            <input
              id="audit-filter-from-date"
              type="date"
              value={inputs.fromDate}
              onChange={(e) => onInputChange({ fromDate: e.target.value })}
              className="h-9 w-full rounded-lg border border-border bg-canvas px-3 font-mono text-xs text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
            />
          </div>

          {/* To Date (UTC+7) */}
          <div className="space-y-1">
            <label
              htmlFor="audit-filter-to-date"
              className="flex items-center gap-1 text-xs font-semibold text-sub"
            >
              <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Đến ngày (UTC+7)</span>
            </label>
            <input
              id="audit-filter-to-date"
              type="date"
              value={inputs.toDate}
              onChange={(e) => onInputChange({ toDate: e.target.value })}
              className="h-9 w-full rounded-lg border border-border bg-canvas px-3 font-mono text-xs text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
            />
          </div>
        </div>

        <div className="flex flex-col gap-3 border-t border-border pt-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="flex flex-wrap items-end gap-3">
            {/* Actor User ID */}
            <div className="space-y-1">
              <label htmlFor="audit-filter-actor" className="block text-xs font-semibold text-sub">
                Mã người thực hiện (UID)
              </label>
              <input
                id="audit-filter-actor"
                type="number"
                min={1}
                value={inputs.actorUserIdInput}
                onChange={(e) => onInputChange({ actorUserIdInput: e.target.value })}
                placeholder="VD: 13"
                className="h-9 w-36 rounded-lg border border-border bg-canvas px-3 font-mono text-xs text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
              />
            </div>

            {/* Hospital Scope (SYSTEM_ADMIN can filter across facilities; HOSPITAL_ADMIN is server-locked) */}
            {isSystemAdmin ? (
              <div className="space-y-1">
                <label
                  htmlFor="audit-filter-hospital"
                  className="block text-xs font-semibold text-sub"
                >
                  Phạm vi cơ sở y tế
                </label>
                <select
                  id="audit-filter-hospital"
                  value={inputs.hospitalIdInput}
                  onChange={(e) => onInputChange({ hospitalIdInput: e.target.value })}
                  className="h-9 rounded-lg border border-border bg-canvas px-3 text-xs font-medium text-ink focus:border-primary focus:bg-white focus:ring-2 focus:ring-primary/20 focus:outline-none"
                >
                  <option value="">Toàn hệ thống MedFlow</option>
                  {availableHospitals.map((hosp) => (
                    <option key={hosp.id} value={String(hosp.id)}>
                      {hosp.name} ({hosp.code})
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <div className="rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-xs text-sky-800">
                Phạm vi dữ liệu:{' '}
                <strong className="font-semibold">
                  {availableHospitals[0]?.name ?? 'Cơ sở hiện tại của bạn'}
                </strong>
              </div>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button type="submit" size="sm">
              <Filter className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Lọc nhật ký</span>
            </Button>
            <Button type="button" variant="secondary" size="sm" onClick={onReset}>
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đặt lại bộ lọc</span>
            </Button>
          </div>
        </div>

        {validationError && (
          <div role="alert">
            <Alert tone="error">{validationError}</Alert>
          </div>
        )}
      </form>
    </section>
  );
}
