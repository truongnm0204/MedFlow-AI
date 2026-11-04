import { CheckCircle2, Lock, RotateCcw, ShieldAlert, ShieldCheck } from 'lucide-react';
import type { FormEvent } from 'react';
import { useMemo, useState } from 'react';

import {
  canAccessRoute,
  hasPermission,
  matchRouteAccessRule,
  resolveEffectivePermissions,
  ROUTE_ROLE_ACCESS_RULES,
  SYSTEM_ROLE_CODES,
} from '@/features/auth/utils/rbac';
import { getRoleDisplayLabel, getRoleLandingRoute } from '@/features/auth/utils/role-landing';
import { Alert, Button } from '@/shared/ui';

import { SYSTEM_PERMISSION_LIST } from '../data/role-mock-data';
import { roleAccessCheckSchema } from '../schemas/role-access-check.schema';
import type { RoleItem } from '../types/role.types';

export interface RoleAccessCheckPanelProps {
  roles: RoleItem[];
}

interface EvaluationResult {
  roleCode: string;
  roleLabel: string;
  targetRoute: string;
  isRouteAllowed: boolean;
  guardName: string;
  routeLabel: string;
  allowedRoles: readonly string[];
  fallbackRedirectRoute: string;
  effectivePermissions: string[];
  checkedPermissionCode: string | null;
  isPermissionGranted: boolean | null;
}

const ROUTE_PRESETS: readonly { path: string; label: string }[] = [
  { path: '/system-admin', label: 'System Admin (/system-admin)' },
  { path: '/hospital-admin', label: 'Hospital Admin (/hospital-admin)' },
  { path: '/admin/roles', label: 'Vai trò & Phân quyền (/admin/roles)' },
  { path: '/admin/staff/new', label: 'Tạo nhân sự (/admin/staff/new)' },
  { path: '/doctor', label: 'Bàn khám Bác sĩ (/doctor)' },
  { path: '/reception', label: 'Bàn Tiếp đón (/reception)' },
  { path: '/diagnostic', label: 'Cận lâm sàng (/diagnostic)' },
  { path: '/pharmacy', label: 'Nhà thuốc (/pharmacy)' },
  { path: '/appointments', label: 'Lịch hẹn bệnh nhân (/appointments)' },
];

