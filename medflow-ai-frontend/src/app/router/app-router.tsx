import { Route, Routes } from 'react-router-dom';

import { LoginPage } from '@/features/auth/pages/login-page';
import { RegisterPage } from '@/features/auth/pages/register-page';
import { VerifyOtpPage } from '@/features/auth/pages/verify-otp-page';
import { FamilyProfileDetailPage } from '@/features/family/pages/family-profile-detail-page';
import { FamilyProfileListPage } from '@/features/family/pages/family-profile-list-page';
import { HomePage } from '@/features/home/pages/home-page';
import { PatientProfilePage } from '@/features/patient/pages/patient-profile-page';
import { ReceptionDashboardPage } from '@/features/reception/pages/reception-dashboard-page';
import { NotFoundPage } from '@/shared/components/layout/not-found-page';

import { GuestOnly, RequireAuth } from './guards';

export function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route
        path="/login"
        element={
          <GuestOnly>
            <LoginPage />
          </GuestOnly>
        }
      />
      <Route
        path="/register"
        element={
          <GuestOnly>
            <RegisterPage />
          </GuestOnly>
        }
      />
      <Route
        path="/reception"
        element={
          <RequireAuth>
            <ReceptionDashboardPage />
          </RequireAuth>
        }
      />
      <Route
        path="/verify-otp"
        element={
          <GuestOnly>
            <VerifyOtpPage />
          </GuestOnly>
        }
      />
      <Route path="/profile" element={<PatientProfilePage />} />
      <Route path="/patient/profile" element={<PatientProfilePage />} />
      <Route path="/family" element={<FamilyProfileListPage />} />
      <Route path="/patient/family" element={<FamilyProfileListPage />} />
      <Route path="/family/:memberId" element={<FamilyProfileDetailPage />} />
      <Route path="/patient/family/:memberId" element={<FamilyProfileDetailPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
