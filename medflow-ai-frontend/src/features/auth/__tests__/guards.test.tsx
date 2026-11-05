import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import {
  GuestOnly,
  RequireAdmin,
  RequireAuth,
  RequireDiagnostic,
  RequireDoctor,
  RequirePharmacy,
  RequireReception,
  RequireRole,
  RequireSystemAdmin,
} from '@/app/router/guards';
import { CanAccess } from '../hooks/use-role-access';
import { useAuthStore } from '../store/auth.store';
import {
  canAccessRoute,
  hasAnyRole,
  hasPermission,
  resolveEffectivePermissions,
} from '../utils/rbac';

function renderWithRouter(ui: React.ReactElement, initial = '/protected') {
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route path="/protected" element={ui} />
        <Route path="/login" element={<div>LOGIN PAGE</div>} />
        <Route path="/" element={<div>HOME PAGE</div>} />
        <Route path="/doctor" element={<div>DOCTOR WORKBENCH</div>} />
        <Route path="/reception" element={<div>RECEPTION WORKBENCH</div>} />
        <Route path="/diagnostic" element={<div>DIAGNOSTIC WORKBENCH</div>} />
        <Route path="/pharmacy" element={<div>PHARMACY WORKBENCH</div>} />
        <Route path="/hospital-admin" element={<div>HOSPITAL ADMIN PORTAL</div>} />
        <Route path="/system-admin" element={<div>SYSTEM ADMIN PORTAL</div>} />
        <Route path="/appointments" element={<div>APPOINTMENTS PAGE</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RequireAuth', () => {
  it('chưa login → redirect /login?returnUrl=', () => {
    useAuthStore.setState({ isAuthenticated: false });
    renderWithRouter(<RequireAuth>SECRET</RequireAuth>);
    expect(screen.queryByText('SECRET')).toBeNull();
    expect(screen.getByText('LOGIN PAGE')).toBeDefined();
  });

  it('đã login → render children', () => {
    useAuthStore.setState({ isAuthenticated: true });
    renderWithRouter(<RequireAuth>SECRET</RequireAuth>);
    expect(screen.getByText('SECRET')).toBeDefined();
  });
});

describe('GuestOnly', () => {
  it('đã login (PATIENT / không role) → redirect /', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: { id: 1, email: 'p@test.com', fullName: 'BN', roles: ['PATIENT'], patientCode: 'P1' },
    });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>);
    expect(screen.queryByText('GUEST FORM')).toBeNull();
    expect(screen.getByText('HOME PAGE')).toBeDefined();
  });

  it.each([
    ['DOCTOR', 'DOCTOR WORKBENCH'],
    ['RECEPTION', 'RECEPTION WORKBENCH'],
    ['DIAGNOSTIC_STAFF', 'DIAGNOSTIC WORKBENCH'],
    ['PHARMACY_STAFF', 'PHARMACY WORKBENCH'],
    ['HOSPITAL_ADMIN', 'HOSPITAL ADMIN PORTAL'],
    ['SYSTEM_ADMIN', 'SYSTEM ADMIN PORTAL'],
  ])('đã login với role %s → redirect tới dashboard tương ứng (%s)', (role, expectedText) => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 2,
        email: 'staff@medflow.test',
        fullName: 'Nhân viên',
        roles: [role],
        patientCode: null,
        staffCode: 'STF-001',
      },
    });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>);
    expect(screen.queryByText('GUEST FORM')).toBeNull();
    expect(screen.getByText(expectedText)).toBeDefined();
  });

  it('đã login kèm ?returnUrl=/appointments hợp lệ → ưu tiên returnUrl hơn role default', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 2,
        email: 'doctor@medflow.test',
        fullName: 'BS A',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
    });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>, '/protected?returnUrl=/appointments');
    expect(screen.getByText('APPOINTMENTS PAGE')).toBeDefined();
  });

  it('đã login kèm ?returnUrl=/system-admin nhưng role là DOCTOR → chặn returnUrl trái quyền và về /doctor', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 2,
        email: 'doctor@medflow.test',
        fullName: 'BS A',
        roles: ['DOCTOR'],
        patientCode: null,
        staffCode: 'STF-001',
      },
    });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>, '/protected?returnUrl=/system-admin');
    expect(screen.getByText('DOCTOR WORKBENCH')).toBeDefined();
  });

  it('chưa login → render children', () => {
    useAuthStore.setState({ isAuthenticated: false, user: null });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>);
    expect(screen.getByText('GUEST FORM')).toBeDefined();
  });
});

