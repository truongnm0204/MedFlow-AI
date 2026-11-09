import { Navigate, Route, Routes } from 'react-router-dom';

import { DepartmentCreatePage } from '@/features/admin-departments/pages/department-create-page';
import { DepartmentEditPage } from '@/features/admin-departments/pages/department-edit-page';
import { DepartmentListPage } from '@/features/admin-departments/pages/department-list-page';
import { DoctorCreatePage } from '@/features/admin-doctors/pages/doctor-create-page';
import { DoctorEditModal } from '@/features/admin-doctors/pages/doctor-edit-modal';
import { DoctorListPage } from '@/features/admin-doctors/pages/doctor-list-page';
import { RoomCreatePage } from '@/features/admin-rooms/pages/room-create-page';
import { RoomEditPage } from '@/features/admin-rooms/pages/room-edit-page';
import { StaffAccountListPage } from '@/features/admin-staff/pages/staff-account-list-page';
import { RoleListPage } from '@/features/admin-roles/pages/role-list-page';
import { OperationalReportsPage } from '@/features/admin-reports/pages/operational-reports-page';
import { AppointmentPolicyPage } from '@/features/admin-policies/pages/appointment-policy-page';
import { QueuePolicyPage } from '@/features/admin-policies/pages/queue-policy-page';
import { ServicePricingPage } from '@/features/admin-services/pages/service-pricing-page';
import { RoomListPage } from '@/features/admin-rooms/pages/room-list-page';
import { StaffAccountEditPage } from '@/features/admin-staff/pages/staff-account-edit-page';
import { ExaminationServiceCreatePage } from '@/features/admin-services/pages/examination-service-create-page';
import { ExaminationServiceListPage } from '@/features/admin-services/pages/examination-service-list-page';
import { ServiceEditModal } from '@/features/admin-services/pages/service-edit-modal';
import { StaffAccountCreatePage } from '@/features/admin-staff/pages/staff-account-create-page';
import { ExaminationSlotCreatePage } from '@/features/admin-slots/pages/examination-slot-create-page';
import { ExaminationSlotListPage } from '@/features/admin-slots/pages/examination-slot-list-page';
import { SlotEditModal } from '@/features/admin-slots/pages/slot-edit-modal';
import { AppointmentDetailPage } from '@/features/appointment/pages/appointment-detail-page';
import { AppointmentConfirmationPage } from '@/features/appointment/pages/appointment-confirmation-page';
import { AppointmentListPage } from '@/features/appointment/pages/appointment-list-page';
import { AppointmentPrintPreviewPage } from '@/features/appointment/pages/appointment-print-preview-page';
import { AppointmentQueueTrackingPage } from '@/features/appointment/pages/appointment-queue-tracking-page';
import { AppointmentReviewPage } from '@/features/appointment/pages/appointment-review-page';
import { AppointmentSlotBookingPage } from '@/features/appointment/pages/appointment-slot-booking-page';
import { BookingEntryPage } from '@/features/appointment/pages/booking-entry-page';
import { DoctorSearchPage } from '@/features/appointment/pages/doctor-search-page';
import { ExaminationServiceCatalogPage } from '@/features/appointment/pages/examination-service-catalog-page';
import { ServiceSlotBookingPage } from '@/features/appointment/pages/service-slot-booking-page';
import { LoginPage } from '@/features/auth/pages/login-page';
import { RegisterPage } from '@/features/auth/pages/register-page';
import { VerifyOtpPage } from '@/features/auth/pages/verify-otp-page';
import { DiagnosticDashboardPage } from '@/features/diagnostic/pages/diagnostic-dashboard-page';
import { DoctorDashboardPage } from '@/features/doctor/pages/doctor-dashboard-page';
import { DoctorAssignedQueuePage } from '@/features/doctor/pages/doctor-assigned-queue-page';
import { FamilyProfileDetailPage } from '@/features/family/pages/family-profile-detail-page';
import { FamilyProfileListPage } from '@/features/family/pages/family-profile-list-page';
import { HomePage } from '@/features/home/pages/home-page';
import { HospitalAdminDashboardPage } from '@/features/hospital-admin/pages/hospital-admin-dashboard-page';
import { PatientProfilePage } from '@/features/patient/pages/patient-profile-page';
import { PharmacyDashboardPage } from '@/features/pharmacy/pages/pharmacy-dashboard-page';
import { AssignRoomPage } from '@/features/reception/pages/assign-room-page';
import { PatientSearchPage } from '@/features/reception/pages/patient-search-page';
import { VisitRoutingPage } from '@/features/reception/pages/visit-routing-page';
import { ExaminationQueuePage } from '@/features/reception/pages/examination-queue-page';
import { ReceptionDashboardPage } from '@/features/reception/pages/reception-dashboard-page';
import { SystemAdminDashboardPage } from '@/features/system-admin/pages/system-admin-dashboard-page';
import { NotFoundPage } from '@/shared/components/layout/not-found-page';

