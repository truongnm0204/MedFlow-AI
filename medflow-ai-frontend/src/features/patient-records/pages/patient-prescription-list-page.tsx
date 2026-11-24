import { useMemo, useState } from 'react';
import {
  AlertCircle,
  Building2,
  Calendar,
  ChevronLeft,
  ChevronRight,
  FileText,
  Home,
  Pill,
  RefreshCw,
  Search,
  Stethoscope,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { PatientRecordsSubnav } from '../components/patient-records-subnav';
import { usePatientPrescriptions } from '../hooks/use-patient-prescriptions';
import { formatEmrDateTimeVn, getPrescriptionStatusMeta } from '../utils/emr-format';

const PAGE_SIZE = 20;

export function PatientPrescriptionListPage() {
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<string>('ALL');
  const [keyword, setKeyword] = useState<string>('');

  const query = usePatientPrescriptions({ page, pageSize: PAGE_SIZE });
  const data = query.data;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  const filteredItems = useMemo(() => {
    const raw = data?.items ?? [];
    return raw.filter((item) => {
      if (statusFilter !== 'ALL' && item.status.toUpperCase() !== statusFilter) {
        return false;
      }
      const term = keyword.trim().toLowerCase();
      if (!term) return true;
      return (
        (item.prescriptionCode ?? '').toLowerCase().includes(term) ||
        (item.visitCode ?? '').toLowerCase().includes(term) ||
        (item.prescribingDoctorName ?? '').toLowerCase().includes(term) ||
        (item.hospitalName ?? '').toLowerCase().includes(term)
      );
    });
  }, [data?.items, keyword, statusFilter]);

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
            <span className="font-semibold text-ink">Đơn thuốc của tôi</span>
          </nav>

          <PatientRecordsSubnav />

          <header className="mb-6">
            <h1 className="text-2xl font-bold text-navy">Đơn thuốc ngoại trú của tôi</h1>
            <p className="mt-1 text-xs leading-relaxed text-sub">
              Theo dõi danh sách đơn thuốc đã được bác sĩ kê chính thức, trạng thái chuẩn bị/cấp
              phát tại nhà thuốc và hướng dẫn sử dụng chi tiết cho từng loại thuốc.
            </p>
          </header>

          <div className="mb-6 grid grid-cols-1 gap-4 rounded-2xl border border-border bg-white p-4 shadow-xs sm:grid-cols-3">
            <div className="sm:col-span-2">
              <label htmlFor="rx-search" className="mb-1.5 block text-xs font-semibold text-navy">
                Tìm kiếm đơn thuốc
              </label>
              <div className="relative">
                <Search
                  className="pointer-events-none absolute top-1/2 left-3 h-3.5 w-3.5 -translate-y-1/2 text-sub"
                  aria-hidden="true"
                />
                <input
                  id="rx-search"
                  type="text"
                  value={keyword}
                  onChange={(e) => setKeyword(e.target.value)}
                  placeholder="Tìm theo mã đơn thuốc, mã lượt khám, bác sĩ hoặc cơ sở..."
                  className="w-full rounded-xl border border-border bg-canvas py-2 pr-3 pl-8 text-xs text-ink placeholder:text-slate-400 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
                />
              </div>
            </div>

            <div>
              <label htmlFor="rx-status" className="mb-1.5 block text-xs font-semibold text-navy">
                Trạng thái cấp phát
              </label>
              <select
                id="rx-status"
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
              >
                <option value="ALL">Tất cả trạng thái</option>
                <option value="ISSUED">Đã kê đơn</option>
                <option value="PREPARING">Đang chuẩn bị thuốc</option>
                <option value="READY">Sẵn sàng nhận thuốc</option>
                <option value="DISPENSED">Đã cấp phát</option>
              </select>
            </div>
          </div>

          {query.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs"
            >
              <div className="mx-auto h-9 w-9 animate-spin rounded-full border-4 border-slate-200 border-t-primary" />
              <p className="mt-3 text-sm font-semibold text-ink">Đang tải danh sách đơn thuốc...</p>
            </div>
          )}

          {!query.isPending && query.isError && (
            <div
              role="alert"
              className="rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-xs"
            >
              <AlertCircle className="mx-auto h-8 w-8 text-danger" aria-hidden="true" />
              <h2 className="mt-3 text-base font-bold text-ink">
                Không thể tải danh sách đơn thuốc
              </h2>
              <p className="mx-auto mt-1 max-w-md text-xs text-sub">
                {getErrorMessage(
                  query.error,
                  'Máy chủ đang bận hoặc không thể tải dữ liệu đơn thuốc. Vui lòng thử lại.',
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

          {!query.isPending && !query.isError && filteredItems.length === 0 && (
            <div className="rounded-2xl border border-border bg-white p-10 text-center shadow-xs">
              <Pill className="mx-auto h-10 w-10 text-slate-300" aria-hidden="true" />
              <h2 className="mt-3 text-base font-bold text-navy">Chưa có đơn thuốc nào</h2>
              <p className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-sub">
                Các đơn thuốc ngoại trú chính thức sau lượt khám sẽ hiển thị tại đây kèm liều lượng
                và hướng dẫn sử dụng chi tiết.
              </p>
            </div>
          )}

          {!query.isPending && !query.isError && filteredItems.length > 0 && (
            <div className="space-y-4">
              {filteredItems.map((item) => {
                const statusMeta = getPrescriptionStatusMeta(item.status);
                const codeOrId = item.prescriptionCode ?? String(item.id);
                return (
                  <article
                    key={item.id}
                    className="rounded-2xl border border-border bg-white p-5 shadow-xs transition-colors hover:border-sky-200"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-mono text-sm font-bold text-primary">
                            {codeOrId}
                          </span>
                          <span
                            className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${statusMeta.className}`}
                          >
                            {statusMeta.label}
                          </span>
                        </div>
                        <p className="text-xs text-sub">
                          Số khoản thuốc:{' '}
                          <strong className="font-semibold text-ink">{item.itemCount}</strong> thuốc
                        </p>
                      </div>

                      <div className="flex items-center gap-1.5 rounded-xl bg-canvas px-3 py-1.5 text-xs font-medium text-navy">
                        <Calendar className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                        <span>{formatEmrDateTimeVn(item.issuedAt)}</span>
                      </div>
                    </div>

                    <div className="mt-3 grid grid-cols-1 gap-2 text-xs text-sub sm:grid-cols-3">
                      <div className="flex items-center gap-1.5">
                        <Stethoscope
                          className="h-3.5 w-3.5 shrink-0 text-slate-400"
                          aria-hidden="true"
                        />
                        <span>BS kê đơn: {item.prescribingDoctorName ?? 'Bác sĩ điều trị'}</span>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <Building2
                          className="h-3.5 w-3.5 shrink-0 text-slate-400"
                          aria-hidden="true"
                        />
                        <span>{item.hospitalName ?? 'Cơ sở MedFlow'}</span>
                      </div>
                      {item.visitCode && (
                        <div className="flex items-center gap-1.5">
                          <FileText
                            className="h-3.5 w-3.5 shrink-0 text-slate-400"
                            aria-hidden="true"
                          />
                          <span>
                            Lượt khám:{' '}
                            <span className="font-mono font-semibold text-ink">
                              {item.visitCode}
                            </span>
                          </span>
                        </div>
                      )}
                    </div>

                    <div className="mt-4 flex flex-wrap items-center justify-end gap-2.5 border-t border-border pt-3.5">
                      {item.visitCode && (
                        <Link
                          to={`/emr/${encodeURIComponent(item.visitCode)}`}
                          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-navy transition-colors hover:bg-canvas"
                        >
                          <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Xem bệnh án lượt khám</span>
                        </Link>
                      )}
                      <Link
                        to={`/emr/prescriptions/${encodeURIComponent(codeOrId)}`}
                        className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
                      >
                        <span>Xem hướng dẫn dùng thuốc</span>
                        <ChevronRight className="h-4 w-4" aria-hidden="true" />
                      </Link>
                    </div>
                  </article>
                );
              })}

              {totalPages > 1 && (
                <div className="flex items-center justify-between rounded-2xl border border-border bg-white px-4 py-3 text-xs">
                  <span className="text-sub">
                    Trang <strong className="text-ink">{page}</strong> / {totalPages}
                  </span>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      disabled={page <= 1}
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      className="inline-flex items-center gap-1 rounded-xl border border-border px-3 py-1.5 font-semibold text-navy hover:bg-canvas disabled:opacity-60"
                    >
                      <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Trước</span>
                    </button>
                    <button
                      type="button"
                      disabled={page >= totalPages}
                      onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
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
      </main>

      <PatientFooter />
    </div>
  );
}
