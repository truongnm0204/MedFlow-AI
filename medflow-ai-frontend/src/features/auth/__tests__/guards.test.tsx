import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { GuestOnly, RequireAuth } from '@/app/router/guards';
import { useAuthStore } from '../store/auth.store';

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

  it('chưa login → render children', () => {
    useAuthStore.setState({ isAuthenticated: false, user: null });
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>);
    expect(screen.getByText('GUEST FORM')).toBeDefined();
  });
});
