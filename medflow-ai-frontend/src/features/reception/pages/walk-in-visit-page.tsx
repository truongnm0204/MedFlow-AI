import { ArrowLeft, ArrowRight, RefreshCw, UserPlus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { ReceptionSidebar } from '../components/reception-sidebar';
import { WalkInClinicalSection } from '../components/walk-in-clinical-section';
import { WalkInPatientBanner } from '../components/walk-in-patient-banner';
import { WalkInPaymentSection } from '../components/walk-in-payment-section';
import { WalkInSuccessModal } from '../components/walk-in-success-modal';
import {
  useCreateWalkInMutation,
  useReceptionPatientSearch,
  useWalkInCatalog,
} from '../hooks/use-reception-api';
import { walkInVisitSchema } from '../schemas/walk-in-visit.schema';
import type {
  ClinicalPriority,
  CreatedWalkInVisitResult,
  PaymentMethod,
  WalkInPatient,
} from '../types/walk-in-visit.types';

export function WalkInVisitPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const patientCode = searchParams.get('patientCode')?.trim() ?? '';
  const catalogQuery = useWalkInCatalog();
  const patientsQuery = useReceptionPatientSearch(patientCode, 'ALL');
  const createWalkIn = useCreateWalkInMutation();

  const [priority, setPriority] = useState<ClinicalPriority>('STANDARD');
  const [departmentId, setDepartmentId] = useState('');
  const [serviceId, setServiceId] = useState('');
  const [reasonForVisit, setReasonForVisit] = useState('');
  const [symptoms, setSymptoms] = useState<string[]>([]);
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>('VIETQR');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [successResult, setSuccessResult] = useState<CreatedWalkInVisitResult | null>(null);

  const departments = catalogQuery.data?.departments ?? [];
  const selectedDept = departments.find((d) => d.id === departmentId) ?? departments[0] ?? null;
  const currentService =
    selectedDept?.services.find((s) => s.id === serviceId) ?? selectedDept?.services[0] ?? null;
  const symptomOptions = catalogQuery.data?.symptomOptions ?? [];

  const patient = useMemo<WalkInPatient | null>(() => {
    const found = patientsQuery.data?.items.find((item) => item.patientCode === patientCode);
    if (!found) return null;
    const birthYear = Number.parseInt(found.dateOfBirth.slice(0, 4), 10);
    return {
      id: found.id,
      patientCode: found.patientCode,
      fullName: found.fullName,
      gender: found.gender,
      birthYear: Number.isFinite(birthYear) ? birthYear : 0,
      identityCard: found.identityCard,
      phoneNumber: found.phoneNumber,
      insuranceType: '',
    };
  }, [patientCode, patientsQuery.data]);

  const handleDepartmentChange = (newDeptId: string) => {
    setDepartmentId(newDeptId);
    setServiceId('');
  };

  const handleToggleSymptom = (symptom: string) => {
    setSymptoms((prev) =>
      prev.includes(symptom) ? prev.filter((value) => value !== symptom) : [...prev, symptom],
    );
  };

  const handleSubmit = async () => {
    if (!patient) {
      setErrors({ patientId: 'Vui lòng chọn bệnh nhân trước khi tạo lượt khám.' });
      return;
    }

    const parseResult = walkInVisitSchema.safeParse({
      patientId: patient.id,
      clinicalPriority: priority,
      departmentId: selectedDept?.id ?? '',
      serviceId: currentService?.id ?? '',
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
    try {
      const result = await createWalkIn.mutateAsync({
        patient,
        departmentId: selectedDept!.id,
        serviceId: currentService!.id,
        reasonForVisit,
        symptoms,
        clinicalPriority: priority,
        paymentMethod,
        serviceName: currentService?.name ?? '',
        priceFormatted: currentService?.priceFormatted ?? '',
      });
      setSuccessResult(result);
    } catch (error) {
      setErrors({ form: getErrorMessage(error) });
    }
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar activeItem="dashboard" />

      <div className="flex-1 flex flex-col min-w-0">
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

        <main className="p-6 space-y-6 flex-1 max-w-5xl w-full mx-auto pb-28">
          {errors.form ? <Alert tone="error">{errors.form}</Alert> : null}

          {catalogQuery.isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(catalogQuery.error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void catalogQuery.refetch()}
                loading={catalogQuery.isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {catalogQuery.isPending ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải danh mục khoa và dịch vụ...
            </div>
          ) : null}

          {patient ? (
            <WalkInPatientBanner
              patient={patient}
              onChangePatient={() => navigate('/reception/patients')}
            />
          ) : (
            <Alert tone="warning">
              Chưa chọn bệnh nhân.{' '}
              <button
                type="button"
                onClick={() => navigate('/reception/patients')}
                className="font-semibold underline"
              >
                Tra cứu và chọn bệnh nhân
              </button>{' '}
              trước khi tạo lượt khám vãng lai.
            </Alert>
          )}

          {catalogQuery.isSuccess ? (
            <>
              <WalkInClinicalSection
                priority={priority}
                onPriorityChange={setPriority}
                departments={departments}
                selectedDepartmentId={selectedDept?.id ?? ''}
                onDepartmentChange={handleDepartmentChange}
                selectedServiceId={currentService?.id ?? ''}
                onServiceChange={setServiceId}
                currentService={currentService}
                reasonForVisit={reasonForVisit}
                onReasonChange={setReasonForVisit}
                symptoms={symptoms}
                onToggleSymptom={handleToggleSymptom}
                symptomOptions={symptomOptions}
                errors={errors}
              />

              <WalkInPaymentSection
                paymentMethod={paymentMethod}
                onPaymentMethodChange={setPaymentMethod}
                service={currentService}
              />
            </>
          ) : null}
        </main>

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
            loading={createWalkIn.isPending}
            disabled={!patient || !currentService}
            onClick={() => void handleSubmit()}
            className="gap-2 shadow-xs px-6"
          >
            <UserPlus className="w-4 h-4" aria-hidden="true" />
            <span>Tạo lượt khám &amp; Chuyển sang phân buồng</span>
            <ArrowRight className="w-4 h-4" aria-hidden="true" />
          </Button>
        </div>
      </div>

      <WalkInSuccessModal
        isOpen={Boolean(successResult)}
        result={successResult}
        onClose={() => {
          setSuccessResult(null);
          navigate('/reception');
        }}
        onGoToRouting={() => {
          const visitId = successResult?.visitCode ?? successResult?.visitId;
          setSuccessResult(null);
          if (visitId) {
            navigate(`/reception/routing?visitId=${encodeURIComponent(visitId)}`);
          }
        }}
      />
    </div>
  );
}