export function RoleAccessCheckPanel({ roles }: RoleAccessCheckPanelProps) {
  const [roleCode, setRoleCode] = useState<string>('DOCTOR');
  const [targetRoute, setTargetRoute] = useState<string>('/doctor');
  const [requiredPermission, setRequiredPermission] = useState<string>('MEDICAL_RECORD_WRITE');
  const [validationError, setValidationError] = useState<string | null>(null);
  const [submittedInput, setSubmittedInput] = useState<{
    roleCode: string;
    targetRoute: string;
    requiredPermission: string;
  }>({
    roleCode: 'DOCTOR',
    targetRoute: '/doctor',
    requiredPermission: 'MEDICAL_RECORD_WRITE',
  });

  const roleOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const code of SYSTEM_ROLE_CODES) {
      map.set(code, getRoleDisplayLabel([code]));
    }
    for (const role of roles) {
      map.set(role.code, role.name);
    }
    return Array.from(map.entries()).map(([code, name]) => ({ code, name }));
  }, [roles]);

  const evaluation = useMemo<EvaluationResult>(() => {
    const activeRole = submittedInput.roleCode;
    const matchedRole = roles.find((item) => item.code === activeRole);
    const catalogPermCodes = matchedRole?.permissions.map((perm) => perm.code);
    const effectivePermissions = resolveEffectivePermissions([activeRole], catalogPermCodes);
    const matchedRule = matchRouteAccessRule(submittedInput.targetRoute);
    const isRouteAllowed = canAccessRoute(submittedInput.targetRoute, [activeRole]);
    const checkedPerm = submittedInput.requiredPermission.trim() || null;
    const isPermissionGranted = checkedPerm
      ? hasPermission([activeRole], checkedPerm, effectivePermissions)
      : null;

    return {
      roleCode: activeRole,
      roleLabel: matchedRole?.name ?? getRoleDisplayLabel([activeRole]),
      targetRoute: submittedInput.targetRoute,
      isRouteAllowed,
      guardName: matchedRule?.guardName ?? 'Public / Cổng bệnh nhân',
      routeLabel: matchedRule?.label ?? 'Màn hình chung / Cổng dịch vụ bệnh nhân',
      allowedRoles: matchedRule?.allowedRoles ?? SYSTEM_ROLE_CODES,
      fallbackRedirectRoute: getRoleLandingRoute([activeRole]),
      effectivePermissions,
      checkedPermissionCode: checkedPerm,
      isPermissionGranted,
    };
  }, [roles, submittedInput]);

  const handleEvaluate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = roleAccessCheckSchema.safeParse({
      roleCode,
      targetRoute,
      requiredPermission,
    });

    if (!parsed.success) {
      const firstIssue = parsed.error.issues[0]?.message ?? 'Dữ liệu kiểm tra không hợp lệ.';
      setValidationError(firstIssue);
      return;
    }

    setValidationError(null);
    setSubmittedInput({
      roleCode: parsed.data.roleCode,
      targetRoute: parsed.data.targetRoute,
      requiredPermission: parsed.data.requiredPermission ?? '',
    });
  };

  const handleReset = () => {
    setRoleCode('DOCTOR');
    setTargetRoute('/doctor');
    setRequiredPermission('MEDICAL_RECORD_WRITE');
    setValidationError(null);
    setSubmittedInput({
      roleCode: 'DOCTOR',
      targetRoute: '/doctor',
      requiredPermission: 'MEDICAL_RECORD_WRITE',
    });
  };

  return (
    <div className="space-y-6">
      <div className="rounded-xl border border-border bg-white p-5 shadow-2xs">
        <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
          <div>
            <h2 className="text-base font-bold text-navy">
              Bộ kiểm tra thực thi quyền truy cập theo vai trò (Enforce Role-based Access)
            </h2>
            <p className="mt-1 text-xs text-sub">
              Kiểm tra quy tắc bảo vệ đường dẫn (Route Guards), điều hướng an toàn khi trái quyền và
              ẩn/hiện thao tác trên giao diện theo ma trận phân quyền RBAC.
            </p>
          </div>
          <span className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary">
            <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
            Two-Layer RBAC Enforcement
          </span>
        </div>

        <form
          aria-label="Biểu mẫu kiểm tra thực thi phân quyền RBAC"
          onSubmit={handleEvaluate}
          noValidate
          className="mt-4 space-y-4"
        >
          {validationError && (
            <div role="alert">
              <Alert tone="error">{validationError}</Alert>
            </div>
          )}

          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <div>
              <label
                htmlFor="rbac-check-role"
                className="mb-1.5 block text-xs font-semibold text-navy"
              >
                Vai trò người dùng (Role)
              </label>
              <select
                id="rbac-check-role"
                aria-label="Chọn vai trò kiểm tra"
                value={roleCode}
                onChange={(event) => setRoleCode(event.target.value)}
                className="h-10 w-full rounded-lg border border-border bg-white px-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
              >
                <option value="">-- Chọn vai trò --</option>
                {roleOptions.map((option) => (
                  <option key={option.code} value={option.code}>
                    {option.code} — {option.name}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label
                htmlFor="rbac-check-route"
                className="mb-1.5 block text-xs font-semibold text-navy"
              >
                Đường dẫn màn hình đích (Target Route)
              </label>
              <input
                id="rbac-check-route"
                type="text"
                aria-label="Đường dẫn màn hình kiểm tra"
                value={targetRoute}
                onChange={(event) => setTargetRoute(event.target.value)}
                placeholder="Ví dụ: /system-admin hoặc /doctor"
                className="h-10 w-full rounded-lg border border-border bg-white px-3 text-sm text-navy placeholder:text-sub focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
            </div>

            <div>
              <label
                htmlFor="rbac-check-permission"
                className="mb-1.5 block text-xs font-semibold text-navy"
              >
                Quyền thao tác trên màn hình (Action Permission)
              </label>
              <select
                id="rbac-check-permission"
                aria-label="Quyền hạn thao tác cần kiểm tra"
                value={requiredPermission}
                onChange={(event) => setRequiredPermission(event.target.value)}
                className="h-10 w-full rounded-lg border border-border bg-white px-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
              >
                <option value="">-- Không kiểm tra quyền lẻ --</option>
                {SYSTEM_PERMISSION_LIST.map((perm) => (
                  <option key={perm.code} value={perm.code}>
                    {perm.code} — {perm.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 pt-1">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-xs font-medium text-sub">Chọn nhanh đường dẫn:</span>
              {ROUTE_PRESETS.map((preset) => (
                <button
                  key={preset.path}
                  type="button"
                  onClick={() => setTargetRoute(preset.path)}
                  className={`rounded-md border px-2 py-1 text-xs font-medium transition-colors ${
                    targetRoute === preset.path
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-canvas text-sub hover:text-navy'
                  }`}
                >
                  {preset.path}
                </button>
              ))}
            </div>

            <div className="flex items-center gap-2">
              <Button type="button" variant="secondary" size="sm" onClick={handleReset}>
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                Đặt lại
              </Button>
              <Button type="submit" size="sm">
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                Kiểm tra quyền truy cập
              </Button>
            </div>
          </div>
        </form>
      </div>

      <div
        aria-label="Kết quả kiểm tra quyền truy cập"
        className="grid grid-cols-1 gap-4 lg:grid-cols-2"
      >
        <div className="rounded-xl border border-border bg-white p-5 shadow-2xs">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-bold text-navy">1. Quyết định Route Guard</h3>
            {evaluation.isRouteAllowed ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                ĐƯỢC PHÉP TRUY CẬP
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 rounded-full bg-rose-50 px-3 py-1 text-xs font-bold text-rose-700">
                <ShieldAlert className="h-3.5 w-3.5" aria-hidden="true" />
                CHẶN TRUY CẬP — CHUYỂN HƯỚNG AN TOÀN
              </span>
            )}
          </div>

          <dl className="mt-4 grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
            <div className="rounded-lg bg-canvas p-3">
              <dt className="font-medium text-sub">Vai trò đang kiểm tra</dt>
              <dd className="mt-1 font-bold text-navy">
                {evaluation.roleCode} ({evaluation.roleLabel})
              </dd>
            </div>
            <div className="rounded-lg bg-canvas p-3">
              <dt className="font-medium text-sub">Đường dẫn & Bộ bảo vệ (Guard)</dt>
              <dd className="mt-1 font-bold text-navy">
                {evaluation.targetRoute} · {evaluation.guardName}
              </dd>
            </div>
            <div className="rounded-lg bg-canvas p-3">
              <dt className="font-medium text-sub">Vai trò được phép vào màn hình</dt>
              <dd className="mt-1 font-semibold text-ink">{evaluation.allowedRoles.join(', ')}</dd>
            </div>
            <div className="rounded-lg bg-canvas p-3">
              <dt className="font-medium text-sub">Hành vi khi truy cập trái phép</dt>
              <dd className="mt-1 font-semibold text-ink">
                {evaluation.isRouteAllowed
                  ? `Cho phép mở ${evaluation.routeLabel}`
                  : `Tự động chuyển hướng về ${evaluation.fallbackRedirectRoute}`}
              </dd>
            </div>
          </dl>
        </div>

        <div className="rounded-xl border border-border bg-white p-5 shadow-2xs">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-bold text-navy">
              2. Hiển thị thao tác trên UI (Action Gate)
            </h3>
            {evaluation.checkedPermissionCode ? (
              evaluation.isPermissionGranted ? (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">
                  <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                  ĐƯỢC HIỂN THỊ THAO TÁC
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-3 py-1 text-xs font-bold text-amber-700">
                  <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                  ẨN THAO TÁC TRÁI QUYỀN
                </span>
              )
            ) : (
              <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-sub">
                Không chọn quyền lẻ
              </span>
            )}
          </div>

          <p className="mt-3 text-xs text-sub">
            {evaluation.checkedPermissionCode ? (
              evaluation.isPermissionGranted ? (
                <>
                  Vai trò <strong className="text-navy">{evaluation.roleCode}</strong> sở hữu quyền{' '}
                  <strong className="text-navy">{evaluation.checkedPermissionCode}</strong> nên các
                  nút thao tác tương ứng được hiển thị.
                </>
              ) : (
                <>
                  Vai trò <strong className="text-navy">{evaluation.roleCode}</strong> không có
                  quyền <strong className="text-navy">{evaluation.checkedPermissionCode}</strong>{' '}
                  nên thành phần <code className="text-navy">&lt;CanAccess&gt;</code> sẽ tự động ẩn
                  thao tác trên giao diện.
                </>
              )
            ) : (
              'Chọn một mã quyền hạn để kiểm tra trạng thái ẩn/hiện nút chức năng trên màn hình.'
            )}
          </p>

          <div className="mt-4">
            <p className="text-xs font-semibold text-navy">
              Danh sách quyền hạt nhân hiệu lực ({evaluation.effectivePermissions.length} quyền):
            </p>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {evaluation.effectivePermissions.map((permCode) => (
                <span
                  key={permCode}
                  className="inline-flex rounded-md border border-border bg-canvas px-2 py-0.5 text-[11px] font-semibold text-navy"
                >
                  {permCode}
                </span>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="overflow-hidden rounded-xl border border-border bg-white shadow-2xs">
        <div className="border-b border-border px-5 py-3.5">
          <h3 className="text-sm font-bold text-navy">
            Bảng tổng hợp quyền truy cập các phân hệ đối với vai trò {evaluation.roleCode}
          </h3>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="bg-canvas font-semibold uppercase tracking-wide text-sub">
              <tr>
                <th scope="col" className="px-4 py-3">
                  ĐƯỜNG DẪN PHÂN HỆ
                </th>
                <th scope="col" className="px-4 py-3">
                  MÀN HÌNH CHỨC NĂNG
                </th>
                <th scope="col" className="px-4 py-3">
                  GUARD BẢO VỆ
                </th>
                <th scope="col" className="px-4 py-3">
                  VAI TRÒ YÊU CẦU
                </th>
                <th scope="col" className="px-4 py-3">
                  TRẠNG THÁI TRUY CẬP
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {ROUTE_ROLE_ACCESS_RULES.map((rule) => {
                const allowed = canAccessRoute(rule.prefix, [evaluation.roleCode]);
                return (
                  <tr key={rule.prefix} className="hover:bg-canvas">
                    <td className="px-4 py-2.5 font-mono font-semibold text-navy">{rule.prefix}</td>
                    <td className="px-4 py-2.5 text-ink">{rule.label}</td>
                    <td className="px-4 py-2.5 font-mono text-sub">{rule.guardName}</td>
                    <td className="px-4 py-2.5 text-sub">{rule.allowedRoles.join(', ')}</td>
                    <td className="px-4 py-2.5">
                      {allowed ? (
                        <span className="inline-flex rounded-full bg-emerald-50 px-2.5 py-0.5 font-semibold text-emerald-700">
                          Cho phép
                        </span>
                      ) : (
                        <span className="inline-flex rounded-full bg-rose-50 px-2.5 py-0.5 font-semibold text-rose-700">
                          Chặn → {evaluation.fallbackRedirectRoute}
                        </span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
