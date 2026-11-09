import { Lock, Pencil, Save, Undo2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { ROLE_SCOPE_LABELS, ROLE_STATUS_LABELS } from '../data/role-mock-data';
import { updateRoleSchema } from '../schemas/update-role.schema';
import type { RoleItem, RoleScope, RoleStatus, UpdateRoleRequest } from '../types/role.types';

export interface RoleEditModalProps {
  role: RoleItem | null;
  isSubmitting?: boolean;
  serverError?: string | null;
  onClose: () => void;
  onSave: (payload: UpdateRoleRequest) => Promise<void> | void;
}

const SCOPE_OPTIONS = Object.keys(ROLE_SCOPE_LABELS) as RoleScope[];
const STATUS_OPTIONS = Object.keys(ROLE_STATUS_LABELS) as RoleStatus[];

export function RoleEditModal({
  role,
  isSubmitting = false,
  serverError = null,
  onClose,
  onSave,
}: RoleEditModalProps) {
  if (!role) return null;

  return (
    <Modal open={true} onClose={onClose} maxWidth="max-w-2xl">
      <RoleEditForm
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

interface RoleEditFormProps {
  role: RoleItem;
  isSubmitting: boolean;
  serverError: string | null;
  onClose: () => void;
  onSave: (payload: UpdateRoleRequest) => Promise<void> | void;
}

function RoleEditForm({ role, isSubmitting, serverError, onClose, onSave }: RoleEditFormProps) {
  const [code, setCode] = useState(role.code);
  const [name, setName] = useState(role.name);
  const [scope, setScope] = useState<RoleScope>(role.scope);
  const [status, setStatus] = useState<RoleStatus>(role.status);
  const [description, setDescription] = useState(role.description);
  const [segregationNote, setSegregationNote] = useState(role.segregationNote);
  const [note, setNote] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);

  const isSystemAdminRole = role.code.trim().toUpperCase() === 'SYSTEM_ADMIN';

  const isDirty =
    code.trim().toUpperCase() !== role.code ||
    name.trim() !== role.name ||
    scope !== role.scope ||
    status !== role.status ||
    description.trim() !== role.description ||
    segregationNote.trim() !== role.segregationNote ||
    note.trim().length > 0;

  const handleReset = () => {
    setCode(role.code);
    setName(role.name);
    setScope(role.scope);
    setStatus(role.status);
    setDescription(role.description);
    setSegregationNote(role.segregationNote);
    setNote('');
    setValidationError(null);
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const parsed = updateRoleSchema.safeParse({
      roleId: role.id,
      originalCode: role.code,
      isSystemRole: role.isSystemRole,
      code,
      name,
      scope,
      status,
      description,
      segregationNote,
      note: note.trim() ? note.trim() : undefined,
    });

    if (!parsed.success) {
      setValidationError(
        parsed.error.issues[0]?.message ?? 'Thông tin cập nhật vai trò không hợp lệ.',
      );
      return;
    }

    setValidationError(null);
    await onSave({
      roleId: role.id,
      code: parsed.data.code,
      name: parsed.data.name,
      scope: parsed.data.scope,
      status: parsed.data.status,
      description: parsed.data.description,
      segregationNote: parsed.data.segregationNote,
      note: parsed.data.note || undefined,
    });
  };

  const activeError = validationError ?? serverError;

  return (
    <form onSubmit={(e) => void handleSubmit(e)} noValidate className="p-6">
      <div className="flex items-start gap-3 border-b border-border pb-4 pr-8">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
          <Pencil className="h-5 w-5" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-bold text-navy">Cập nhật thông tin vai trò</h2>
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
          </div>
          <p className="mt-1 text-xs font-medium text-sub">
            Chỉnh sửa cấu hình định danh, phân hệ, trạng thái và nguyên tắc phân tách nhiệm vụ (SoD)
            của vai trò RBAC.
          </p>
        </div>
      </div>

      {role.isSystemRole && (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50/80 px-3.5 py-2.5 text-xs text-amber-900">
          <Lock className="mt-0.5 h-4 w-4 shrink-0 text-amber-700" aria-hidden="true" />
          <div>
            <span className="font-semibold">Bảo vệ vai trò hệ thống mặc định: </span>
            Mã định danh (<code className="font-mono font-semibold">{role.code}</code>) và trạng
            thái hoạt động (<span className="font-semibold">Đang hoạt động</span>) được khóa cố định
            để ngăn ngừa tự khóa quyền RBAC.
            {isSystemAdminRole &&
              ' Vai trò SYSTEM_ADMIN bắt buộc thuộc nhóm phân hệ Quản trị hệ thống.'}
          </div>
        </div>
      )}

      {activeError && (
        <div className="mt-4" role="alert">
          <Alert tone="error">{activeError}</Alert>
        </div>
      )}

      <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="edit-role-code" className="block text-xs font-semibold text-navy">
            Mã vai trò <span className="text-red-600">*</span>
          </label>
          <input
            id="edit-role-code"
            type="text"
            value={code}
            disabled={role.isSystemRole || isSubmitting}
            onChange={(e) => {
              setValidationError(null);
              setCode(e.target.value.toUpperCase());
            }}
            aria-label="Mã vai trò"
            maxLength={50}
            placeholder="VD: CLINICAL_AUDITOR"
            className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 font-mono text-xs uppercase text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas disabled:text-sub"
          />
        </div>

        <div>
          <label htmlFor="edit-role-name" className="block text-xs font-semibold text-navy">
            Tên vai trò <span className="text-red-600">*</span>
          </label>
          <input
            id="edit-role-name"
            type="text"
            value={name}
            disabled={isSubmitting}
            onChange={(e) => {
              setValidationError(null);
              setName(e.target.value);
            }}
            aria-label="Tên vai trò"
            maxLength={120}
            placeholder="Nhập tên hiển thị của vai trò..."
            className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas"
          />
        </div>

        <div>
          <label htmlFor="edit-role-scope" className="block text-xs font-semibold text-navy">
            Nhóm phân hệ <span className="text-red-600">*</span>
          </label>
          <select
            id="edit-role-scope"
            value={scope}
            disabled={isSystemAdminRole || isSubmitting}
            onChange={(e) => {
              setValidationError(null);
              setScope(e.target.value as RoleScope);
            }}
            aria-label="Nhóm phân hệ vai trò"
            className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas disabled:text-sub"
          >
            {SCOPE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {ROLE_SCOPE_LABELS[option]} ({option})
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="edit-role-status" className="block text-xs font-semibold text-navy">
            Trạng thái hoạt động <span className="text-red-600">*</span>
          </label>
          <select
            id="edit-role-status"
            value={status}
            disabled={role.isSystemRole || isSubmitting}
            onChange={(e) => {
              setValidationError(null);
              setStatus(e.target.value as RoleStatus);
            }}
            aria-label="Trạng thái vai trò"
            className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas disabled:text-sub"
          >
            {STATUS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {ROLE_STATUS_LABELS[option]} ({option})
              </option>
            ))}
          </select>
        </div>
      </div>

      <div className="mt-4">
        <label htmlFor="edit-role-description" className="block text-xs font-semibold text-navy">
          Mô tả chức năng & nhiệm vụ <span className="text-red-600">*</span>
        </label>
        <textarea
          id="edit-role-description"
          rows={3}
          value={description}
          disabled={isSubmitting}
          onChange={(e) => {
            setValidationError(null);
            setDescription(e.target.value);
          }}
          aria-label="Mô tả vai trò"
          maxLength={500}
          placeholder="Nhập mô tả phạm vi trách nhiệm của vai trò..."
          className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas"
        />
      </div>

      <div className="mt-4">
        <label htmlFor="edit-role-sod" className="block text-xs font-semibold text-navy">
          Ghi chú phân tách nhiệm vụ (SoD) <span className="text-red-600">*</span>
        </label>
        <textarea
          id="edit-role-sod"
          rows={2}
          value={segregationNote}
          disabled={isSubmitting}
          onChange={(e) => {
            setValidationError(null);
            setSegregationNote(e.target.value);
          }}
          aria-label="Ghi chú phân tách nhiệm vụ (SoD)"
          maxLength={500}
          placeholder="Nhập nguyên tắc kiểm soát xung đột quyền hạn (Segregation of Duties)..."
          className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas"
        />
      </div>

      <div className="mt-4">
        <label htmlFor="edit-role-note" className="block text-xs font-semibold text-navy">
          Ghi chú kiểm toán cập nhật
        </label>
        <input
          id="edit-role-note"
          type="text"
          value={note}
          disabled={isSubmitting}
          onChange={(e) => setNote(e.target.value)}
          aria-label="Ghi chú kiểm toán cập nhật"
          maxLength={250}
          placeholder="Nhập lý do cập nhật thông tin vai trò (ghi nhận vào audit_logs)..."
          className="mt-1.5 w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder:text-sub focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:bg-canvas"
        />
      </div>

      <div className="mt-5 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-4">
        <button
          type="button"
          disabled={!isDirty || isSubmitting}
          onClick={handleReset}
          className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
          Hoàn tác
        </button>

        <div className="flex items-center gap-2">
          <Button type="button" variant="secondary" onClick={onClose} disabled={isSubmitting}>
            Hủy
          </Button>
          <Button type="submit" loading={isSubmitting} disabled={isSubmitting}>
            <Save className="h-4 w-4" aria-hidden="true" />
            Lưu thay đổi
          </Button>
        </div>
      </div>
    </form>
  );
}
