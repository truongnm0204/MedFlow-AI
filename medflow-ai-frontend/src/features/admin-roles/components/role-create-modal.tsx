import { Plus, RotateCcw, Search, ShieldCheck } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import {
  PERMISSION_GROUPS,
  ROLE_SCOPE_LABELS,
  ROLE_STATUS_LABELS,
  SYSTEM_PERMISSION_LIST,
} from '../data/role-mock-data';
import { createRoleSchema } from '../schemas/role-create.schema';
import type {
  CreateRoleRequest,
  RolePermissionItem,
  RoleScope,
  RoleStatus,
} from '../types/role.types';

export interface RoleCreateModalProps {
  open: boolean;
  isSubmitting?: boolean;
  serverError?: string | null;
  onClose: () => void;
  onCreate: (payload: CreateRoleRequest) => Promise<void> | void;
}

function sortCanonicalPermissionCodes(codes: Iterable<string>): string[] {
  const codeSet = new Set(codes);
  return SYSTEM_PERMISSION_LIST.filter((perm) => codeSet.has(perm.code)).map((perm) => perm.code);
}

const INPUT_CLASS =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:opacity-60';

export function RoleCreateModal({
  open,
  isSubmitting = false,
  serverError = null,
  onClose,
  onCreate,
}: RoleCreateModalProps) {
  if (!open) return null;

  return (
    <Modal open={true} onClose={onClose} maxWidth="max-w-3xl">
      <RoleCreateForm
        isSubmitting={isSubmitting}
        serverError={serverError}
        onClose={onClose}
        onCreate={onCreate}
      />
    </Modal>
  );
}

interface RoleCreateFormProps {
  isSubmitting: boolean;
  serverError: string | null;
  onClose: () => void;
  onCreate: (payload: CreateRoleRequest) => Promise<void> | void;
}

interface FieldErrorMap {
  code?: string;
  name?: string;
  scope?: string;
  status?: string;
  description?: string;
  segregationNote?: string;
  permissionCodes?: string;
  note?: string;
}

