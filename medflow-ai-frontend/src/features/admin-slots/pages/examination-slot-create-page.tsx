import { zodResolver } from '@hookform/resolvers/zod';
import { Clock, Info } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { SlotCreateActionBar } from '../components/slot-create-action-bar';
import { SlotCreateForm } from '../components/slot-create-form';
import { SlotCreateSidebar } from '../components/slot-create-sidebar';
import {
  DEFAULT_SLOT_FORM_VALUES,
  examinationSlotSchema,
  type ExaminationSlotFormValues,
} from '../schemas/examination-slot.schema';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ExaminationSlot, ServiceType } from '../types/examination-slot.types';

export function ExaminationSlotCreatePage() {
  const navigate = useNavigate();
  const slots = useExaminationSlotStore((state) => state.slots);
  const draft = useExaminationSlotStore((state) => state.draft);
  const addSlot = useExaminationSlotStore((state) => state.addSlot);
  const saveDraft = useExaminationSlotStore((state) => state.saveDraft);
  const clearDraft = useExaminationSlotStore((state) => state.clearDraft);

  const [message, setMessage] = useState<string | null>(null);
  const [isDraftSaved, setIsDraftSaved] = useState(false);
  const [conflictStatus, setConflictStatus] = useState<{
    checked: boolean;
    hasConflict: boolean;
    message: string;
  } | null>(null);

  const form = useForm<ExaminationSlotFormValues>({
    resolver: zodResolver(examinationSlotSchema),
    defaultValues: {
      code: draft?.code ?? DEFAULT_SLOT_FORM_VALUES.code,
      serviceType: draft?.serviceType ?? DEFAULT_SLOT_FORM_VALUES.serviceType,
      serviceId: draft?.serviceId ?? DEFAULT_SLOT_FORM_VALUES.serviceId,
      serviceName: draft?.serviceName ?? DEFAULT_SLOT_FORM_VALUES.serviceName,
      serviceCode: draft?.serviceCode ?? DEFAULT_SLOT_FORM_VALUES.serviceCode,
      specialty: draft?.specialty ?? DEFAULT_SLOT_FORM_VALUES.specialty,
      date: draft?.date ?? DEFAULT_SLOT_FORM_VALUES.date,
      startTime: draft?.startTime ?? DEFAULT_SLOT_FORM_VALUES.startTime,
      endTime: draft?.endTime ?? DEFAULT_SLOT_FORM_VALUES.endTime,
      facility: draft?.facility ?? DEFAULT_SLOT_FORM_VALUES.facility,
      capacityMax: draft?.capacityMax ?? DEFAULT_SLOT_FORM_VALUES.capacityMax,
      room: draft?.room ?? DEFAULT_SLOT_FORM_VALUES.room,
      doctorId: draft?.doctorId ?? DEFAULT_SLOT_FORM_VALUES.doctorId,
      doctorName: draft?.doctorName ?? DEFAULT_SLOT_FORM_VALUES.doctorName,
      status: draft?.status ?? DEFAULT_SLOT_FORM_VALUES.status,
    },
  });

  const {
    handleSubmit,
    setValue,
    getValues,
    control,
    formState: { isDirty, isSubmitting },
  } = form;

  const currentServiceType = useWatch({ control, name: 'serviceType' }) ?? 'OUTPATIENT';
  const currentRoom = useWatch({ control, name: 'room' }) ?? '';
  const currentCapacity = useWatch({ control, name: 'capacityMax' }) ?? 8;

  // Auto-save draft every 30s when dirty
  useEffect(() => {
    if (!isDirty) return;
    const timer = window.setInterval(() => {
      saveDraft(getValues());
      setIsDraftSaved(true);
    }, 30_000);
    return () => window.clearInterval(timer);
  }, [getValues, isDirty, saveDraft]);

  // Global hotkeys (Esc to cancel, Ctrl+S to save)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        navigate('/admin/slots');
      }
      if (e.ctrlKey && e.key.toLowerCase() === 's') {
        e.preventDefault();
        document.getElementById('slot-create-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  const handleManualDraftSave = () => {
    saveDraft(getValues());
    setIsDraftSaved(true);
    setMessage('Đã lưu bản nháp khung giờ khám vào bộ nhớ phiên làm việc.');
    setTimeout(() => setMessage(null), 3000);
  };

  const handleCheckConflict = () => {
    const values = getValues();
    if (!values.date || !values.startTime || !values.endTime) {
      setConflictStatus({
        checked: true,
        hasConflict: true,
        message: 'Vui lòng điền đầy đủ ngày khám, giờ bắt đầu và giờ kết thúc để kiểm tra.',
      });
      return;
    }

    // Check doctor conflict
    if (values.doctorId) {
      const doctorConflict = slots.find(
        (s) =>
          s.date === values.date &&
          s.doctorId === values.doctorId &&
          !(values.endTime <= s.startTime || values.startTime >= s.endTime),
      );
      if (doctorConflict) {
        setConflictStatus({
          checked: true,
          hasConflict: true,
          message: `Xung đột lịch bác sĩ: ${doctorConflict.doctorName} đã có slot ${doctorConflict.code} (${doctorConflict.startTime}–${doctorConflict.endTime}) vào ngày này.`,
        });
        return;
      }
    }

    // Check room conflict
    if (values.room) {
      const roomConflict = slots.find(
        (s) =>
          s.date === values.date &&
          s.room === values.room &&
          !(values.endTime <= s.startTime || values.startTime >= s.endTime),
      );
      if (roomConflict) {
        setConflictStatus({
          checked: true,
          hasConflict: true,
          message: `Xung đột phòng khám: Phòng ${values.room} đã có slot ${roomConflict.code} (${roomConflict.startTime}–${roomConflict.endTime}) vào ngày này.`,
        });
        return;
      }
    }

    setConflictStatus({
      checked: true,
      hasConflict: false,
      message: 'Tài nguyên hợp lệ, không phát hiện xung đột lịch bác sĩ và phòng khám.',
    });
  };

  const handleViewBookings = () => {
    setMessage('Hiện chưa có lượt đặt nào trên khung giờ mới khởi tạo.');
    setTimeout(() => setMessage(null), 3000);
  };

  const handleCreate = (values: ExaminationSlotFormValues) => {
    if (slots.some((s) => s.code.toLowerCase() === values.code.trim().toLowerCase())) {
      setMessage(`Mã slot "${values.code}" đã tồn tại trên hệ thống.`);
      return;
    }

    // Compute dateDisplay: DD/MM
    const dateParts = values.date.split('-');
    const dateDisplay = dateParts.length === 3 ? `${dateParts[2]}/${dateParts[1]}` : values.date;

    const newSlot: ExaminationSlot = {
      id: values.code.toLowerCase().replace(/[^a-z0-9]/g, '-'),
      code: values.code,
      doctorId: values.doctorId || 'doc-unassigned',
      doctorName: values.doctorName || 'Chưa phân công',
      serviceId: values.serviceId,
      serviceName: values.serviceName,
      serviceCode: values.serviceCode,
      serviceType: values.serviceType,
      specialty: values.specialty,
      room: values.room || 'Chưa xếp phòng',
      capacityBooked: 0,
      capacityMax: values.capacityMax,
      date: values.date,
      dateDisplay,
      startTime: values.startTime,
      endTime: values.endTime,
      status: values.status || 'OPEN',
    };

    addSlot(newSlot);
    clearDraft();
    navigate('/admin/slots');
  };

  return (
    <AdminPortalShell
      showTabs={false}
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Quản lý Slot khám', href: '/admin/slots' },
        { label: 'Tạo khung giờ khám (SCR-ADM-SLT-02)' },
      ]}
    >
      <form onSubmit={handleSubmit(handleCreate)}>
        <main className="mx-auto max-w-[1720px] px-6 pb-28 pt-6">
          {/* Subheader bar matching Figma SCR-ADM-SLT-02 */}
          <div className="flex flex-wrap items-center justify-between gap-3 text-xs">
            <div className="flex flex-wrap items-center gap-3">
              <Link
                to="/admin/slots"
                className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-sub shadow-2xs hover:border-slate-300 hover:text-primary transition-colors"
              >
                ← Quay lại Danh sách Slot khám (SCR-ADM-SLT-01)
              </Link>
              <span className="text-slate-300">|</span>
              <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-[11px] font-semibold text-primary">
                ● Tạo mới (Draft Mode)
              </span>
              <span className="text-slate-500 text-[11px]">Mã màn hình: SCR-ADM-SLT-02</span>
            </div>

            <div className="flex items-center gap-2 text-xs text-slate-500">
              <Clock className="h-3.5 w-3.5 text-slate-400" />
              <span>{isDraftSaved ? 'Đã lưu bản nháp' : 'Thay đổi chưa lưu'}</span>
            </div>
          </div>

          {/* Page Heading */}
          <div className="mt-4">
            <h1 className="text-2xl font-bold tracking-tight text-ink">Tạo khung giờ khám</h1>
            <p className="mt-1 text-xs text-slate-500">
              Gán dịch vụ, bác sĩ, thời gian, phòng và sức chứa cho slot.
            </p>
          </div>

          {/* Operational Notice Banner */}
          <div className="my-5 flex items-start gap-3 rounded-lg border border-sky-200 bg-sky-50/70 p-4 text-xs leading-relaxed text-sky-900 shadow-2xs">
            <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-white text-sky-600 shadow-2xs mt-0.5">
              <Info className="h-4 w-4" />
            </span>
            <p>
              <span className="font-bold">Lưu ý vận hành:</span> Mở slot khi dịch vụ hợp lệ, giờ kết
              thúc sau giờ bắt đầu và tài nguyên không xung đột. Không giảm sức chứa dưới số lượt đã
              đặt.
            </p>
          </div>

          {/* Status Message / Notification */}
          {message && (
            <div
              role="status"
              className="mb-5 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800 shadow-2xs"
            >
              {message}
            </div>
          )}

          {/* Main 2-Column Grid */}
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
            {/* Left 2 Cols: Form Fields */}
            <div className="lg:col-span-2">
              <SlotCreateForm
                form={form}
                serviceType={currentServiceType as ServiceType}
                onServiceTypeChange={(type) => {
                  setValue('serviceType', type, { shouldValidate: true, shouldDirty: true });
                  // Reset service when type changes
                  setValue('serviceId', '', { shouldValidate: true });
                }}
                selectedRoom={currentRoom}
                onRoomSelect={(roomCode) =>
                  setValue('room', roomCode, { shouldValidate: true, shouldDirty: true })
                }
                onRoomClear={() =>
                  setValue('room', '', { shouldValidate: true, shouldDirty: true })
                }
                capacityMax={currentCapacity}
                onCapacityQuickPick={(cap) =>
                  setValue('capacityMax', cap, { shouldValidate: true, shouldDirty: true })
                }
              />
            </div>

            {/* Right 1 Col: Resource Verification & Capacity Control Sidebar */}
            <div className="lg:col-span-1">
              <SlotCreateSidebar
                onCheckConflict={handleCheckConflict}
                conflictStatus={conflictStatus}
                onViewBookings={handleViewBookings}
              />
            </div>
          </div>
        </main>

        {/* Bottom Fixed Action Bar */}
        <SlotCreateActionBar
          onCancel={() => navigate('/admin/slots')}
          onSaveDraft={handleManualDraftSave}
          isSubmitting={isSubmitting}
        />
      </form>
    </AdminPortalShell>
  );
}