describe('Role-specific Route Guards & CanAccess', () => {
  it('RequireAdmin cho phép HOSPITAL_ADMIN và SYSTEM_ADMIN, chặn DOCTOR về /doctor', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 3,
        email: 'hadmin@medflow.test',
        fullName: 'Admin CS',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
    });
    const { unmount } = renderWithRouter(<RequireAdmin>ADMIN AREA</RequireAdmin>);
    expect(screen.getByText('ADMIN AREA')).toBeDefined();
    unmount();

    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 4,
        email: 'doc@medflow.test',
        fullName: 'BS',
        roles: ['DOCTOR'],
        patientCode: null,
      },
    });
    renderWithRouter(<RequireAdmin>ADMIN AREA</RequireAdmin>);
    expect(screen.queryByText('ADMIN AREA')).toBeNull();
    expect(screen.getByText('DOCTOR WORKBENCH')).toBeDefined();
  });

  it('RequireSystemAdmin chỉ cho phép SYSTEM_ADMIN, chuyển hướng HOSPITAL_ADMIN về /hospital-admin', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 3,
        email: 'hadmin@medflow.test',
        fullName: 'Admin CS',
        roles: ['HOSPITAL_ADMIN'],
        patientCode: null,
      },
    });
    renderWithRouter(<RequireSystemAdmin>SYS ADMIN AREA</RequireSystemAdmin>);
    expect(screen.queryByText('SYS ADMIN AREA')).toBeNull();
    expect(screen.getByText('HOSPITAL ADMIN PORTAL')).toBeDefined();
  });

  it('RequireReception, RequireDoctor, RequireDiagnostic, RequirePharmacy chặn role không khớp', () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 1,
        email: 'patient@medflow.test',
        fullName: 'Bệnh nhân',
        roles: ['PATIENT'],
        patientCode: 'PAT-01',
      },
    });

    const { unmount: u1 } = renderWithRouter(<RequireReception>RECEPTION ONLY</RequireReception>);
    expect(screen.queryByText('RECEPTION ONLY')).toBeNull();
    expect(screen.getByText('HOME PAGE')).toBeDefined();
    u1();

    const { unmount: u2 } = renderWithRouter(<RequireDoctor>DOCTOR ONLY</RequireDoctor>);
    expect(screen.queryByText('DOCTOR ONLY')).toBeNull();
    expect(screen.getByText('HOME PAGE')).toBeDefined();
    u2();

    const { unmount: u3 } = renderWithRouter(
      <RequireDiagnostic>DIAGNOSTIC ONLY</RequireDiagnostic>,
    );
    expect(screen.queryByText('DIAGNOSTIC ONLY')).toBeNull();
    expect(screen.getByText('HOME PAGE')).toBeDefined();
    u3();

    const { unmount: u4 } = renderWithRouter(<RequirePharmacy>PHARMACY ONLY</RequirePharmacy>);
    expect(screen.queryByText('PHARMACY ONLY')).toBeNull();
    expect(screen.getByText('HOME PAGE')).toBeDefined();
    u4();

    renderWithRouter(
      <RequireRole allowedRoles={['SYSTEM_ADMIN']} fallbackTo="/login">
        CUSTOM GUARD
      </RequireRole>,
    );
    expect(screen.getByText('LOGIN PAGE')).toBeDefined();
  });

  it('CanAccess và rbac helpers kiểm tra chính xác role và permission', () => {
    expect(hasAnyRole(['doctor'], ['DOCTOR', 'RECEPTION'])).toBe(true);
    expect(resolveEffectivePermissions(['SYSTEM_ADMIN'])).toEqual([
      'HOSPITAL_CONFIG',
      'USER_MANAGE',
      'AUDIT_READ',
    ]);
    expect(hasPermission(['DOCTOR'], 'PRESCRIPTION_ISSUE')).toBe(true);
    expect(hasPermission(['DOCTOR'], 'USER_MANAGE')).toBe(false);
    expect(canAccessRoute('/admin/staff/10/edit', ['HOSPITAL_ADMIN'])).toBe(false);
    expect(canAccessRoute('/admin/staff/10/edit', ['SYSTEM_ADMIN'])).toBe(true);

    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        id: 2,
        email: 'doctor@medflow.test',
        fullName: 'BS A',
        roles: ['DOCTOR'],
        patientCode: null,
      },
    });

    render(
      <div>
        <CanAccess requiredPermissions={['PRESCRIPTION_ISSUE']}>
          <button type="button">Kê đơn thuốc</button>
        </CanAccess>
        <CanAccess allowedRoles={['SYSTEM_ADMIN']} fallback={<span>Không đủ quyền quản trị</span>}>
          <button type="button">Xóa vai trò</button>
        </CanAccess>
      </div>,
    );

    expect(screen.getByRole('button', { name: 'Kê đơn thuốc' })).toBeDefined();
    expect(screen.queryByRole('button', { name: 'Xóa vai trò' })).toBeNull();
    expect(screen.getByText('Không đủ quyền quản trị')).toBeDefined();
  });
});
