import { ShieldCheck } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { ROLE_SCOPE_LABELS, ROLE_STATUS_LABELS } from '../data/role-mock-data';
import type { RoleItem } from '../types/role.types';

interface RoleDetailModalProps {
  role: RoleItem | null;
  onClose: () => void;
}

export function RoleDetailModal({ role, onClose }: RoleDetailModalProps) {
  if (!role) return null;

  return (
    <Modal open={Boolean(role)} onClose={onClose} maxWidth="max-w-2xl">
      <div className="p-6">
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
                className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                  role.status === 'ACTIVE'
                    ? 'bg-emerald-50 text-emerald-700'
                    : 'bg-slate-100 text-sub'
                }`}
              >
                {ROLE_STATUS_LABELS[role.status]}
              </span>
            </div>
            <p className="mt-1 text-sm text-sub">{role.description}</p>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
          <div className="rounded-lg border border-border bg-canvas p-3">
            <p className="text-xs font-medium text-sub">Nhóm phân hệ</p>
            <p className="mt-1 text-sm font-semibold text-navy">{ROLE_SCOPE_LABELS[role.scope]}</p>
          </div>
          <div className="rounded-lg border border-border bg-canvas p-3">
            <p className="text-xs font-medium text-sub">Tài khoản được gán</p>
            <p className="mt-1 text-sm font-semibold text-navy">
              {role.assignedUsersCount} tài khoản
            </p>
          </div>
          <div className="rounded-lg border border-border bg-canvas p-3">
            <p className="text-xs font-medium text-sub">Cập nhật gần nhất</p>
            <p className="mt-1 text-sm font-semibold text-navy">{role.updatedAt}</p>
          </div>
        </div>

        <div className="mt-4 rounded-lg border border-sky-100 bg-sky-50/60 p-3 text-xs text-ink">
          <span className="font-semibold text-navy">Nguyên tắc phân tách nhiệm vụ (SoD): </span>
          {role.segregationNote}
        </div>

        <div className="mt-5">
          <h3 className="text-xs font-semibold uppercase tracking-wide text-sub">
            Danh sách quyền hạn được cấp ({role.permissions.length})
          </h3>
          <div className="mt-2 max-h-64 divide-y divide-border overflow-y-auto rounded-xl border border-border">
            {role.permissions.map((perm) => (
              <div
                key={perm.code}
                className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 text-sm"
              >
                <div>
                  <p className="font-medium text-navy">{perm.name}</p>
                  <p className="text-xs text-sub">{perm.module}</p>
                </div>
                <span className="rounded border border-border bg-canvas px-2 py-0.5 font-mono text-xs font-semibold text-primary">
                  {perm.code}
                </span>
              </div>
            ))}
          </div>
        </div>

        <div className="mt-6 flex justify-end border-t border-border pt-4">
          <Button type="button" variant="secondary" onClick={onClose}>
            Đóng
          </Button>
        </div>
      </div>
    </Modal>
  );
}
