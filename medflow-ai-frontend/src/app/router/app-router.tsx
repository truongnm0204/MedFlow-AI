import { Suspense, createElement, lazy, type ComponentProps, type ExoticComponent } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';

import { LoginPage } from '@/features/auth/pages/login-page';
import { RegisterPage } from '@/features/auth/pages/register-page';
import { VerifyOtpPage } from '@/features/auth/pages/verify-otp-page';
import { HomePage } from '@/features/home/pages/home-page';
import { NotFoundPage } from '@/shared/components/layout/not-found-page';
import { RouteLoadingFallback } from '@/shared/components/layout/route-loading-fallback';

import {
  GuestOnly,
  RequireAdmin,
  RequireAuth,
  RequireDiagnostic,
  RequireDoctor,
  RequireHospitalAdmin,
  RequireIdentityClaimVerify,
  RequirePharmacy,
  RequireReception,
  RequireRole,
  RequireSystemAdmin,
} from './guards';

function withRouteSuspense<P extends object>(LazyComponent: ExoticComponent<P>) {
  function LazyRouteWrapper(props: P) {
    return (
      <Suspense fallback={<RouteLoadingFallback />}>{createElement(LazyComponent, props)}</Suspense>
    );
  }
  return LazyRouteWrapper;
}

const DepartmentCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-departments/pages/department-create-page').then((m) => ({
      default: m.DepartmentCreatePage,
    })),
  ),
);
const DepartmentEditPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-departments/pages/department-edit-page').then((m) => ({
      default: m.DepartmentEditPage,
    })),
  ),
);
const DepartmentListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-departments/pages/department-list-page').then((m) => ({
      default: m.DepartmentListPage,
    })),
  ),
);
const DoctorCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-doctors/pages/doctor-create-page').then((m) => ({
      default: m.DoctorCreatePage,
    })),
  ),
);
const DoctorEditModal = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-doctors/pages/doctor-edit-modal').then((m) => ({
      default: m.DoctorEditModal,
    })),
  ),
);
const DoctorListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-doctors/pages/doctor-list-page').then((m) => ({
      default: m.DoctorListPage,
    })),
  ),
);
const RoomCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-rooms/pages/room-create-page').then((m) => ({
      default: m.RoomCreatePage,
    })),
  ),
);
const RoomEditPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-rooms/pages/room-edit-page').then((m) => ({
      default: m.RoomEditPage,
    })),
  ),
);
const StaffAccountListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-staff/pages/staff-account-list-page').then((m) => ({
      default: m.StaffAccountListPage,
    })),
  ),
);
const LazyRoleListPage = lazy(() =>
  import('@/features/admin-roles/pages/role-list-page').then((m) => ({
    default: m.RoleListPage,
  })),
);
function RoleListPage(props: ComponentProps<typeof LazyRoleListPage>) {
  return (
    <Suspense fallback={<RouteLoadingFallback />}>
      <LazyRoleListPage {...props} />
    </Suspense>
  );
}
const OperationalReportsPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-reports/pages/operational-reports-page').then((m) => ({
      default: m.OperationalReportsPage,
    })),
  ),
);
const AppointmentPolicyPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-policies/pages/appointment-policy-page').then((m) => ({
      default: m.AppointmentPolicyPage,
    })),
  ),
);
const QueuePolicyPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-policies/pages/queue-policy-page').then((m) => ({
      default: m.QueuePolicyPage,
    })),
  ),
);
const ServicePricingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-services/pages/service-pricing-page').then((m) => ({
      default: m.ServicePricingPage,
    })),
  ),
);
const RoomListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-rooms/pages/room-list-page').then((m) => ({
      default: m.RoomListPage,
    })),
  ),
);
const StaffAccountEditPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-staff/pages/staff-account-edit-page').then((m) => ({
      default: m.StaffAccountEditPage,
    })),
  ),
);
const ExaminationServiceCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-services/pages/examination-service-create-page').then((m) => ({
      default: m.ExaminationServiceCreatePage,
    })),
  ),
);
const ExaminationServiceListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-services/pages/examination-service-list-page').then((m) => ({
      default: m.ExaminationServiceListPage,
    })),
  ),
);
const DiagnosticServiceListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-services/pages/diagnostic-service-list-page').then((m) => ({
      default: m.DiagnosticServiceListPage,
    })),
  ),
);
const ServiceEditModal = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-services/pages/service-edit-modal').then((m) => ({
      default: m.ServiceEditModal,
    })),
  ),
);
const StaffAccountCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-staff/pages/staff-account-create-page').then((m) => ({
      default: m.StaffAccountCreatePage,
    })),
  ),
);
const ExaminationSlotCreatePage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-slots/pages/examination-slot-create-page').then((m) => ({
      default: m.ExaminationSlotCreatePage,
    })),
  ),
);
const ExaminationSlotListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-slots/pages/examination-slot-list-page').then((m) => ({
      default: m.ExaminationSlotListPage,
    })),
  ),
);
const SlotEditModal = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-slots/pages/slot-edit-modal').then((m) => ({
      default: m.SlotEditModal,
    })),
  ),
);
const AppointmentDetailPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-detail-page').then((m) => ({
      default: m.AppointmentDetailPage,
    })),
  ),
);
const AppointmentConfirmationPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-confirmation-page').then((m) => ({
      default: m.AppointmentConfirmationPage,
    })),
  ),
);
const AppointmentListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-list-page').then((m) => ({
      default: m.AppointmentListPage,
    })),
  ),
);
const AppointmentPrintPreviewPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-print-preview-page').then((m) => ({
      default: m.AppointmentPrintPreviewPage,
    })),
  ),
);
const AppointmentQueueTrackingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-queue-tracking-page').then((m) => ({
      default: m.AppointmentQueueTrackingPage,
    })),
  ),
);
const AppointmentReviewPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-review-page').then((m) => ({
      default: m.AppointmentReviewPage,
    })),
  ),
);
const AppointmentSlotBookingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/appointment-slot-booking-page').then((m) => ({
      default: m.AppointmentSlotBookingPage,
    })),
  ),
);
const BookingEntryPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/booking-entry-page').then((m) => ({
      default: m.BookingEntryPage,
    })),
  ),
);
const DoctorSearchPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/doctor-search-page').then((m) => ({
      default: m.DoctorSearchPage,
    })),
  ),
);
const ExaminationServiceCatalogPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/examination-service-catalog-page').then((m) => ({
      default: m.ExaminationServiceCatalogPage,
    })),
  ),
);
const ServiceSlotBookingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/appointment/pages/service-slot-booking-page').then((m) => ({
      default: m.ServiceSlotBookingPage,
    })),
  ),
);
const DiagnosticDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-dashboard-page').then((m) => ({
      default: m.DiagnosticDashboardPage,
    })),
  ),
);
const DiagnosticOrderReviewPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-order-review-page').then((m) => ({
      default: m.DiagnosticOrderReviewPage,
    })),
  ),
);
const DiagnosticExecutionPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-execution-page').then((m) => ({
      default: m.DiagnosticExecutionPage,
    })),
  ),
);
const DiagnosticPatientReceivePage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-patient-receive-page').then((m) => ({
      default: m.DiagnosticPatientReceivePage,
    })),
  ),
);
const DiagnosticQueueManagePage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-queue-manage-page').then((m) => ({
      default: m.DiagnosticQueueManagePage,
    })),
  ),
);
const DiagnosticResultEntryPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-result-entry-page').then((m) => ({
      default: m.DiagnosticResultEntryPage,
    })),
  ),
);
const DiagnosticResultViewPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-result-view-page').then((m) => ({
      default: m.DiagnosticResultViewPage,
    })),
  ),
);
const DiagnosticWorklistPage = withRouteSuspense(
  lazy(() =>
    import('@/features/diagnostic/pages/diagnostic-worklist-page').then((m) => ({
      default: m.DiagnosticWorklistPage,
    })),
  ),
);
const DoctorDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-dashboard-page').then((m) => ({
      default: m.DoctorDashboardPage,
    })),
  ),
);
const DoctorAssignedQueuePage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-assigned-queue-page').then((m) => ({
      default: m.DoctorAssignedQueuePage,
    })),
  ),
);
const DoctorConsultationWorkspacePage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-consultation-workspace-page').then((m) => ({
      default: m.DoctorConsultationWorkspacePage,
    })),
  ),
);
const DoctorMedicalHistoryPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-medical-history-page').then((m) => ({
      default: m.DoctorMedicalHistoryPage,
    })),
  ),
);
const DoctorDiagnosisPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-diagnosis-page').then((m) => ({
      default: m.DoctorDiagnosisPage,
    })),
  ),
);
const FamilyProfileDetailPage = withRouteSuspense(
  lazy(() =>
    import('@/features/family/pages/family-profile-detail-page').then((m) => ({
      default: m.FamilyProfileDetailPage,
    })),
  ),
);
const FamilyProfileListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/family/pages/family-profile-list-page').then((m) => ({
      default: m.FamilyProfileListPage,
    })),
  ),
);
const HospitalAdminDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/hospital-admin/pages/hospital-admin-dashboard-page').then((m) => ({
      default: m.HospitalAdminDashboardPage,
    })),
  ),
);
const PatientDiagnosticResultPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient/pages/patient-diagnostic-result-page').then((m) => ({
      default: m.PatientDiagnosticResultPage,
    })),
  ),
);
const PatientProfilePage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient/pages/patient-profile-page').then((m) => ({
      default: m.PatientProfilePage,
    })),
  ),
);
const AccountSettingsPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient-records/pages/account-settings-page').then((m) => ({
      default: m.AccountSettingsPage,
    })),
  ),
);
const ForgotPasswordPage = withRouteSuspense(
  lazy(() =>
    import('@/features/auth/pages/forgot-password-page').then((m) => ({
      default: m.ForgotPasswordPage,
    })),
  ),
);
const MedicalRecordDetailPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient-records/pages/medical-record-detail-page').then((m) => ({
      default: m.MedicalRecordDetailPage,
    })),
  ),
);
const MedicalRecordListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient-records/pages/medical-record-list-page').then((m) => ({
      default: m.MedicalRecordListPage,
    })),
  ),
);
const PatientPrescriptionDetailPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient-records/pages/patient-prescription-detail-page').then((m) => ({
      default: m.PatientPrescriptionDetailPage,
    })),
  ),
);
const PatientPrescriptionListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/patient-records/pages/patient-prescription-list-page').then((m) => ({
      default: m.PatientPrescriptionListPage,
    })),
  ),
);
const VisitPaymentPage = withRouteSuspense(
  lazy(() =>
    import('@/features/payment/pages/visit-payment-page').then((m) => ({
      default: m.VisitPaymentPage,
    })),
  ),
);
const MyVisitsPage = withRouteSuspense(
  lazy(() =>
    import('@/features/payment/pages/my-visits-page').then((m) => ({
      default: m.MyVisitsPage,
    })),
  ),
);
const PatientPaymentDetailPage = withRouteSuspense(
  lazy(() =>
    import('@/features/payment/pages/patient-payment-detail-page').then((m) => ({
      default: m.PatientPaymentDetailPage,
    })),
  ),
);
const PatientPaymentListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/payment/pages/patient-payment-list-page').then((m) => ({
      default: m.PatientPaymentListPage,
    })),
  ),
);
const PharmacyDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/pharmacy-dashboard-page').then((m) => ({
      default: m.PharmacyDashboardPage,
    })),
  ),
);
const PrescriptionSearchPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/prescription-search-page').then((m) => ({
      default: m.PrescriptionSearchPage,
    })),
  ),
);
const AssignRoomPage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/assign-room-page').then((m) => ({
      default: m.AssignRoomPage,
    })),
  ),
);
const VisitRoutingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/visit-routing-page').then((m) => ({
      default: m.VisitRoutingPage,
    })),
  ),
);
const ExaminationQueuePage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/examination-queue-page').then((m) => ({
      default: m.ExaminationQueuePage,
    })),
  ),
);
const PatientSearchPage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/patient-search-page').then((m) => ({
      default: m.PatientSearchPage,
    })),
  ),
);
const ReceptionDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/reception-dashboard-page').then((m) => ({
      default: m.ReceptionDashboardPage,
    })),
  ),
);
const PatientIdentityClaimPage = withRouteSuspense(
  lazy(() =>
    import('@/features/identity-claim/pages/patient-identity-claim-page').then((m) => ({
      default: m.PatientIdentityClaimPage,
    })),
  ),
);
const ReceptionIdentityClaimPage = withRouteSuspense(
  lazy(() =>
    import('@/features/identity-claim/pages/reception-identity-claim-page').then((m) => ({
      default: m.ReceptionIdentityClaimPage,
    })),
  ),
);
const WalkInVisitPage = withRouteSuspense(
  lazy(() =>
    import('@/features/reception/pages/walk-in-visit-page').then((m) => ({
      default: m.WalkInVisitPage,
    })),
  ),
);
const AuditLogListPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-audit/pages/audit-log-list-page').then((m) => ({
      default: m.AuditLogListPage,
    })),
  ),
);
const IntegrationConfigPage = withRouteSuspense(
  lazy(() =>
    import('@/features/admin-integrations/pages/integration-config-page').then((m) => ({
      default: m.IntegrationConfigPage,
    })),
  ),
);
const SystemAdminDashboardPage = withRouteSuspense(
  lazy(() =>
    import('@/features/system-admin/pages/system-admin-dashboard-page').then((m) => ({
      default: m.SystemAdminDashboardPage,
    })),
  ),
);
const DoctorReviewDiagnosticsPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-review-diagnostics-page').then((m) => ({
      default: m.DoctorReviewDiagnosticsPage,
    })),
  ),
);
const DoctorDiagnosticResultsTrackingPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-diagnostic-results-tracking-page').then((m) => ({
      default: m.DoctorDiagnosticResultsTrackingPage,
    })),
  ),
);
const PrepareMedicinesPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/prepare-medicines-page').then((m) => ({
      default: m.PrepareMedicinesPage,
    })),
  ),
);
const FefoBatchSelectionPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/fefo-batch-selection-page').then((m) => ({
      default: m.FefoBatchSelectionPage,
    })),
  ),
);
const DispenseConfirmPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/dispense-confirm-page').then((m) => ({
      default: m.DispenseConfirmPage,
    })),
  ),
);
const DispensingResultPage = withRouteSuspense(
  lazy(() =>
    import('@/features/pharmacy/pages/dispensing-result-page').then((m) => ({
      default: m.DispensingResultPage,
    })),
  ),
);
const DoctorCreatePrescriptionPage = withRouteSuspense(
  lazy(() =>
    import('@/features/doctor/pages/doctor-create-prescription-page').then((m) => ({
      default: m.DoctorCreatePrescriptionPage,
    })),
  ),
);