import {
  GuestOnly,
  RequireAdmin,
  RequireAuth,
  RequireDiagnostic,
  RequireDoctor,
  RequirePharmacy,
  RequireReception,
  RequireSystemAdmin,
} from './guards';

export function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/booking" element={<BookingEntryPage />} />
      <Route path="/appointment/book" element={<BookingEntryPage />} />
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
      <Route path="/appointment/review" element={<AppointmentReviewPage />} />
      <Route
        path="/appointment/confirmation/:appointmentId"
        element={<AppointmentConfirmationPage />}
      />
      <Route path="/appointments/review" element={<AppointmentReviewPage />} />
      <Route path="/appointments" element={<AppointmentListPage />} />
      <Route path="/appointments/:appointmentId/print" element={<AppointmentPrintPreviewPage />} />
      <Route path="/appointments/:appointmentId/queue" element={<AppointmentQueueTrackingPage />} />
      <Route path="/appointments/:appointmentId" element={<AppointmentDetailPage />} />
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
            <RequireReception>
              <ReceptionDashboardPage />
            </RequireReception>
          </RequireAuth>
        }
      />
      <Route
        path="/reception/routing"
        element={
          <RequireAuth>
            <RequireReception>
              <VisitRoutingPage />
            </RequireReception>
          </RequireAuth>
        }
      />
      <Route
        path="/reception/patients"
        element={
          <RequireAuth>
            <RequireReception>
              <PatientSearchPage />
            </RequireReception>
          </RequireAuth>
        }
      />
      <Route
        path="/reception/queue"
        element={
          <RequireAuth>
            <RequireReception>
              <ExaminationQueuePage />
            </RequireReception>
          </RequireAuth>
        }
      />
      <Route
        path="/reception/assign-room"
        element={
          <RequireAuth>
            <RequireReception>
              <AssignRoomPage />
            </RequireReception>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorDashboardPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/assigned-queue"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorAssignedQueuePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticDashboardPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <PharmacyDashboardPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/admin"
        element={
          <RequireAuth>
            <RequireAdmin>
              <Navigate to="/admin/services" replace />
            </RequireAdmin>
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
        path="/admin/roles"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoleListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/roles/permissions"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoleListPage initialView="matrix" />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/roles/access-check"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoleListPage initialView="access-check" />
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
        path="/admin/staff/new"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <StaffAccountCreatePage />
            </RequireSystemAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/staff/:id/edit"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <StaffAccountEditPage />
            </RequireSystemAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/departments"
        element={
          <RequireAuth>
            <RequireAdmin>
              <DepartmentListPage />
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
        path="/admin/departments/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <DepartmentCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/departments/:id/edit"
        element={
          <RequireAuth>
            <RequireAdmin>
              <DepartmentEditPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
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
        path="/admin/rooms"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoomListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/slots"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationSlotListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      >
        <Route path=":slotId/edit" element={<SlotEditModal />} />
      </Route>
      <Route
        path="/admin/slots/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationSlotCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/examination-slots"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationSlotListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      >
        <Route path=":slotId/edit" element={<SlotEditModal />} />
      </Route>
      <Route
        path="/admin/examination-slots/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ExaminationSlotCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/rooms/new"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoomCreatePage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/rooms/:id/edit"
        element={
          <RequireAuth>
            <RequireAdmin>
              <RoomEditPage />
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
        path="/admin/pricing"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ServicePricingPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/service-pricing"
        element={
          <RequireAuth>
            <RequireAdmin>
              <ServicePricingPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/policies"
        element={
          <RequireAuth>
            <RequireAdmin>
              <AppointmentPolicyPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/appointment-policy"
        element={
          <RequireAuth>
            <RequireAdmin>
              <AppointmentPolicyPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/queue-policy"
        element={
          <RequireAuth>
            <RequireAdmin>
              <QueuePolicyPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/hospital-admin"
        element={
          <RequireAuth>
            <RequireAdmin>
              <HospitalAdminDashboardPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/system-admin"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <SystemAdminDashboardPage />
            </RequireSystemAdmin>
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