function RoleCreateForm({ isSubmitting, serverError, onClose, onCreate }: RoleCreateFormProps) {
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [scope, setScope] = useState<RoleScope>('OPERATIONAL');
  const [status, setStatus] = useState<RoleStatus>('ACTIVE');
  const [description, setDescription] = useState('');
  const [segregationNote, setSegregationNote] = useState('');
  const [selectedCodes, setSelectedCodes] = useState<string[]>([]);
  const [note, setNote] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrorMap>({});

  const selectedSet = useMemo(() => new Set(selectedCodes), [selectedCodes]);

  const filteredGroups = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return PERMISSION_GROUPS;

    return PERMISSION_GROUPS.map((group) => ({
      module: group.module,
      permissions: group.permissions.filter(
        (perm) =>
          perm.code.toLowerCase().includes(q) ||
          perm.name.toLowerCase().includes(q) ||
          perm.module.toLowerCase().includes(q),
      ),
    })).filter((group) => group.permissions.length > 0);
  }, [searchQuery]);

  const clearValidation = () => {
    setValidationError(null);
    setFieldErrors({});
  };

  const handleTogglePermission = (permCode: string) => {
    clearValidation();
    setSelectedCodes((prev) => {
      const next = new Set(prev);
      if (next.has(permCode)) {
        next.delete(permCode);
      } else {
        next.add(permCode);
      }
      return sortCanonicalPermissionCodes(next);
    });
  };

  const handleToggleModuleGroup = (groupPermissions: RolePermissionItem[]) => {
    clearValidation();
    setSelectedCodes((prev) => {
      const next = new Set(prev);
      const allSelected = groupPermissions.every((perm) => next.has(perm.code));
      if (allSelected) {
        for (const perm of groupPermissions) {
          next.delete(perm.code);
        }
      } else {
        for (const perm of groupPermissions) {
          next.add(perm.code);
        }
      }
      return sortCanonicalPermissionCodes(next);
    });
  };

  const handleClearPermissions = () => {
    clearValidation();
    setSelectedCodes([]);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const parsed = createRoleSchema.safeParse({
      code,
      name,
      scope,
      status,
      description: description.trim() ? description.trim() : undefined,
      segregationNote: segregationNote.trim() ? segregationNote.trim() : undefined,
      permissionCodes: selectedCodes,
      note: note.trim() ? note.trim() : undefined,
    });

    if (!parsed.success) {
      const nextFieldErrors: FieldErrorMap = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0] as keyof FieldErrorMap | undefined;
        if (key && !nextFieldErrors[key]) {
          nextFieldErrors[key] = issue.message;
        }
      }
      setFieldErrors(nextFieldErrors);
      setValidationError(parsed.error.issues[0]?.message ?? 'Dữ liệu tạo vai trò không hợp lệ.');
      return;
    }

    clearValidation();
    await onCreate(parsed.data);
  };

  const activeError = validationError ?? serverError;

  return (
    <form onSubmit={(e) => void handleSubmit(e)} noValidate className="p-6">
      <div className="flex items-start gap-3 border-b border-border pb-4 pr-8">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
          <ShieldCheck className="h-5 w-5" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-bold text-navy">Tạo vai trò mới</h2>
            <span className="inline-flex rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-sub">
              Tùy chỉnh
            </span>
          </div>
          <p className="mt-1 text-xs font-medium text-sub">
            Khai báo vai trò tùy chỉnh trong danh mục RBAC và thiết lập quyền hạn khởi tạo (UC58)
          </p>
        </div>
      </div>

      {activeError && (
        <div role="alert" className="mt-4">
          <Alert tone="error">{activeError}</Alert>
        </div>
      )}

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="create-role-code" className="mb-1 block text-xs font-semibold text-navy">
            Mã vai trò <span className="text-rose-600">*</span>
          </label>
          <input
            id="create-role-code"
            type="text"
            value={code}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setCode(e.target.value);
            }}
            aria-label="Mã vai trò"
            aria-invalid={fieldErrors.code ? true : undefined}
            maxLength={50}
            placeholder="VD: CLINICAL_COORDINATOR"
            className={`${INPUT_CLASS} font-mono uppercase`}
          />
          <p className="mt-1 text-[11px] text-sub">
            Từ 2–50 ký tự, bắt đầu bằng chữ cái, chỉ dùng chữ in hoa, số và dấu gạch dưới (_).
          </p>
          {fieldErrors.code && (
            <p className="mt-1 text-xs font-medium text-rose-600">{fieldErrors.code}</p>
          )}
        </div>

        <div>
          <label htmlFor="create-role-name" className="mb-1 block text-xs font-semibold text-navy">
            Tên vai trò <span className="text-rose-600">*</span>
          </label>
          <input
            id="create-role-name"
            type="text"
            value={name}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setName(e.target.value);
            }}
            aria-label="Tên vai trò"
            aria-invalid={fieldErrors.name ? true : undefined}
            maxLength={120}
            placeholder="VD: Điều phối viên lâm sàng"
            className={INPUT_CLASS}
          />
          <p className="mt-1 text-[11px] text-sub">Tên hiển thị nghiệp vụ (2–120 ký tự).</p>
          {fieldErrors.name && (
            <p className="mt-1 text-xs font-medium text-rose-600">{fieldErrors.name}</p>
          )}
        </div>

        <div>
          <label htmlFor="create-role-scope" className="mb-1 block text-xs font-semibold text-navy">
            Nhóm phân hệ vai trò <span className="text-rose-600">*</span>
          </label>
          <select
            id="create-role-scope"
            value={scope}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setScope(e.target.value as RoleScope);
            }}
            aria-label="Nhóm phân hệ vai trò"
            className={INPUT_CLASS}
          >
            {Object.entries(ROLE_SCOPE_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label} ({value})
              </option>
            ))}
          </select>
        </div>

        <div>
          <label
            htmlFor="create-role-status"
            className="mb-1 block text-xs font-semibold text-navy"
          >
            Trạng thái vai trò <span className="text-rose-600">*</span>
          </label>
          <select
            id="create-role-status"
            value={status}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setStatus(e.target.value as RoleStatus);
            }}
            aria-label="Trạng thái vai trò"
            className={INPUT_CLASS}
          >
            {Object.entries(ROLE_STATUS_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label} ({value})
              </option>
            ))}
          </select>
        </div>

        <div className="sm:col-span-2">
          <label
            htmlFor="create-role-description"
            className="mb-1 block text-xs font-semibold text-navy"
          >
            Mô tả vai trò
          </label>
          <textarea
            id="create-role-description"
            rows={2}
            value={description}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setDescription(e.target.value);
            }}
            aria-label="Mô tả vai trò"
            maxLength={500}
            placeholder="Mô tả chức năng, nhiệm vụ và phạm vi hoạt động của vai trò..."
            className={INPUT_CLASS}
          />
          {fieldErrors.description && (
            <p className="mt-1 text-xs font-medium text-rose-600">{fieldErrors.description}</p>
          )}
        </div>

        <div className="sm:col-span-2">
          <label
            htmlFor="create-role-sod-note"
            className="mb-1 block text-xs font-semibold text-navy"
          >
            Ghi chú phân tách nhiệm vụ (SoD)
          </label>
          <input
            id="create-role-sod-note"
            type="text"
            value={segregationNote}
            disabled={isSubmitting}
            onChange={(e) => {
              clearValidation();
              setSegregationNote(e.target.value);
            }}
            aria-label="Ghi chú phân tách nhiệm vụ (SoD)"
            maxLength={500}
            placeholder="Tuân thủ nguyên tắc phân quyền tối thiểu (Least Privilege)..."
            className={INPUT_CLASS}
          />
          {fieldErrors.segregationNote && (
            <p className="mt-1 text-xs font-medium text-rose-600">{fieldErrors.segregationNote}</p>
          )}
        </div>
      </div>

      <div className="mt-4 rounded-lg border border-sky-100 bg-sky-50/60 p-3 text-xs text-ink">
        <p className="font-semibold text-navy">Nguyên tắc phân tách nhiệm vụ (SoD):</p>
        <p className="mt-0.5 text-[11px] text-sub">
          Không cho phép gán đồng thời quyền{' '}
          <code className="font-mono font-semibold text-navy">PRESCRIPTION_ISSUE</code> (Kê đơn
          thuốc) và <code className="font-mono font-semibold text-navy">MEDICINE_DISPENSE</code>{' '}
          (Cấp phát thuốc) trên cùng một vai trò.
        </p>
      </div>

      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-canvas p-3">
        <div className="relative min-w-[220px] flex-1">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
            aria-hidden="true"
          />
          <input
            type="search"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            aria-label="Tìm kiếm quyền hạn khởi tạo"
            placeholder="Tìm mã quyền, tên quyền hoặc phân hệ..."
            className="w-full rounded-lg border border-border bg-white py-1.5 pl-9 pr-3 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
          />
        </div>

        <div className="flex flex-wrap items-center gap-2 text-xs">
          <span className="rounded-md border border-border bg-white px-2.5 py-1 font-semibold text-navy">
            Đã chọn: {selectedCodes.length} / {SYSTEM_PERMISSION_LIST.length} quyền
          </span>
          <button
            type="button"
            onClick={handleClearPermissions}
            disabled={selectedCodes.length === 0 || isSubmitting}
            className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 font-medium text-sub transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Bỏ chọn tất cả
          </button>
        </div>
      </div>

      <div className="mt-3 max-h-60 space-y-3 overflow-y-auto pr-1">
        {filteredGroups.length === 0 ? (
          <div className="rounded-xl border border-border bg-canvas px-4 py-6 text-center text-xs text-sub">
            Không tìm thấy quyền hạn phù hợp với từ khóa.
          </div>
        ) : (
          filteredGroups.map((group) => {
            const allInGroupSelected = group.permissions.every((perm) =>
              selectedSet.has(perm.code),
            );
            return (
              <div
                key={group.module}
                className="overflow-hidden rounded-xl border border-border bg-white"
              >
                <div className="flex items-center justify-between border-b border-border bg-canvas px-4 py-2">
                  <span className="text-xs font-semibold uppercase tracking-wide text-navy">
                    {group.module} ({group.permissions.length})
                  </span>
                  <button
                    type="button"
                    disabled={isSubmitting}
                    onClick={() => handleToggleModuleGroup(group.permissions)}
                    aria-label={`Chọn nhóm ${group.module}`}
                    className="text-xs font-semibold text-primary hover:text-primary-dark disabled:opacity-50"
                  >
                    {allInGroupSelected ? 'Bỏ chọn nhóm' : 'Chọn cả nhóm'}
                  </button>
                </div>
                <div className="divide-y divide-border">
                  {group.permissions.map((perm) => {
                    const isChecked = selectedSet.has(perm.code);
                    return (
                      <label
                        key={perm.code}
                        className="flex cursor-pointer flex-wrap items-center justify-between gap-2 px-4 py-2 text-sm transition-colors hover:bg-canvas"
                      >
                        <div className="flex items-start gap-3">
                          <input
                            type="checkbox"
                            checked={isChecked}
                            disabled={isSubmitting}
                            onChange={() => handleTogglePermission(perm.code)}
                            aria-label={`Quyền ${perm.code} - ${perm.name}`}
                            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
                          />
                          <div>
                            <span className="font-medium text-navy">{perm.name}</span>
                            <p className="text-xs text-sub">{perm.module}</p>
                          </div>
                        </div>

                        <span className="rounded border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-navy">
                          {perm.code}
                        </span>
                      </label>
                    );
                  })}
                </div>
              </div>
            );
          })
        )}
      </div>

      <div className="mt-4">
        <label htmlFor="create-role-audit-note" className="block text-xs font-semibold text-navy">
          Ghi chú kiểm toán tạo vai trò
        </label>
        <input
          id="create-role-audit-note"
          type="text"
          value={note}
          disabled={isSubmitting}
          onChange={(e) => {
            clearValidation();
            setNote(e.target.value);
          }}
          aria-label="Ghi chú kiểm toán tạo vai trò"
          maxLength={250}
          placeholder="Nhập lý do khởi tạo vai trò mới (ghi nhận vào audit_logs)..."
          className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
        />
      </div>

      <div className="mt-5 flex items-center justify-end gap-2 border-t border-border pt-4">
        <Button type="button" variant="secondary" onClick={onClose} disabled={isSubmitting}>
          Hủy
        </Button>
        <Button type="submit" loading={isSubmitting} disabled={isSubmitting}>
          <Plus className="h-4 w-4" aria-hidden="true" />
          Tạo vai trò
        </Button>
      </div>
    </form>
  );
}