export function AppRouter() {
  return <AppRoutes />;
}

function AppRoutes() {
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
        path="/patient/visits"
        element={
          <RequireAuth>
            <MyVisitsPage />
          </RequireAuth>
        }
      />
      <Route
        path="/payments"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPaymentListPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/payments/visits/:visitIdOrCode"
        element={
          <RequireAuth>
            <VisitPaymentPage />
          </RequireAuth>
        }
      />
      <Route
        path="/patient/payments/visits/:visitIdOrCode"
        element={
          <RequireAuth>
            <VisitPaymentPage />
          </RequireAuth>
        }
      />
      <Route
        path="/payments/:visitCode"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPaymentDetailPage />
            </RequireRole>
          </RequireAuth>
        }
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
        path="/forgot-password"
        element={
          <GuestOnly>
            <ForgotPasswordPage />
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
        path="/reception/walk-in"
        element={
          <RequireAuth>
            <RequireReception>
              <WalkInVisitPage />
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
        path="/reception/identity-claims"
        element={
          <RequireAuth>
            <RequireIdentityClaimVerify>
              <ReceptionIdentityClaimPage />
            </RequireIdentityClaimVerify>
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
        path="/doctor/consultation"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/clinical-examination"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/diagnosis"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorDiagnosisPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/diagnosis"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorDiagnosisPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/clinical-examination"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/diagnostic-orders"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/diagnostic-orders"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/additional-diagnostic-orders"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/additional-diagnostic-order"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/additional-diagnostic-orders"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/additional-diagnostic-order"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/complete"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/complete-visit"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/complete"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/complete-visit"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorConsultationWorkspacePage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/medical-history"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorMedicalHistoryPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/patients/:patientIdOrCode/medical-history"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorMedicalHistoryPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/visits/:visitIdOrCode/medical-history"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorMedicalHistoryPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/medical-history"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorMedicalHistoryPage />
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
        path="/diagnostic/worklist"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticWorklistPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/worklist/:orderIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticWorklistPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/worklist"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticWorklistPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/execution"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticExecutionPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/execution/:orderIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticExecutionPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/execution"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticExecutionPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode/execution"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticExecutionPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/execution"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticExecutionPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/review"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode/review"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/orders"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/orders/:orderIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticOrderReviewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/queue"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/queue/:entryIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/queues"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/queue"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/queue/:entryIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/queue-entries"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/queue-entries/:entryIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticQueueManagePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/receive-patient"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode/receive-patient"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/receive-patient"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/patients/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/patients/:orderIdOrCode/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/workbench/orders/:orderIdOrCode/receive"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticPatientReceivePage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/results"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticResultEntryPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/results/view"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticResultViewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/results/:orderIdOrCode/view"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticResultViewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/orders/:orderIdOrCode/result/view"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticResultViewPage />
            </RequireDiagnostic>
          </RequireAuth>
        }
      />
      <Route
        path="/diagnostic/results/:orderIdOrCode"
        element={
          <RequireAuth>
            <RequireDiagnostic>
              <DiagnosticResultEntryPage />
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
        path="/pharmacy/prescriptions"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <PrescriptionSearchPage />
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
        path="/admin/identity-claims"
        element={
          <RequireAuth>
            <RequireIdentityClaimVerify adminOnly>
              <ReceptionIdentityClaimPage />
            </RequireIdentityClaimVerify>
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
        path="/admin/roles/new"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <RoleListPage initialCreateOpen />
            </RequireSystemAdmin>
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
        path="/admin/diagnostic-services"
        element={
          <RequireAuth>
            <RequireHospitalAdmin>
              <DiagnosticServiceListPage />
            </RequireHospitalAdmin>
          </RequireAuth>
        }
      />
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
        path="/admin/audit-logs"
        element={
          <RequireAuth>
            <RequireAdmin>
              <AuditLogListPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/system-admin/audit-logs"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <AuditLogListPage />
            </RequireSystemAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/integrations"
        element={
          <RequireAuth>
            <RequireAdmin>
              <IntegrationConfigPage />
            </RequireAdmin>
          </RequireAuth>
        }
      />
      <Route
        path="/system-admin/integrations"
        element={
          <RequireAuth>
            <RequireSystemAdmin>
              <IntegrationConfigPage />
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
      <Route
        path="/patient/identity-claims"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientIdentityClaimPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/diagnostic-results"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientDiagnosticResultPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/diagnostic-results/:orderCode"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientDiagnosticResultPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/emr/diagnostic-result"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientDiagnosticResultPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/emr/diagnostic-result/:orderCode"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientDiagnosticResultPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/emr"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <MedicalRecordListPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/emr"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <MedicalRecordListPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/prescriptions"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPrescriptionListPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/emr/prescriptions"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPrescriptionListPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/emr/prescriptions/:prescriptionCode"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPrescriptionDetailPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/prescriptions/:prescriptionCode"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <PatientPrescriptionDetailPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/emr/:recordId"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <MedicalRecordDetailPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/patient/emr/:recordId"
        element={
          <RequireAuth>
            <RequireRole allowedRoles={['PATIENT']}>
              <MedicalRecordDetailPage />
            </RequireRole>
          </RequireAuth>
        }
      />
      <Route
        path="/settings"
        element={
          <RequireAuth>
            <AccountSettingsPage />
          </RequireAuth>
        }
      />
      <Route
        path="/patient/settings"
        element={
          <RequireAuth>
            <AccountSettingsPage />
          </RequireAuth>
        }
      />
      <Route path="/family" element={<FamilyProfileListPage />} />
      <Route path="/patient/family" element={<FamilyProfileListPage />} />
      <Route path="/family/:memberId" element={<FamilyProfileDetailPage />} />
      <Route path="/patient/family/:memberId" element={<FamilyProfileDetailPage />} />
      <Route
        path="/doctor/diagnostic-results/tracking"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorDiagnosticResultsTrackingPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/diagnostic-results"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorReviewDiagnosticsPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/diagnostic-results"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorReviewDiagnosticsPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/prepare"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <PrepareMedicinesPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/prescriptions/:prescriptionCode/prepare"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <PrepareMedicinesPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/fefo-batches"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <FefoBatchSelectionPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/prescriptions/:prescriptionCode/fefo-batches"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <FefoBatchSelectionPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/dispense"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <DispenseConfirmPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/prescriptions/:prescriptionCode/dispense"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <DispenseConfirmPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/dispensing-result"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <DispensingResultPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/pharmacy/dispensing/:dispenseCode"
        element={
          <RequireAuth>
            <RequirePharmacy>
              <DispensingResultPage />
            </RequirePharmacy>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/prescriptions/new"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorCreatePrescriptionPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route
        path="/doctor/consultation/:visitIdOrCode/prescription"
        element={
          <RequireAuth>
            <RequireDoctor>
              <DoctorCreatePrescriptionPage />
            </RequireDoctor>
          </RequireAuth>
        }
      />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
