import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, ArrowRight, UserPlus } from 'lucide-react';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { useLogout } from '@/features/auth/hooks/use-auth';
import { Button } from '@/shared/ui/button';

import { ReceptionSidebar } from '../components/reception-sidebar';
import { WalkInClinicalSection } from '../components/walk-in-clinical-section';
import { WalkInPatientBanner } from '../components/walk-in-patient-banner';
import { WalkInPaymentSection } from '../components/walk-in-payment-section';
import { WalkInSuccessModal } from '../components/walk-in-success-modal';
import {
  SYMPTOM_OPTIONS_MOCK,
  WALK_IN_DEPARTMENTS_MOCK,
  WALK_IN_PATIENT_MOCK,
} from '../data/walk-in-visit-mock';
import { walkInVisitSchema } from '../schemas/walk-in-visit.schema';
import type {
  ClinicalPriority,
  CreatedWalkInVisitResult,
  PaymentMethod,
} from '../types/walk-in-visit.types';

export function WalkInVisitPage() {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const [patient] = useState(WALK_IN_PATIENT_MOCK);
  const [priority, setPriority] = useState<ClinicalPriority>('STANDARD');
  const [departmentId, setDepartmentId] = useState('dept-gastro');
  const [serviceId, setServiceId] = useState('srv-gastro-01');
  const [reasonForVisit, setReasonForVisit] = useState(
    'Bệnh nhân đau quặn vùng thượng vị sau ăn khoảng 1-2 tiếng, ợ hơi chua nhiều kèm cảm giác cồn cào buồn nôn 2 ngày nay. Không sốt, chưa dùng thuốc gì ở nhà.',
  );
  const [symptoms, setSymptoms] = useState<string[]>(['Đau thượng vị', 'Ợ hơi/ợ chua', 'Buồn nôn']);
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>('VIETQR');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successResult, setSuccessResult] = useState<CreatedWalkInVisitResult | null>(null);

  const selectedDept = WALK_IN_DEPARTMENTS_MOCK.find((d) => d.id === departmentId);
  const currentService = selectedDept
    ? selectedDept.services.find((s) => s.id === serviceId) || null
    : null;

  const handleDepartmentChange = (newDeptId: string) => {
    setDepartmentId(newDeptId);
    const dept = WALK_IN_DEPARTMENTS_MOCK.find((d) => d.id === newDeptId);
    if (dept && dept.services.length > 0) {
      setServiceId(dept.services[0].id);
    } else {
      setServiceId('');
    }
  };

  const handleToggleSymptom = (sym: string) => {
    setSymptoms((prev) => (prev.includes(sym) ? prev.filter((s) => s !== sym) : [...prev, sym]));
  };

  const handleSubmit = () => {
    const parseResult = walkInVisitSchema.safeParse({
      patientId: patient.id,
      clinicalPriority: priority,
      departmentId,
      serviceId,
      reasonForVisit,
      symptoms,
      paymentMethod,
    });

    if (!parseResult.success) {
      const fieldErrors: Record<string, string> = {};
      for (const issue of parseResult.error.issues) {
        const path = issue.path[0] as string;
        if (path && !fieldErrors[path]) {
          fieldErrors[path] = issue.message;
        }
      }
      setErrors(fieldErrors);
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    setTimeout(() => {
      setIsSubmitting(false);
      setSuccessResult({
        visitId: `VIS-${Date.now().toString().slice(-6)}`,
        visitCode: 'VIS-2026-0415-0145',
        ticketNo: 'A019',
        patientName: patient.fullName,
        departmentName: selectedDept ? selectedDept.name : 'Khoa Tiêu hóa',
        serviceName: currentService ? currentService.name : 'Khám chuyên khoa',
        receptionTime: new Date().toLocaleTimeString('vi-VN', {
          hour: '2-digit',
          minute: '2-digit',
        }),
        paymentMethod,
        priceFormatted: currentService ? currentService.priceFormatted : '250.000 VNĐ',
      });
    }, 600);
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar activeItem="dashboard" />

      <div className="flex-1 flex flex-col min-w-0">
        {/* Top Header */}
        <header className="h-16 bg-white border-b border-slate-200 px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/reception')}
              aria-label="Quay lại bàn tiếp đón"
              className="p-2 rounded-xl text-slate-500 hover:text-slate-800 hover:bg-slate-100 transition-colors"
            >
              <ArrowLeft className="w-5 h-5" aria-hidden="true" />
            </button>
            <div>
              <div className="text-sm font-bold text-slate-900">
                Đăng ký Khám Vãng lai tại Quầy (SCR-REC-06)
              </div>
              <div className="text-[11px] text-slate-500">
                Phân hệ Nhân viên &gt; Quầy Tiếp đón Ngoại trú &gt; Đăng ký vãng lai
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div className="flex items-center gap-2.5 pl-3 border-l border-slate-200">
              <div className="w-9 h-9 rounded-full bg-emerald-100 border border-emerald-200 text-emerald-700 flex items-center justify-center font-bold text-xs">
                {(user?.fullName ?? 'LT').slice(0, 2).toUpperCase()}
              </div>
              <div className="text-right">
                <div className="text-xs font-bold text-slate-800">
                  {user?.fullName ?? 'Nhân viên tiếp đón'}
                </div>
                <button
                  type="button"
                  onClick={() => logout.mutate()}
                  className="text-[10px] text-slate-400 hover:text-rose-600 transition-colors"
                >
                  Đăng xuất
                </button>
              </div>
            </div>
          </div>
        </header>

        {/* Main Content */}
        <main className="p-6 space-y-6 flex-1 max-w-5xl w-full mx-auto pb-28">
          {/* Patient Banner */}
          <WalkInPatientBanner
            patient={patient}
            onChangePatient={() => navigate('/reception/patients')}
          />

          {/* Clinical Information Section */}
          <WalkInClinicalSection
            priority={priority}
            onPriorityChange={setPriority}
            departments={WALK_IN_DEPARTMENTS_MOCK}
            selectedDepartmentId={departmentId}
            onDepartmentChange={handleDepartmentChange}
            selectedServiceId={serviceId}
            onServiceChange={setServiceId}
            currentService={currentService}
            reasonForVisit={reasonForVisit}
            onReasonChange={setReasonForVisit}
            symptoms={symptoms}
            onToggleSymptom={handleToggleSymptom}
            symptomOptions={SYMPTOM_OPTIONS_MOCK}
            errors={errors}
          />

          {/* Payment Section */}
          <WalkInPaymentSection
            paymentMethod={paymentMethod}
            onPaymentMethodChange={setPaymentMethod}
            service={currentService}
          />
        </main>

        {/* Sticky Bottom Action Bar */}
        <div className="fixed bottom-0 left-0 lg:left-[260px] right-0 bg-white/95 backdrop-blur-md border-t border-slate-200 px-6 py-4 flex items-center justify-between z-30 shadow-lg">
          <Button
            variant="secondary"
            size="md"
            onClick={() => navigate('/reception')}
            className="gap-2"
          >
            <ArrowLeft className="w-4 h-4" aria-hidden="true" />
            <span>Hủy &amp; Quay lại Bàn tiếp đón</span>
          </Button>

          <Button
            variant="primary"
            size="md"
            loading={isSubmitting}
            onClick={handleSubmit}
            className="gap-2 shadow-xs px-6"
          >
            <UserPlus className="w-4 h-4" aria-hidden="true" />
            <span>Tạo lượt khám &amp; Chuyển sang phân buồng</span>
            <ArrowRight className="w-4 h-4" aria-hidden="true" />
          </Button>
        </div>
      </div>

      {/* Success Modal */}
      <WalkInSuccessModal
        isOpen={!!successResult}
        result={successResult}
        onClose={() => {
          setSuccessResult(null);
          navigate('/reception');
        }}
        onGoToRouting={() => {
          setSuccessResult(null);
          navigate('/reception');
        }}
      />
    </div>
  );
}
