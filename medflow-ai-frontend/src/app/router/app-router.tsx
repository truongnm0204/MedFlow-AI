import { Route, Routes } from 'react-router-dom';

import { DoctorCreatePage } from '@/features/admin-doctors/pages/doctor-create-page';
import { DoctorEditModal } from '@/features/admin-doctors/pages/doctor-edit-modal';
import { DoctorListPage } from '@/features/admin-doctors/pages/doctor-list-page';
import { OperationalReportsPage } from '@/features/admin-reports/pages/operational-reports-page';
import { ExaminationServiceCreatePage } from '@/features/admin-services/pages/examination-service-create-page';
import { ExaminationServiceListPage } from '@/features/admin-services/pages/examination-service-list-page';
import { ServiceEditModal } from '@/features/admin-services/pages/service-edit-modal';
import { StaffAccountListPage } from '@/features/admin-staff/pages/staff-account-list-page';
import { AppointmentSlotBookingPage } from '@/features/appointment/pages/appointment-slot-booking-page';
import { DoctorSearchPage } from '@/features/appointment/pages/doctor-search-page';
import { ExaminationServiceCatalogPage } from '@/features/appointment/pages/examination-service-catalog-page';
import { ServiceSlotBookingPage } from '@/features/appointment/pages/service-slot-booking-page';
import { LoginPage } from '@/features/auth/pages/login-page';
import { RegisterPage } from '@/features/auth/pages/register-page';
import { VerifyOtpPage } from '@/features/auth/pages/verify-otp-page';
import { DiagnosticDashboardPage } from '@/features/diagnostic/pages/diagnostic-dashboard-page';
import { DoctorDashboardPage } from '@/features/doctor/pages/doctor-dashboard-page';
import { FamilyProfileDetailPage } from '@/features/family/pages/family-profile-detail-page';
import { FamilyProfileListPage } from '@/features/family/pages/family-profile-list-page';
import { HomePage } from '@/features/home/pages/home-page';
import { PatientProfilePage } from '@/features/patient/pages/patient-profile-page';
import { PharmacyDashboardPage } from '@/features/pharmacy/pages/pharmacy-dashboard-page';
import { ReceptionDashboardPage } from '@/features/reception/pages/reception-dashboard-page';
import { NotFoundPage } from '@/shared/components/layout/not-found-page';

import { GuestOnly, RequireAdmin, RequireAuth } from './guards';

export function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/services" element={<ExaminationServiceCatalogPage />} />
      <Route path="/services/:serviceId" element={<ExaminationServiceCatalogPage />} />
      <Route path="/services/:serviceId/booking" element={<ServiceSlotBookingPage />} />
      <Route path="/appointment/services" element={<ExaminationServiceCatalogPage />} />
      <Route path="/appointment/services/:serviceId" element={<ExaminationServiceCatalogPage />} />
      <Route path="/appointment/services/:serviceId/booking" element={<ServiceSlotBookingPage />} />
      <Route path="/doctors" element={<DoctorSearchPage />} />
      <Route path="/doctors/:doctorId" element={<DoctorSearchPage />} />
      <Route path="/doctors/:doctorId/booking" element={<AppointmentSlotBookingPage />} />
      <Route path="/appointment/doctors" element={<DoctorSearchPage />} />
      <Route path="/appointment/doctors/:doctorId" element={<DoctorSearchPage />} />
      <Route
        path="/appointment/doctors/:doctorId/booking"
        element={<AppointmentSlotBookingPage />}
      />
      <Route
        path="/patient"
        element={
          <RequireAuth>
            <HomePage />
          </RequireAuth>
        }
      />
      <Route
        path="/patient/home"
        element={
          <RequireAuth>
            <HomePage />
          </RequireAuth>
        }
      />
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
        path="/doctor"
        element={
          <RequireAuth>
            <DoctorDashboardPage />
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic"
        element={
          <RequireAuth>
            <DiagnosticDashboardPage />
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy"
        element={
          <RequireAuth>
            <PharmacyDashboardPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/staff"
        element={
          <RequireAuth>
            <RequireAdmin>
              <StaffAccountListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/doctors"
        element={
          <RequireAuth>
            <RequireAdmin>
              <DoctorListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      >
        <Route path=":doctorId/edit" element={<DoctorEditModal />} />
      </Route>
      <Route
        path="/admin/doctors/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <DoctorCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/services"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationServiceListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      >
        <Route path=":serviceId/edit" element={<ServiceEditModal />} />
      </Route>
      <Route
        path="/admin/services/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationServiceCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/examination-services"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationServiceListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      >
        <Route path=":serviceId/edit" element={<ServiceEditModal />} />
      </Route>
      <Route
        path="/admin/examination-services/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationServiceCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/reports"
        element={
          <RequireAuth>
            <RequireAdmin>
              <OperationalReportsPage />
            </RequireAdmin>
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
