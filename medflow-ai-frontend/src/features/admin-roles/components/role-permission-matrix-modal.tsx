import { AlertTriangle, Lock, RotateCcw, Save, Search, ShieldCheck, Undo2 } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import {
  getDefaultRolePermissionCodes,
  PERMISSION_GROUPS,
  ROLE_SCOPE_LABELS,
  ROLE_STATUS_LABELS,
  SYSTEM_PERMISSION_LIST,
} from '../data/role-mock-data';
import { assignPermissionsSchema } from '../schemas/assign-permissions.schema';
import type {
  RoleItem,
  RolePermissionItem,
  UpdateRolePermissionsRequest,
} from '../types/role.types';

export interface RolePermissionMatrixModalProps {
  role: RoleItem | null;
  isSubmitting?: boolean;
  serverError?: string | null;
  onClose: () => void;
  onSave: (payload: UpdateRolePermissionsRequest) => Promise<void> | void;
}

function sortCanonicalPermissionCodes(codes: Iterable<string>): string[] {
  const codeSet = new Set(codes);
  return SYSTEM_PERMISSION_LIST.filter((perm) => codeSet.has(perm.code)).map((perm) => perm.code);
}

export function RolePermissionMatrixModal({
  role,
  isSubmitting = false,
  serverError = null,
  onClose,
  onSave,
}: RolePermissionMatrixModalProps) {
  if (!role) return null;

  return (
    <Modal open={true} onClose={onClose} maxWidth="max-w-3xl">
      <RolePermissionMatrixForm
        key={`${role.id}-${role.updatedAt}`}
        role={role}
        isSubmitting={isSubmitting}
        serverError={serverError}
        onClose={onClose}
        onSave={onSave}
      />
    </Modal>
  );
}

interface RolePermissionMatrixFormProps {
  role: RoleItem;
  isSubmitting: boolean;
  serverError: string | null;
  onClose: () => void;
  onSave: (payload: UpdateRolePermissionsRequest) => Promise<void> | void;
}

