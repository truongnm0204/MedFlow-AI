import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  AlertCircle,
  ArrowLeft,
  CheckCircle2,
  ChevronRight,
  Clock,
  Edit3,
  FileText,
  Home,
  Info,
  Printer,
  QrCode,
  RotateCcw,
  Stethoscope,
  UserRound,
  X,
  XCircle,
} from 'lucide-react';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { AppointmentCancelModal } from '../components/appointment-cancel-modal';
import { AppointmentQuickViewModal } from '../components/appointment-quick-view-modal';
import { AppointmentRescheduleModal } from '../components/appointment-reschedule-modal';
import { CANCELLED_STATUSES, COMPLETED_STATUSES } from '../data/appointment-list-mock';
import {
  useAppointmentDetailQuery,
  useCancelAppointmentMutation,
  useRescheduleAppointmentMutation,
  useUpdateAppointmentReasonMutation,
} from '../hooks/use-appointment-detail';
import { updateReasonNoteSchema } from '../schemas/appointment-detail.schema';
import { useBookingStore } from '../store/booking.store';

export function AppointmentDetailPage() {
  const { appointmentId = '' } = useParams<{ appointmentId: string }>();
  const navigate = useNavigate();
  const selectService = useBookingStore((s) => s.selectService);
  const selectDoctor = useBookingStore((s) => s.selectDoctor);

  const {
    data: appointment,
    isLoading,
    isError,
    error,
    refetch,
  } = useAppointmentDetailQuery(appointmentId);

  const rescheduleMutation = useRescheduleAppointmentMutation();
  const cancelMutation = useCancelAppointmentMutation();
  const updateReasonMutation = useUpdateAppointmentReasonMutation();

  const [isPrintModalOpen, setIsPrintModalOpen] = useState(false);
  const [isRescheduleModalOpen, setIsRescheduleModalOpen] = useState(false);
  const [isCancelModalOpen, setIsCancelModalOpen] = useState(false);
  const [isEditingReason, setIsEditingReason] = useState(false);
  const [reasonDraft, setReasonDraft] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [feedbackBanner, setFeedbackBanner] = useState<string | null>(null);

  const isCompleted = appointment ? COMPLETED_STATUSES.includes(appointment.status) : false;
  const isCancelled = appointment ? CANCELLED_STATUSES.includes(appointment.status) : false;
  const canModify = appointment
    ? ['CONFIRMED', 'BOOKED', 'PENDING'].includes(appointment.status)
    : false;
  const canPrintSlip = appointment ? !isCancelled : false;

  const handleOpenEditReason = () => {
    if (!appointment) return;
    setReasonDraft(
      appointment.reasonForVisit ??
        'Đau tức vùng thượng vị từng cơn sau ăn, ợ chua liên tục 5 ngày gần đây.',
    );
    setReasonError(null);
    setIsEditingReason(true);
  };

  const handleSaveReason = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!appointment) return;

    const parsed = updateReasonNoteSchema.safeParse({
      reasonForVisit: reasonDraft,
    });

    if (!parsed.success) {
      setReasonError(parsed.error.issues[0]?.message ?? 'Lý do khám không hợp lệ.');
      return;
    }

    setReasonError(null);
    updateReasonMutation.mutate(
      {
        appointmentId: appointment.id,
        reasonForVisit: parsed.data.reasonForVisit,
      },
      {
        onSuccess: () => {
          setIsEditingReason(false);
          setFeedbackBanner('Đã cập nhật lý do khám / triệu chứng ban đầu vào hồ sơ tiếp nhận.');
        },
      },
    );
  };

  const handleConfirmReschedule = (payload: { targetSlotId: string; rescheduleNote?: string }) => {
    if (!appointment) return;

    rescheduleMutation.mutate(
      {
        appointmentId: appointment.id,
        targetSlotId: payload.targetSlotId,
        rescheduleNote: payload.rescheduleNote,
      },
      {
        onSuccess: (updated) => {
          setIsRescheduleModalOpen(false);
          setFeedbackBanner(
            `Đổi lịch khám thành công sang khung giờ ${updated.scheduledTimeRange} (${updated.scheduledDateFullLabel}) • STT giữ trước mới: ${updated.queueNumber ?? '005'}.`,
          );
        },
      },
    );
  };

  const handleConfirmCancel = (cancellationReason: string) => {
    if (!appointment) return;

    cancelMutation.mutate(
      {
        appointmentId: appointment.id,
        cancellationReason,
      },
      {
        onSuccess: () => {
          setIsCancelModalOpen(false);
          setFeedbackBanner(
            `Lịch hẹn #${appointment.appointmentCode} đã được hủy và giải phóng slot thành công.`,
          );
        },
      },
    );
  };

  const handleRebookSpecialty = () => {
    if (!appointment) return;
    selectDoctor(appointment.doctorId);
    selectService(appointment.serviceId, appointment.serviceName);
    navigate(`/appointment/doctors?specialty=${encodeURIComponent(appointment.specialtyId)}`);
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          {/* Breadcrumb & FHIR Tag Row (Figma 17:1340) */}
          <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
            <nav
              aria-label="Breadcrumb"
              className="flex flex-wrap items-center gap-2 text-xs text-sub"
            >
              <Link
                to="/"
                className="flex items-center gap-1.5 transition-colors hover:text-primary"
              >
                <Home className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Trang chủ</span>
              </Link>
              <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <Link to="/appointments" className="transition-colors hover:text-primary">
                Lịch hẹn của tôi
              </Link>
              <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <span className="font-bold text-ink">
                Chi tiết Lịch hẹn {appointment ? `#${appointment.appointmentCode}` : ''}
              </span>
            </nav>

            <div className="flex items-center gap-2">
              <span className="font-mono text-[11px] text-sub">HL7/FHIR: Appointment.detail</span>
              <span className="rounded-md border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-[11px] font-bold text-primary">
                SCR-APT-09
              </span>
            </div>
          </div>

          {/* Feedback Banner */}
          {feedbackBanner && (
            <div
              role="status"
              className="mb-5 flex items-center justify-between gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-semibold text-emerald-900"
            >
              <div className="flex items-center gap-2">
                <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                <span>{feedbackBanner}</span>
              </div>
              <button
                type="button"
                onClick={() => setFeedbackBanner(null)}
                className="font-bold text-emerald-800 hover:underline"
              >
                Đóng
              </button>
            </div>
          )}

          {/* Loading State */}
          {isLoading && (
            <div role="status" aria-label="Đang tải chi tiết phiếu hẹn" className="space-y-5">
              <div className="h-28 animate-pulse rounded-2xl border border-border bg-white p-6" />
              <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
                <div className="space-y-6 lg:col-span-8">
                  <div className="h-64 animate-pulse rounded-2xl border border-border bg-white p-6" />
                  <div className="h-56 animate-pulse rounded-2xl border border-border bg-white p-6" />
                </div>
                <div className="space-y-6 lg:col-span-4">
                  <div className="h-60 animate-pulse rounded-2xl border border-border bg-white p-6" />
                  <div className="h-52 animate-pulse rounded-2xl border border-border bg-white p-6" />
                </div>
              </div>
            </div>
          )}

          {/* Error / Not Found / Forbidden State */}
          {!isLoading && (isError || !appointment) && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-rose-50 text-danger">
                <AlertCircle className="h-6 w-6" aria-hidden="true" />
              </div>
              <h1 className="mt-4 text-lg font-extrabold text-ink">
                Không thể hiển thị chi tiết phiếu hẹn khám
              </h1>
              <p className="mx-auto mt-1.5 max-w-lg text-xs leading-relaxed text-sub">
                {getErrorMessage(
                  error,
                  'Phiếu hẹn không tồn tại hoặc bạn không có quyền truy cập hồ sơ này.',
                )}
              </p>
              <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
                <Link
                  to="/appointments"
                  className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
                >
                  <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Về danh sách Lịch hẹn của tôi</span>
                </Link>
                <button
                  type="button"
                  onClick={() => refetch()}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
                >
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Thử tải lại</span>
                </button>
              </div>
            </div>
          )}

          {/* Main SCR-APT-09 Content (Figma 17:1340) */}
          {!isLoading && !isError && appointment && (
            <div className="space-y-6">
              {/* Top Header Summary & Valid Actions Card */}
              <section
                aria-label="Thông tin tổng quan phiếu hẹn"
                className="flex flex-col justify-between gap-4 rounded-2xl border border-border bg-white p-6 shadow-xs lg:flex-row lg:items-center"
              >
                <div>
                  <div className="flex flex-wrap items-center gap-3">
                    <h1 className="text-xl font-extrabold tracking-tight text-ink sm:text-2xl">
                      Chi Tiết Phiếu Hẹn Khám Ngoại Trú
                    </h1>

                    {isCancelled ? (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-[11px] font-bold text-rose-700">
                        <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>{appointment.statusBannerLabel}</span>
                      </span>
                    ) : isCompleted ? (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[11px] font-bold text-emerald-700">
                        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>{appointment.statusBannerLabel}</span>
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[11px] font-bold text-emerald-700">
                        <span className="h-2 w-2 rounded-full bg-emerald-500" aria-hidden="true" />
                        <span>{appointment.statusBannerLabel}</span>
                      </span>
                    )}
                  </div>

                  <p className="mt-2 text-xs text-sub">
                    Mã hồ sơ tiếp nhận:{' '}
                    <span className="font-mono font-bold text-ink">
                      {appointment.appointmentCode}
                    </span>{' '}
                    • Hình thức:{' '}
                    <span className="font-medium text-ink">
                      {appointment.bookingTypeLabel ?? 'Khám chuyên khoa hẹn giờ tự nguyện'}
                    </span>
                  </p>
                </div>

                {/* Status-aware Valid Actions (Unauthorized/invalid actions hidden per DoD) */}
                <div className="flex flex-wrap items-center gap-2.5">
                  {canModify && (
                    <button
                      type="button"
                      onClick={() => setIsCancelModalOpen(true)}
                      className="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-white px-3.5 py-2.5 text-xs font-bold text-danger transition-colors hover:bg-rose-50"
                    >
                      <X className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Hủy lịch hẹn</span>
                    </button>
                  )}

                  {canPrintSlip && (
                    <button
                      type="button"
                      onClick={() => setIsPrintModalOpen(true)}
                      className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2.5 text-xs font-bold text-ink transition-colors hover:bg-canvas"
                    >
                      <Printer className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
                      <span>In Phiếu Khám</span>
                    </button>
                  )}

                  {canModify && (
                    <button
                      type="button"
                      onClick={() => setIsRescheduleModalOpen(true)}
                      className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
                    >
                      <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Đổi Lịch Khám (SCR-APT-10)</span>
                    </button>
                  )}

                  {(isCompleted || isCancelled) && (
                    <button
                      type="button"
                      onClick={handleRebookSpecialty}
                      className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2.5 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
                    >
                      <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>
                        {appointment.rebookSpecialtyLabel ?? `Đặt lại ${appointment.specialtyName}`}
                      </span>
                    </button>
                  )}
                </div>
              </section>

              {/* 2-Column Main Detail Grid (8 Cols Left + 4 Cols Right) */}
              <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
                {/* LEFT COLUMN (lg:col-span-8) */}
                <div className="space-y-6 lg:col-span-8">
                  {/* Card 1: Thông Tin Buồng Khám & Bác Sĩ Chuyên Khoa */}
                  <section
                    aria-label="Thông Tin Buồng Khám và Bác Sĩ Chuyên Khoa"
                    className="rounded-2xl border border-border bg-white p-6 shadow-xs"
                  >
                    <div className="flex items-center gap-2.5">
                      <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-50 text-primary">
                        <Stethoscope className="h-4 w-4" aria-hidden="true" />
                      </div>
                      <h2 className="text-base font-extrabold text-ink">
                        Thông Tin Buồng Khám &amp; Bác Sĩ Chuyên Khoa
                      </h2>
                    </div>

                    <div className="mt-5 grid grid-cols-1 gap-4 sm:grid-cols-2">
                      {/* Box 1: Bác sĩ phụ trách */}
                      <div className="rounded-xl border border-slate-100 bg-canvas p-4">
                        <p className="text-xs font-medium text-sub">Bác sĩ phụ trách</p>
                        <p className="mt-1 text-sm font-extrabold text-ink">
                          {appointment.doctorName}
                        </p>
                        <p className="mt-0.5 text-xs text-sub">{appointment.departmentName}</p>
                      </div>

                      {/* Box 2: Thời gian tiếp nhận */}
                      <div className="rounded-xl border border-slate-100 bg-canvas p-4">
                        <p className="text-xs font-medium text-sub">Thời gian tiếp nhận</p>
                        <p className="mt-1 text-sm font-extrabold text-primary">
                          {appointment.scheduledTimeRange}
                        </p>
                        <p className="mt-0.5 text-xs text-sub">
                          {appointment.scheduledDateFullLabel}
                        </p>
                      </div>

                      {/* Box 3: Địa điểm khám */}
                      <div className="rounded-xl border border-slate-100 bg-canvas p-4">
                        <p className="text-xs font-medium text-sub">Địa điểm khám</p>
                        <p className="mt-1 text-sm font-extrabold text-ink">
                          {appointment.roomName.replace('Phòng ', 'Phòng khám ')}
                        </p>
                        <p className="mt-0.5 text-xs text-sub">
                          {appointment.roomZoneLabel || 'Khu Điều trị Ngoại trú Ban ngày'}
                        </p>
                      </div>

                      {/* Box 4: Gói dịch vụ */}
                      <div className="rounded-xl border border-slate-100 bg-canvas p-4">
                        <p className="text-xs font-medium text-sub">Gói dịch vụ</p>
                        <p className="mt-1 text-sm font-extrabold text-ink">
                          {appointment.serviceFullLabel ??
                            `${appointment.serviceCode}: ${appointment.serviceName}`}
                        </p>
                        <p className="mt-0.5 text-xs text-sub">
                          {appointment.serviceSubLabel ?? 'Khám theo giờ yêu cầu tự nguyện'}
                        </p>
                      </div>
                    </div>
                  </section>

                  {/* Card 2: Thông Tin Bệnh Nhân Đăng Ký */}
                  <section
                    aria-label="Thông Tin Bệnh Nhân Đăng Ký"
                    className="rounded-2xl border border-border bg-white p-6 shadow-xs"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex items-center gap-2.5">
                        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-50 text-primary">
                          <UserRound className="h-4 w-4" aria-hidden="true" />
                        </div>
                        <h2 className="text-base font-extrabold text-ink">
                          Thông Tin Bệnh Nhân Đăng Ký
                        </h2>
                      </div>

                      {canModify && !isEditingReason && (
                        <button
                          type="button"
                          onClick={handleOpenEditReason}
                          className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-bold text-primary transition-colors hover:bg-sky-50"
                        >
                          <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Cập nhật triệu chứng</span>
                        </button>
                      )}
                    </div>

                    {/* 4-Column Administrative Metadata */}
                    <div className="mt-5 grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-4">
                      <div>
                        <p className="text-xs font-medium text-sub">Họ và tên</p>
                        <p className="mt-1 text-sm font-extrabold text-ink">
                          {appointment.patientName}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs font-medium text-sub">Năm sinh / Tuổi</p>
                        <p className="mt-1 text-sm font-extrabold text-ink">
                          {appointment.patientBirthYearLabel ??
                            `1986 (${appointment.patientAgeLabel})`}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs font-medium text-sub">Số điện thoại</p>
                        <p className="mt-1 font-mono text-sm font-extrabold text-ink">
                          {appointment.patientPhone ?? '0912 345 678'}
                        </p>
                      </div>

                      <div>
                        <p className="text-xs font-medium text-sub">CCCD / Định danh</p>
                        <p className="mt-1 font-mono text-sm font-extrabold text-ink">
                          {appointment.patientNationalId ?? '001086001234'}
                        </p>
                      </div>
                    </div>

                    {/* Reason for Visit / Initial Symptoms */}
                    <div className="mt-5">
                      <p className="text-xs font-medium text-sub">
                        Lý do khám / Triệu chứng ban đầu:
                      </p>

                      {isEditingReason ? (
                        <form onSubmit={handleSaveReason} noValidate className="mt-2 space-y-2.5">
                          <label htmlFor="reason-for-visit-input" className="sr-only">
                            Lý do khám / Triệu chứng ban đầu
                          </label>
                          <textarea
                            id="reason-for-visit-input"
                            rows={3}
                            value={reasonDraft}
                            onChange={(event) => {
                              setReasonDraft(event.target.value);
                              if (reasonError) setReasonError(null);
                            }}
                            placeholder="Nhập triệu chứng hoặc lý do đăng ký khám (tối thiểu 10 ký tự)..."
                            className="w-full rounded-xl border border-border bg-white p-3 text-xs text-ink focus:border-primary focus:outline-none"
                          />
                          {reasonError && (
                            <p role="alert" className="text-xs font-semibold text-danger">
                              {reasonError}
                            </p>
                          )}
                          <div className="flex items-center justify-end gap-2">
                            <button
                              type="button"
                              onClick={() => {
                                setIsEditingReason(false);
                                setReasonError(null);
                              }}
                              className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-bold text-ink hover:bg-canvas"
                            >
                              Hủy
                            </button>
                            <button
                              type="submit"
                              disabled={updateReasonMutation.isPending}
                              className="rounded-lg bg-primary px-3.5 py-1.5 text-xs font-bold text-white hover:bg-primary-dark disabled:opacity-60"
                            >
                              Lưu triệu chứng
                            </button>
                          </div>
                        </form>
                      ) : (
                        <div className="mt-2 rounded-xl border border-slate-100 bg-canvas p-4 text-xs font-medium leading-relaxed text-ink">
                          {appointment.reasonForVisit ??
                            'Đau tức vùng thượng vị từng cơn sau ăn, ợ chua liên tục 5 ngày gần đây.'}
                        </div>
                      )}
                    </div>

                    {/* Completed Clinical Conclusion */}
                    {isCompleted && appointment.clinicalSummary && (
                      <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50/70 p-4 text-xs text-emerald-950">
                        <p className="flex items-center gap-1.5 font-extrabold text-emerald-900">
                          <FileText
                            className="h-4 w-4 shrink-0 text-emerald-600"
                            aria-hidden="true"
                          />
                          <span>Kết luận lâm sàng tóm tắt:</span>
                        </p>
                        <p className="mt-1 leading-relaxed">{appointment.clinicalSummary}</p>
                      </div>
                    )}

                    {/* Cancelled Reason Box */}
                    {isCancelled && (
                      <div className="mt-4 rounded-xl border border-rose-200 bg-rose-50/60 p-4 text-xs text-rose-950">
                        <p className="flex items-center gap-1.5 font-extrabold text-rose-900">
                          <Info className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
                          <span>Lý do hủy lịch hẹn:</span>
                        </p>
                        <p className="mt-1 leading-relaxed">
                          {appointment.cancellationReason ??
                            'Người bệnh chủ động hủy lịch hẹn trước giờ khám.'}
                        </p>
                      </div>
                    )}
                  </section>
                </div>

                {/* RIGHT COLUMN (lg:col-span-4) */}
                <div className="space-y-6 lg:col-span-4">
                  {/* Card 3: Dark Sky Queue Number & Check-in Card (Figma 17:1340) */}
                  <section
                    aria-label="Số thứ tự giữ trước và Check-in"
                    className="rounded-2xl bg-sky-800 p-6 text-white shadow-sm"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-[11px] font-bold tracking-wider text-sky-100 uppercase">
                        SỐ THỨ TỰ (STT) GIỮ TRƯỚC
                      </span>
                      <span className="rounded-full bg-sky-700/90 px-2.5 py-0.5 text-[11px] font-bold text-sky-100">
                        {appointment.priorityBadgeLabel ?? 'Ưu Tiên Khám'}
                      </span>
                    </div>

                    <div className="mt-3 flex items-baseline gap-2">
                      <span className="font-mono text-4xl font-extrabold tracking-tight text-white">
                        {isCancelled ? '---' : (appointment.queueNumber ?? '012')}
                      </span>
                      <span className="text-sm font-semibold text-sky-100">
                        / {appointment.roomCode.replace('P.', 'Buồng ')}
                      </span>
                    </div>

                    <p className="mt-3 text-xs leading-relaxed text-sky-100">
                      {isCancelled
                        ? 'Số thứ tự giữ trước đã được giải phóng sau khi hủy lịch hẹn.'
                        : `Vui lòng có mặt tại ${appointment.roomCode.replace('P.', 'Phòng ')} trước ${appointment.checkInDeadlineTime ?? '08:45'} để nhân viên tiếp đón xác nhận và điều dưỡng đo sinh hiệu.`}
                    </p>

                    {/* White Inner Check-in Box (SRS v1 Counter Check-in aligned) */}
                    <div className="mt-5 flex items-center justify-between gap-3 rounded-xl bg-white p-3.5 text-ink">
                      <div>
                        <p className="text-xs font-extrabold tracking-wide text-ink uppercase">
                          CHECK-IN NHANH
                        </p>
                        <p className="mt-0.5 text-[11px] text-sub">
                          Xuất trình mã QR tại{' '}
                          {appointment.checkInCounterLabel ?? 'Quầy Tiếp đón Tầng 1'}
                        </p>
                      </div>

                      <button
                        type="button"
                        onClick={() => setIsPrintModalOpen(true)}
                        aria-label="Mở mã QR và phiếu hẹn khám"
                        className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-navy font-mono text-xs font-extrabold text-white transition-opacity hover:opacity-90"
                      >
                        <QrCode className="h-5 w-5" aria-hidden="true" />
                      </button>
                    </div>
                  </section>

                  {/* Card 4: Biểu Phí Niêm Yết (Figma 17:1340) */}
                  <section
                    aria-label="Biểu Phí Niêm Yết"
                    className="rounded-2xl border border-border bg-white p-6 shadow-xs"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <h2 className="text-sm font-extrabold text-ink">Biểu Phí Niêm Yết</h2>
                      <span className="text-xs font-semibold text-sub">Tự nguyện 100%</span>
                    </div>

                    <div className="mt-4 flex items-baseline justify-between gap-2 border-b border-slate-100 pb-4">
                      <span className="text-xs font-medium text-sub">
                        {appointment.listedFeeServiceLabel ?? appointment.serviceName}
                      </span>
                      <span className="text-base font-extrabold text-ink">
                        {appointment.listedFee.toLocaleString('vi-VN')} VNĐ
                      </span>
                    </div>

                    <div className="mt-3 flex items-center justify-between gap-2 text-xs">
                      <span className="text-sub">Trạng thái thanh toán:</span>
                      <span
                        className={`font-bold ${
                          appointment.paymentStatus === 'PAID_VIETQR'
                            ? 'text-emerald-700'
                            : appointment.paymentStatus === 'REFUNDED_OR_FREE'
                              ? 'text-sub'
                              : 'text-amber-700'
                        }`}
                      >
                        {appointment.paymentStatusLabel}
                      </span>
                    </div>

                    <div className="mt-4 rounded-xl border border-slate-100 bg-canvas p-4 text-xs leading-relaxed text-sub">
                      <p className="font-bold text-ink">Chính sách khám ngoại trú:</p>
                      <p className="mt-1">
                        Dịch vụ khám hoàn toàn theo yêu cầu tự nguyện. Phòng khám không áp dụng
                        chính sách Bảo hiểm Y tế (BHYT).
                      </p>
                    </div>
                  </section>
                </div>
              </div>

              {/* Bottom Back Navigation */}
              <div className="flex items-center justify-between pt-2">
                <Link
                  to="/appointments"
                  className="inline-flex items-center gap-2 rounded-xl border border-border bg-white px-4 py-2.5 text-xs font-bold text-ink transition-colors hover:bg-canvas"
                >
                  <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Quay lại Danh sách Lịch hẹn của tôi (SCR-APT-08)</span>
                </Link>
              </div>
            </div>
          )}
        </div>
      </main>

      <PatientFooter />

      {/* Printable Slip Preview Modal (SCR-APT-07B) */}
      <AppointmentQuickViewModal
        appointment={isPrintModalOpen && appointment ? appointment : null}
        onClose={() => setIsPrintModalOpen(false)}
      />

      {/* Reschedule Modal (SCR-APT-10 Figma 17:1517) */}
      <AppointmentRescheduleModal
        appointment={isRescheduleModalOpen && appointment ? appointment : null}
        isSubmitting={rescheduleMutation.isPending}
        onClose={() => setIsRescheduleModalOpen(false)}
        onConfirmReschedule={handleConfirmReschedule}
      />

      {/* Cancel Confirmation Modal */}
      <AppointmentCancelModal
        appointment={isCancelModalOpen && appointment ? appointment : null}
        isSubmitting={cancelMutation.isPending}
        onClose={() => setIsCancelModalOpen(false)}
        onConfirmCancel={handleConfirmCancel}
      />
    </div>
  );
}
