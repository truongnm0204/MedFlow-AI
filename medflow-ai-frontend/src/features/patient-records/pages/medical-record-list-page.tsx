import { useState } from 'react';
import {
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  FileText,
  Home,
  RefreshCw,
  ShieldCheck,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { MedicalRecordCard } from '../components/medical-record-card';
import { MedicalRecordFilterBar } from '../components/medical-record-filter-bar';
import { PatientRecordsSubnav } from '../components/patient-records-subnav';
import { usePatientMedicalRecords } from '../hooks/use-patient-medical-records';
import type { PatientMedicalRecordFilterParams } from '../types/patient-records.types';

const DEFAULT_PAGE_SIZE = 10;

export function MedicalRecordListPage() {
  const [filters, setFilters] = useState<PatientMedicalRecordFilterParams>({
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  const query = usePatientMedicalRecords(filters);
  const data = query.data;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const currentPage = filters.page ?? 1;

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pt-5 pb-16">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
            <span className="font-semibold text-ink">Hồ sơ bệnh án điện tử (EMR)</span>
          </nav>

          <PatientRecordsSubnav />

          <header className="mb-6 flex flex-wrap items-end justify-between gap-4">
            <div>
              <h1 className="text-2xl font-bold text-navy">Hồ sơ bệnh án điện tử (EMR)</h1>
              <p className="mt-1 text-xs leading-relaxed text-sub">
                Tra cứu lịch sử khám lâm sàng đã hoàn tất, chẩn đoán ICD-10, kết quả cận lâm sàng và
                đơn thuốc của bạn cùng thành viên gia đình đã xác minh PHI.
              </p>
            </div>
            <div className="inline-flex items-center gap-2 rounded-xl border border-sky-200 bg-sky-50 px-3.5 py-2 text-xs font-medium text-primary">
              <ShieldCheck className="h-4 w-4 shrink-0" aria-hidden="true" />
              <span>Bảo vệ quyền riêng tư PHI 2 lớp (OTP / Nhân viên xác thực)</span>
            </div>
          </header>

          <MedicalRecordFilterBar
            members={data?.members ?? []}
            hospitals={data?.hospitals ?? []}
            specialties={data?.specialties ?? []}
            initialValues={{
              patientId: filters.patientId ? String(filters.patientId) : '',
              fromDate: filters.fromDate ?? '',
              toDate: filters.toDate ?? '',
              hospitalId: filters.hospitalId ? String(filters.hospitalId) : '',
              specialtyCode: filters.specialtyCode ?? '',
              keyword: filters.keyword ?? '',
            }}
            onApply={(next) =>
              setFilters({
                ...next,
                page: 1,
                pageSize: DEFAULT_PAGE_SIZE,
              })
            }
            onReset={() => setFilters({ page: 1, pageSize: DEFAULT_PAGE_SIZE })}
          />

          <div className="mt-6">
            {query.isPending && (
              <div
                role="status"
                className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs"
              >
                <div className="mx-auto h-9 w-9 animate-spin rounded-full border-4 border-slate-200 border-t-primary" />
                <p className="mt-3 text-sm font-semibold text-ink">
                  Đang tải danh sách hồ sơ bệnh án...
                </p>
              </div>
            )}

            {!query.isPending && query.isError && (
              <div
                role="alert"
                className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
              >
                <AlertCircle className="mx-auto h-8 w-8 text-danger" aria-hidden="true" />
                <h2 className="mt-3 text-base font-bold text-ink">
                  Không thể tải danh sách hồ sơ bệnh án
                </h2>
                <p className="mx-auto mt-1 max-w-md text-xs text-sub">
                  {getErrorMessage(
                    query.error,
                    'Không thể truy cập dữ liệu bệnh án lúc này. Vui lòng thử lại.',
                  )}
                </p>
                <button
                  type="button"
                  onClick={() => void query.refetch()}
                  className="mt-4 inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white hover:bg-primary-dark"
                >
                  <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Thử lại</span>
                </button>
              </div>
            )}

            {!query.isPending && !query.isError && data && data.items.length === 0 && (
              <div className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs">
                <FileText className="mx-auto h-10 w-10 text-slate-300" aria-hidden="true" />
                <h2 className="mt-3 text-base font-bold text-navy">
                  Chưa có hồ sơ bệnh án hoàn tất phù hợp
                </h2>
                <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-sub">
                  Hồ sơ bệnh án sẽ tự động hiển thị tại đây ngay sau khi bác sĩ hoàn tất lượt khám
                  lâm sàng. Nếu bạn đã từng khám trước đây, hãy kiểm tra liên kết hồ sơ bệnh nhân.
                </p>
                <div className="mt-5 flex flex-wrap items-center justify-center gap-3">
                  <Link
                    to="/booking"
                    className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white hover:bg-primary-dark"
                  >
                    <span>Đặt lịch khám mới</span>
                  </Link>
                  <Link
                    to="/patient/identity-claims"
                    className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-4 py-2 text-xs font-semibold text-navy hover:bg-canvas"
                  >
                    <span>Liên kết hồ sơ đã có</span>
                  </Link>
                </div>
              </div>
            )}

            {!query.isPending && !query.isError && data && data.items.length > 0 && (
              <div className="space-y-4">
                <div className="flex items-center justify-between text-xs text-sub">
                  <span>
                    Hiển thị <strong className="text-ink">{data.items.length}</strong> trên tổng số{' '}
                    <strong className="text-ink">{data.totalCount}</strong> hồ sơ bệnh án đã khóa
                  </span>
                </div>

                {data.items.map((item) => (
                  <MedicalRecordCard key={item.medicalRecordId} record={item} />
                ))}

                {totalPages > 1 && (
                  <div className="flex items-center justify-between rounded-2xl border border-border bg-white px-4 py-3 text-xs">
                    <span className="text-sub">
                      Trang <strong className="text-ink">{currentPage}</strong> / {totalPages}
                    </span>
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        disabled={currentPage <= 1}
                        onClick={() =>
                          setFilters((prev) => ({ ...prev, page: Math.max(1, currentPage - 1) }))
                        }
                        className="inline-flex items-center gap-1 rounded-xl border border-border px-3 py-1.5 font-semibold text-navy hover:bg-canvas disabled:opacity-60"
                      >
                        <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
                        <span>Trước</span>
                      </button>
                      <button
                        type="button"
                        disabled={currentPage >= totalPages}
                        onClick={() =>
                          setFilters((prev) => ({
                            ...prev,
                            page: Math.min(totalPages, currentPage + 1),
                          }))
                        }
                        className="inline-flex items-center gap-1 rounded-xl border border-border px-3 py-1.5 font-semibold text-navy hover:bg-canvas disabled:opacity-60"
                      >
                        <span>Sau</span>
                        <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