function RolePermissionMatrixForm({
  role,
  isSubmitting,
  serverError,
  onClose,
  onSave,
}: RolePermissionMatrixFormProps) {
  const [selectedCodes, setSelectedCodes] = useState<string[]>(() =>
    sortCanonicalPermissionCodes(role.permissions.map((perm) => perm.code)),
  );
  const [searchQuery, setSearchQuery] = useState('');
  const [note, setNote] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const baselineCodes = useMemo(() => new Set(role.permissions.map((perm) => perm.code)), [role]);
  const selectedSet = useMemo(() => new Set(selectedCodes), [selectedCodes]);

  const addedCodes = useMemo(
    () => selectedCodes.filter((code) => !baselineCodes.has(code)),
    [selectedCodes, baselineCodes],
  );
  const revokedCodes = useMemo(
    () => Array.from(baselineCodes).filter((code) => !selectedSet.has(code)),
    [baselineCodes, selectedSet],
  );

  const isDirty = addedCodes.length > 0 || revokedCodes.length > 0 || note.trim().length > 0;

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

  const handleTogglePermission = (code: string) => {
    setValidationError(null);
    setSelectedCodes((prev) => {
      const next = new Set(prev);
      if (next.has(code)) {
        next.delete(code);
      } else {
        next.add(code);
      }
      return sortCanonicalPermissionCodes(next);
    });
  };

  const handleToggleModuleGroup = (groupPermissions: RolePermissionItem[]) => {
    setValidationError(null);
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

  const handleRestoreDefaults = () => {
    setValidationError(null);
    setSelectedCodes(sortCanonicalPermissionCodes(getDefaultRolePermissionCodes(role.code)));
  };

  const handleDiscardChanges = () => {
    setValidationError(null);
    setSelectedCodes(sortCanonicalPermissionCodes(role.permissions.map((perm) => perm.code)));
    setNote('');
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = assignPermissionsSchema.safeParse({
      roleId: role.id,
      roleCode: role.code,
      permissionCodes: selectedCodes,
      note: note.trim() ? note.trim() : undefined,
    });

    if (!parsed.success) {
      setValidationError(parsed.error.issues[0]?.message ?? 'Dữ liệu phân quyền không hợp lệ.');
      return;
    }

    setValidationError(null);
    await onSave(parsed.data);
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
            <h2 className="text-lg font-bold text-navy">{role.name}</h2>
            <span className="rounded border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-navy">
              {role.code}
            </span>
            <span
              className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-medium ${
                role.isSystemRole ? 'bg-sky-50 text-primary' : 'bg-slate-100 text-sub'
              }`}
            >
              {role.isSystemRole ? 'Hệ thống' : 'Tùy chỉnh'}
            </span>
            <span
              className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                role.status === 'ACTIVE'
                  ? 'bg-emerald-50 text-emerald-700'
                  : 'bg-slate-100 text-sub'
              }`}
            >
              {ROLE_STATUS_LABELS[role.status]}
            </span>
            <span className="text-xs text-sub">• {ROLE_SCOPE_LABELS[role.scope]}</span>
          </div>
          <p className="mt-1 text-xs font-medium text-sub">
            Ma trận gán / thu hồi quyền hạn truy cập (SCR-ADM-ROLE-02)
          </p>
        </div>
      </div>

      {role.status === 'INACTIVE' && (
        <div className="mt-4 flex items-start gap-2.5 rounded-lg border border-amber-200 bg-amber-50 px-3.5 py-2.5 text-xs text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <span>
            Vai trò đang ở trạng thái Tạm ngừng. Quyền hạn cập nhật sẽ có hiệu lực ngay khi kích
            hoạt lại vai trò.
          </span>
        </div>
      )}

      <div className="mt-3 rounded-lg border border-sky-100 bg-sky-50/60 p-3 text-xs text-ink">
        <p>
          <span className="font-semibold text-navy">Nguyên tắc phân tách nhiệm vụ (SoD): </span>
          {role.segregationNote}
        </p>
        <p className="mt-1 text-[11px] text-sub">
          Lưu ý kiểm soát nội bộ: Không cho phép gán đồng thời quyền{' '}
          <code className="font-mono font-semibold text-navy">PRESCRIPTION_ISSUE</code> và{' '}
          <code className="font-mono font-semibold text-navy">MEDICINE_DISPENSE</code> trên cùng một
          vai trò.
        </p>
      </div>

      {activeError && (
        <div role="alert" className="mt-3">
          <Alert tone="error">{activeError}</Alert>
        </div>
      )}

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
            aria-label="Tìm kiếm quyền hạn trong ma trận"
            placeholder="Tìm mã quyền, tên quyền hoặc phân hệ..."
            className="w-full rounded-lg border border-border bg-white py-1.5 pl-9 pr-3 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
          />
        </div>

        <div className="flex flex-wrap items-center gap-2 text-xs">
          <span className="rounded-md border border-border bg-white px-2.5 py-1 font-semibold text-navy">
            Đã chọn: {selectedCodes.length} / {SYSTEM_PERMISSION_LIST.length} quyền
          </span>
          {addedCodes.length > 0 && (
            <span className="rounded-md bg-emerald-50 px-2 py-1 font-semibold text-emerald-700">
              +{addedCodes.length} cấp mới
            </span>
          )}
          {revokedCodes.length > 0 && (
            <span className="rounded-md bg-rose-50 px-2 py-1 font-semibold text-rose-700">
              -{revokedCodes.length} thu hồi
            </span>
          )}
          <button
            type="button"
            onClick={handleRestoreDefaults}
            disabled={isSubmitting}
            className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 font-medium text-navy transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Khôi phục mặc định
          </button>
          <button
            type="button"
            onClick={handleDiscardChanges}
            disabled={!isDirty || isSubmitting}
            className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 font-medium text-sub transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
            Hoàn tác
          </button>
        </div>
      </div>

      <div className="mt-3 max-h-80 space-y-3 overflow-y-auto pr-1">
        {filteredGroups.length === 0 ? (
          <div className="rounded-xl border border-border bg-canvas px-4 py-8 text-center text-xs text-sub">
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
                    const wasGranted = baselineCodes.has(perm.code);
                    const isNewlyAdded = isChecked && !wasGranted;
                    const isRevoked = !isChecked && wasGranted;
                    const isMandatorySelfLockout =
                      role.code === 'SYSTEM_ADMIN' && perm.code === 'USER_MANAGE';

                    return (
                      <label
                        key={perm.code}
                        className="flex cursor-pointer flex-wrap items-center justify-between gap-2 px-4 py-2.5 text-sm transition-colors hover:bg-canvas"
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
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-medium text-navy">{perm.name}</span>
                              {isMandatorySelfLockout && (
                                <span className="inline-flex items-center gap-1 rounded bg-amber-50 px-1.5 py-0.5 text-[11px] font-medium text-amber-800">
                                  <Lock className="h-3 w-3" aria-hidden="true" />
                                  Bắt buộc (Chống tự khóa)
                                </span>
                              )}
                            </div>
                            <p className="text-xs text-sub">{perm.module}</p>
                          </div>
                        </div>

                        <div className="flex items-center gap-2">
                          {isNewlyAdded && (
                            <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                              Mới cấp
                            </span>
                          )}
                          {isRevoked && (
                            <span className="rounded-full bg-rose-50 px-2 py-0.5 text-[11px] font-semibold text-rose-700">
                              Thu hồi
                            </span>
                          )}
                          {!isNewlyAdded && !isRevoked && isChecked && (
                            <span className="rounded-full bg-sky-50 px-2 py-0.5 text-[11px] font-semibold text-primary">
                              Đang cấp
                            </span>
                          )}
                          <span className="rounded border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-navy">
                            {perm.code}
                          </span>
                        </div>
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
        <label htmlFor="assign-permission-note" className="block text-xs font-semibold text-navy">
          Ghi chú kiểm toán phân quyền
        </label>
        <input
          id="assign-permission-note"
          type="text"
          value={note}
          disabled={isSubmitting}
          onChange={(e) => setNote(e.target.value)}
          aria-label="Ghi chú kiểm toán phân quyền"
          maxLength={250}
          placeholder="Nhập lý do thay đổi phân quyền (ghi nhận vào audit_logs)..."
          className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none"
        />
      </div>

      <div className="mt-5 flex items-center justify-end gap-2 border-t border-border pt-4">
        <Button type="button" variant="secondary" onClick={onClose} disabled={isSubmitting}>
          Hủy
        </Button>
        <Button type="submit" loading={isSubmitting} disabled={isSubmitting}>
          <Save className="h-4 w-4" aria-hidden="true" />
          Lưu phân quyền
        </Button>
      </div>
    </form>
  );
}
