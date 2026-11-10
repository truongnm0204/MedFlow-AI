import {
  ArrowLeft,
  CheckCircle2,
  ClipboardCheck,
  Pill,
  Printer,
  Save,
  ShieldAlert,
  ShieldCheck,
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DoctorSidebar } from '../components/doctor-sidebar';
import { PrescriptionDrugItemsTable } from '../components/prescription-drug-items-table';
import { CREATE_PRESCRIPTION_CONTEXT_MOCK } from '../data/create-prescription-mock';
import { DOCTOR_WORKBENCH_META } from '../data/doctor-mock-data';
import { createPrescriptionSchema } from '../schemas/create-prescription.schema';
import type {
  PrescriptionCatalogOption,
  PrescriptionDrugLineItem,
} from '../types/create-prescription.types';

const RX_WORKFLOW_STEPS = [
  'Tiền sử & Khảo sát',
  'Khám lâm sàng',
  'Chỉ định CLS',
  'Kết quả CLS trả về',
  'Chẩn đoán ICD-10',
  'Kê đơn thuốc (SCR-RX-01)',
] as const;

export function DoctorCreatePrescriptionPage() {
  const params = useParams<{ visitIdOrCode?: string }>();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const ctx = CREATE_PRESCRIPTION_CONTEXT_MOCK;
  const activeVisitCode = params.visitIdOrCode?.trim() || ctx.visitCode;

  const [items, setItems] = useState<PrescriptionDrugLineItem[]>(ctx.defaultItems);
  const [doctorAdvice, setDoctorAdvice] = useState(ctx.defaultAdvice);
  const [notice, setNotice] = useState<{
    tone: 'error' | 'info' | 'success';
    message: string;
  } | null>(null);
  const [isIssued, setIsIssued] = useState(false);

  const doctorName = user?.fullName?.trim() || ctx.doctorName;
  const roomLabel = `${DOCTOR_WORKBENCH_META.roomCode} • ${DOCTOR_WORKBENCH_META.departmentName}`;

  const handleQuantityChange = (itemId: string, nextQty: number) => {
    setItems((prev) => prev.map((it) => (it.id === itemId ? { ...it, quantity: nextQty } : it)));
  };

  const handleDosageChange = (itemId: string, nextDosage: string) => {
    setItems((prev) =>
      prev.map((it) => (it.id === itemId ? { ...it, dosageInstruction: nextDosage } : it)),
    );
  };

  const handleRemoveItem = (itemId: string) => {
    setItems((prev) => prev.filter((it) => it.id !== itemId));
  };

  const handleAddCatalogItem = (option: PrescriptionCatalogOption) => {
    if (option.isAllergyContraindicated) {
      setNotice({
        tone: 'error',
        message: `CHẶN AN TOÀN: ${option.contraindicationReason ?? 'Thuốc thuộc nhóm dị ứng của bệnh nhân!'}`,
      });
      return;
    }

    const exists = items.some((i) => i.drugCode === option.drugCode);
    if (exists) {
      setNotice({
        tone: 'info',
        message: `Thuốc ${option.drugName} đã có trong danh sách đơn.`,
      });
      return;
    }

    setItems((prev) => [
      ...prev,
      {
        id: `rx-${Date.now()}`,
        drugCode: option.drugCode,
        drugName: option.drugName,
        activeIngredient: option.activeIngredient,
        dosageForm: option.dosageForm,
        unit: option.unit,
        quantity: option.defaultQuantity,
        unitPrice: option.unitPrice,
        dosageInstruction: option.defaultDosage,
        mealTiming: option.defaultMealTiming,
      },
    ]);
    setNotice(null);
  };

  const handleIssuePrescription = () => {
    const parsed = createPrescriptionSchema.safeParse({
      items,
      doctorAdvice,
    });

    if (!parsed.success) {
      setNotice({
        tone: 'error',
        message: parsed.error.issues[0]?.message || 'Thông tin đơn thuốc chưa hợp lệ.',
      });
      return;
    }

    setIsIssued(true);
    setNotice({
      tone: 'success',
      message: `Đã ký số & phát hành Đơn thuốc điện tử ${ctx.rxCode} (Chuyển trực tiếp sang Nhà thuốc bệnh viện).`,
    });
  };

  const totalUnits = items.reduce((sum, i) => sum + i.quantity, 0);
  const totalAmount = items.reduce((sum, i) => sum + i.quantity * i.unitPrice, 0);

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DoctorSidebar
        activeItem="prescription"
        doctorName={doctorName}
        roomLabel={roomLabel}
        onItemSelect={(id) => {
          if (id === 'dashboard') navigate('/doctor');
          else if (id === 'consultation') navigate('/doctor/consultation');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/doctor/consultation')}
              aria-label="Quay lại bàn làm việc bác sĩ"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy flex items-center gap-2">
                <Pill className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Kê Đơn thuốc Điện tử Ngoại trú (SCR-RX-01)</span>
              </h1>
              <p className="truncate text-[11px] text-sub">
                Lượt khám <span className="font-mono font-bold">{activeVisitCode}</span> • Mã đơn{' '}
                <span className="font-mono font-bold text-primary">{ctx.rxCode}</span>
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
            className="rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-canvas disabled:opacity-50"
          >
            Đăng xuất
          </button>
        </header>

        <main className="flex-1 space-y-5 overflow-y-auto p-6 pb-28">
          {/* Patient Header & High Allergy Warning */}
          <section
            aria-label="Thông tin bệnh nhân và cảnh báo dị ứng"
            className="rounded-2xl border border-border bg-white p-5 shadow-2xs space-y-3"
          >
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-base font-bold text-navy">{ctx.patientName}</p>
                  <span className="text-xs text-sub">
                    • {ctx.gender}, {ctx.ageLabel}
                  </span>
                  <Badge tone={isIssued ? 'emerald' : 'sky'}>
                    {isIssued ? 'Đã ký số phát hành' : 'Đang kê đơn thuốc (SCR-RX-01)'}
                  </Badge>
                </div>
                <p className="mt-0.5 font-mono text-xs text-sub">
                  Mã BN: <strong className="text-navy">{ctx.patientCode}</strong> • Chẩn đoán:{' '}
                  <span className="font-sans font-semibold text-ink">{ctx.primaryDiagnosis}</span>
                </p>
              </div>
              <Badge tone="emerald">100% Khám Ngoại trú Tự nguyện</Badge>
            </div>

            <div
              role="note"
              className="rounded-xl border border-rose-300 bg-rose-50 px-4 py-2.5 text-xs text-rose-800 space-y-0.5"
            >
              <div className="flex items-center gap-2 font-bold">
                <ShieldAlert className="h-4 w-4 shrink-0 text-rose-600" aria-hidden="true" />
                <span>{ctx.allergyAlert}</span>
              </div>
              <p className="pl-6 font-semibold text-rose-700">{ctx.contraindications}</p>
            </div>
          </section>

          {/* Stepper */}
          <ol aria-label="Tiến trình khám ngoại trú" className="flex flex-wrap gap-2">
            {RX_WORKFLOW_STEPS.map((label, idx) => {
              const step = idx + 1;
              const isActive = step === 6;
              return (
                <li
                  key={label}
                  aria-current={isActive ? 'step' : undefined}
                  className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold ${
                    isActive
                      ? 'border-primary bg-primary text-white shadow-2xs'
                      : 'border-emerald-200 bg-emerald-50 text-emerald-700'
                  }`}
                >
                  <span>{step}.</span>
                  <span>{label}</span>
                </li>
              );
            })}
          </ol>

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone={notice.tone}>{notice.message}</Alert>
            </div>
          )}

          {/* Regimen Preset Highlight */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-sky-200 bg-sky-50/70 px-4 py-3 text-xs">
            <div className="flex items-center gap-2 font-bold text-navy">
              <ClipboardCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              <span>
                Đơn mẫu áp dụng: Phác đồ 4 thuốc diệt trừ H. pylori (Bismuth Quadruple - 14 ngày)
              </span>
            </div>
            <Badge tone="sky">Thay thế nhóm Penicillin</Badge>
          </div>

          {/* Drug Items Table */}
          <PrescriptionDrugItemsTable
            items={items}
            catalog={ctx.catalog}
            onQuantityChange={handleQuantityChange}
            onDosageChange={handleDosageChange}
            onRemoveItem={handleRemoveItem}
            onAddCatalogItem={handleAddCatalogItem}
          />

          {/* Bottom Clinical Safety Check + Cost Summary */}
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            <div className="space-y-4 lg:col-span-8">
              <Card className="border-emerald-200 bg-emerald-50/40">
                <CardHeader className="py-3">
                  <div className="flex items-center gap-2 text-xs font-bold uppercase text-emerald-800">
                    <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                    <span>Kiểm tra An toàn Lâm sàng &amp; Tương tác Thuốc (Automated Check)</span>
                  </div>
                </CardHeader>
                <CardContent className="text-xs text-emerald-900 space-y-1">
                  <p>
                    • Phác đồ 4 thuốc hiện tại hoàn toàn không chứa hoạt chất nhóm Penicillin /
                    Beta-lactam và NSAIDs.
                  </p>
                  <p>
                    • Lưu ý tư vấn người bệnh kiêng tuyệt đối rượu bia khi dùng Klion 500mg
                    (Metronidazole).
                  </p>
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="py-3">
                  <label
                    htmlFor="doctor-rx-advice"
                    className="text-xs font-bold uppercase text-navy"
                  >
                    Lời dặn của Bác sĩ &amp; Chế độ Sinh hoạt Ngoại trú
                  </label>
                </CardHeader>
                <CardContent>
                  <textarea
                    id="doctor-rx-advice"
                    rows={3}
                    value={doctorAdvice}
                    onChange={(e) => setDoctorAdvice(e.target.value)}
                    className="w-full rounded-lg border border-border bg-white p-3 text-xs text-ink focus:outline-hidden focus:ring-2 focus:ring-primary"
                  />
                </CardContent>
              </Card>
            </div>

            <div className="lg:col-span-4">
              <Card>
                <CardHeader className="border-b border-border bg-slate-50/70 py-3">
                  <h2 className="text-xs font-bold uppercase text-navy">
                    Tổng kết Viện phí Toa thuốc
                  </h2>
                </CardHeader>
                <CardContent className="space-y-3 p-4 text-xs">
                  <div className="flex justify-between">
                    <span className="text-sub">Số khoản thuốc:</span>
                    <strong className="font-mono text-navy">{items.length} khoản</strong>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sub">Tổng số đơn vị:</span>
                    <strong className="font-mono text-navy">{totalUnits} đơn vị</strong>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-sub">Số ngày cấp:</span>
                    <strong className="font-mono text-navy">14 ngày</strong>
                  </div>
                  <div className="border-t border-border pt-3 flex items-center justify-between">
                    <span className="font-bold text-navy">TỔNG THANH TOÁN:</span>
                    <span
                      data-testid="prescription-total-amount"
                      className="font-mono text-lg font-black text-primary"
                    >
                      {totalAmount.toLocaleString('vi-VN')} đ
                    </span>
                  </div>
                </CardContent>
              </Card>
            </div>
          </div>
        </main>

        <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 shadow-sm print:hidden">
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() =>
                setNotice({ tone: 'info', message: `Đã lưu nháp đơn thuốc ${ctx.rxCode}.` })
              }
              className="gap-1.5"
            >
              <Save className="h-4 w-4" aria-hidden="true" />
              <span>Lưu nháp</span>
            </Button>

            <Button
              variant="secondary"
              size="sm"
              onClick={() => window.print()}
              className="gap-1.5"
            >
              <Printer className="h-4 w-4" aria-hidden="true" />
              <span>In toa thuốc</span>
            </Button>
          </div>

          <Button
            variant="primary"
            size="sm"
            onClick={handleIssuePrescription}
            className="gap-1.5 shadow-xs"
          >
            <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
            <span>Ký số &amp; Phát hành Đơn thuốc (Chuyển Nhà thuốc)</span>
          </Button>
        </footer>
      </div>
    </div>
  );
}
