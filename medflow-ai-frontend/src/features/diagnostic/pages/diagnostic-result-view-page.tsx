import {
  ArrowLeft,
  CheckCircle2,
  ClipboardCheck,
  ExternalLink,
  FileDown,
  FileText,
  History,
  Printer,
  RefreshCw,
  ShieldAlert,
} from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useLogout } from '@/features/auth/hooks/use-auth';
import { useAuthStore } from '@/features/auth/store/auth.store';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import { DiagnosticSidebar } from '../components/diagnostic-sidebar';
import {
  useDiagnosticResultViewDetail,
  useDiagnosticResultViewsList,
} from '../hooks/use-diagnostic-result-view';

function computeInitials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return 'BN';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

function formatDateTime(value: string | null): string {
  if (!value) return 'Chưa cập nhật';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN');
}

function formatVnd(amount: number | null | undefined): string {
  if (amount == null || Number.isNaN(amount)) return 'Chưa cập nhật';
  return `${new Intl.NumberFormat('vi-VN').format(amount)} đ`;
}

function formatGender(gender: string | null): string {
  if (!gender) return 'Chưa rõ';
  const upper = gender.trim().toUpperCase();
  if (upper === 'MALE' || upper === 'NAM') return 'Nam';
  if (upper === 'FEMALE' || upper === 'NỮ' || upper === 'NU') return 'Nữ';
  return gender;
}

export function DiagnosticResultViewPage() {
  const params = useParams<{ orderIdOrCode?: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useLogout();

  const activeOrderCode =
    params.orderIdOrCode?.trim() || searchParams.get('orderCode')?.trim() || '';

  const detailQuery = useDiagnosticResultViewDetail(activeOrderCode);
  const listQuery = useDiagnosticResultViewsList(!activeOrderCode);
  const [notice, setNotice] = useState<string | null>(null);

  const response = detailQuery.data;
  const item = response?.item;
  const clinical = response?.clinicalContext;

  const handleOpenDocument = () => {
    if (item?.document.hasDocument && item.document.reportFileUrl) {
      window.open(item.document.reportFileUrl, '_blank', 'noopener,noreferrer');
      return;
    }
    setNotice('Chỉ định này chưa có tệp tài liệu / hình ảnh đính kèm trên hệ thống.');
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <DiagnosticSidebar
        activeItem="results"
        staffName={user?.fullName ?? item?.finalizedByStaffName ?? 'Nhân viên Cận lâm sàng'}
        departmentName={item?.roomName ?? item?.departmentName ?? 'Khoa Cận lâm sàng'}
        onItemSelect={(navItem) => {
          if (navItem === 'dashboard') navigate('/diagnostic');
          if (navItem === 'orders') navigate('/diagnostic/orders');
          if (navItem === 'queue') navigate('/diagnostic/queue');
          if (navItem === 'execution') navigate('/diagnostic/execution');
          if (navItem === 'results') navigate('/diagnostic/results');
        }}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center justify-between gap-4 border-b border-border bg-white px-6">
          <div className="flex min-w-0 items-center gap-3">
            <button
              type="button"
              onClick={() => navigate('/diagnostic/results')}
              aria-label="Quay lại danh sách kết quả cận lâm sàng"
              className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border bg-white text-navy transition-colors hover:bg-canvas"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            </button>
            <div className="min-w-0">
              <h1 className="truncate text-sm font-bold text-navy">
                Chi tiết Phiếu Kết quả Cận lâm sàng (SCR-DIA-08)
              </h1>
              <p className="truncate text-[11px] text-sub">
                {activeOrderCode ? (
                  <>
                    Diagnostic Management • Mã chỉ định:{' '}
                    <span className="font-mono font-bold text-primary">{activeOrderCode}</span>
                  </>
                ) : (
                  'Danh sách kết quả cận lâm sàng đã lưu'
                )}
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
          {!activeOrderCode && (
            <Card>
              <CardHeader className="py-4">
                <h2 className="text-sm font-bold text-navy">
                  Danh sách Phiếu Kết quả Cận lâm sàng Đã Ghi nhận
                </h2>
              </CardHeader>
              <CardContent>
                {listQuery.isPending && (
                  <p role="status" className="text-xs text-sub">
                    Đang tải danh sách phiếu kết quả cận lâm sàng...
                  </p>
                )}
                {!listQuery.isPending && listQuery.isError && (
                  <Alert tone="error">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <span>
                        {listQuery.error instanceof Error
                          ? listQuery.error.message
                          : 'Không thể tải danh sách kết quả.'}
                      </span>
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={() => void listQuery.refetch()}
                      >
                        Thử lại
                      </Button>
                    </div>
                  </Alert>
                )}
                {!listQuery.isPending &&
                  !listQuery.isError &&
                  (listQuery.data?.items.length ?? 0) === 0 && (
                    <p className="text-xs text-sub">
                      Chưa có phiếu kết quả cận lâm sàng nào trong khoa của bạn.
                    </p>
                  )}
                {!listQuery.isPending && (listQuery.data?.items.length ?? 0) > 0 && (
                  <div className="divide-y divide-slate-100 text-xs">
                    {listQuery.data?.items.map((row) => (
                      <div
                        key={row.orderId}
                        className="flex flex-wrap items-center justify-between gap-3 py-3"
                      >
                        <div>
                          <p className="font-bold text-navy">
                            {row.serviceName}{' '}
                            <span className="font-mono text-primary">({row.orderCode})</span>
                          </p>
                          <p className="text-sub">
                            {row.patient.fullName} ({row.patient.patientCode}) • Lượt khám{' '}
                            {row.visitCode}
                          </p>
                        </div>
                        <Link
                          to={`/diagnostic/results/${encodeURIComponent(row.orderCode)}/view`}
                          className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 font-semibold text-white hover:bg-primary-dark"
                        >
                          <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                          <span>Xem phiếu</span>
                        </Link>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          )}

          {activeOrderCode && detailQuery.isPending && (
            <div
              role="status"
              className="rounded-2xl border border-border bg-white p-8 text-center text-xs text-sub"
            >
              Đang tải chi tiết phiếu kết quả cận lâm sàng...
            </div>
          )}

          {activeOrderCode && !detailQuery.isPending && detailQuery.isError && (
            <div role="alert">
              <Alert tone="error">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span>
                    {detailQuery.error instanceof Error
                      ? detailQuery.error.message
                      : 'Không tìm thấy phiếu kết quả cận lâm sàng.'}
                  </span>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => void detailQuery.refetch()}
                    className="gap-1.5"
                  >
                    <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>Thử lại</span>
                  </Button>
                </div>
              </Alert>
            </div>
          )}

          {notice && (
            <div role="status" aria-live="polite">
              <Alert tone="info">{notice}</Alert>
            </div>
          )}

          {item && (
            <>
              <section
                aria-label="Thông tin bệnh nhân và chỉ định CLS"
                className="rounded-2xl border border-border bg-white p-5 shadow-2xs space-y-3"
              >
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="flex items-center gap-3.5">
                    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-sky-200 bg-sky-100 text-sm font-bold text-sky-800">
                      {computeInitials(item.patient.fullName)}
                    </div>
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="text-base font-bold text-navy">{item.patient.fullName}</p>
                        <span className="text-xs text-sub">
                          • {formatGender(item.patient.gender)}
                          {item.patient.age != null ? `, ${item.patient.age} tuổi` : ''}
                        </span>
                        <Badge tone={item.isResultFinalized ? 'emerald' : 'amber'}>
                          <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" />
                          {item.isResultFinalized
                            ? 'Đã khóa kết quả (Finalized)'
                            : item.resultStatus}
                        </Badge>
                      </div>
                      <p className="mt-0.5 font-mono text-xs text-sub">
                        Mã BN: <strong className="text-navy">{item.patient.patientCode}</strong> •
                        Lượt khám: <strong className="text-navy">{item.visitCode}</strong> • Mã chỉ
                        định: <strong className="text-primary">{item.orderCode}</strong>
                      </p>
                    </div>
                  </div>

                  <div className="text-right text-xs">
                    <p className="font-bold text-navy">{item.serviceName}</p>
                    <p className="text-sub">
                      BS chỉ định: {item.orderingDoctorName} • Phòng:{' '}
                      {item.roomName || item.departmentName}
                    </p>
                    <p className="mt-0.5 font-mono text-[11px] text-sub">
                      Hoàn tất kỹ thuật: {formatDateTime(item.completedAt)} •{' '}
                      <strong className="text-emerald-700">
                        Đơn giá: {formatVnd(item.unitPrice)}
                      </strong>
                    </p>
                  </div>
                </div>

                {item.patient.allergies && (
                  <div
                    role="note"
                    className="flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-2 text-xs font-bold text-rose-700"
                  >
                    <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>Cảnh báo dị ứng: {item.patient.allergies}</span>
                  </div>
                )}
              </section>

              <div className="grid grid-cols-1 gap-6 xl:grid-cols-12">
                <div className="space-y-4 xl:col-span-5">
                  <Card>
                    <CardHeader className="py-3.5">
                      <h2 className="text-sm font-bold text-navy">
                        Bối cảnh Lâm sàng &amp; Tài liệu Đính kèm
                      </h2>
                    </CardHeader>
                    <CardContent className="space-y-3 text-xs">
                      <div className="rounded-xl border border-border bg-canvas p-3.5 space-y-1.5">
                        <p>
                          <strong className="text-navy">Lý do khám: </strong>
                          <span>{clinical?.reasonForVisit || 'Chưa ghi nhận'}</span>
                        </p>
                        <p>
                          <strong className="text-navy">Triệu chứng / Khám lâm sàng: </strong>
                          <span>
                            {clinical?.clinicalFindings ||
                              clinical?.presentingComplaint ||
                              'Chưa ghi nhận'}
                          </span>
                        </p>
                        {(clinical?.primaryDiagnosisCode || clinical?.primaryDiagnosisName) && (
                          <p>
                            <strong className="text-navy">Chẩn đoán sơ bộ: </strong>
                            <span>
                              {[clinical?.primaryDiagnosisCode, clinical?.primaryDiagnosisName]
                                .filter(Boolean)
                                .join(' - ')}
                            </span>
                          </p>
                        )}
                      </div>

                      <div className="rounded-xl border border-border bg-white p-3.5 space-y-2">
                        <p className="font-bold text-navy">Tệp báo cáo / Hình ảnh lưu trữ</p>
                        {item.document.hasDocument && item.document.reportFileUrl ? (
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="truncate font-mono text-ink">
                              {item.document.fileName || item.document.reportFileUrl}
                            </span>
                            <Button
                              type="button"
                              variant="secondary"
                              size="sm"
                              onClick={handleOpenDocument}
                              className="gap-1.5"
                            >
                              <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                              <span>Mở tài liệu</span>
                            </Button>
                          </div>
                        ) : (
                          <p className="text-sub">
                            Chưa có tệp đính kèm / hình ảnh lưu trữ cho chỉ định này.
                          </p>
                        )}
                      </div>
                    </CardContent>
                  </Card>

                  {(response?.versions.length ?? 0) > 0 && (
                    <Card>
                      <CardHeader className="flex items-center gap-2 py-3.5">
                        <History className="h-4 w-4 text-primary" aria-hidden="true" />
                        <h2 className="text-sm font-bold text-navy">
                          Lịch sử Phiên bản Kết quả ({response?.versions.length})
                        </h2>
                      </CardHeader>
                      <CardContent className="space-y-2 text-xs">
                        {response?.versions.map((ver) => (
                          <div
                            key={ver.versionId}
                            className="rounded-lg border border-border bg-canvas p-3 space-y-1"
                          >
                            <div className="flex items-center justify-between">
                              <strong className="font-mono text-navy">
                                Phiên bản v{ver.versionNo}
                              </strong>
                              <span className="text-[11px] text-sub">
                                {formatDateTime(ver.enteredAt)}
                              </span>
                            </div>
                            {ver.enteredByStaffName && (
                              <p className="text-sub">Người nhập: {ver.enteredByStaffName}</p>
                            )}
                            {ver.correctionReason && (
                              <p className="text-amber-800">
                                Lý do hiệu đính: {ver.correctionReason}
                              </p>
                            )}
                          </div>
                        ))}
                      </CardContent>
                    </Card>
                  )}
                </div>

                <div className="xl:col-span-7">
                  <Card>
                    <CardHeader className="flex flex-wrap items-center justify-between gap-2 py-3.5">
                      <div className="flex items-center gap-2">
                        <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
                        <h2 className="text-sm font-bold text-navy">
                          Mô tả Kết quả &amp; Kết luận Chuyên khoa
                        </h2>
                      </div>
                      <span className="text-[11px] font-medium text-sub">
                        {item.resultFinalizedAt
                          ? `Khóa kết quả lúc ${formatDateTime(item.resultFinalizedAt)}`
                          : 'Bản nháp / Chờ khóa'}
                      </span>
                    </CardHeader>
                    <CardContent className="space-y-4 text-xs">
                      <section
                        aria-label="Mô tả kết quả kỹ thuật"
                        className="rounded-xl border border-border bg-white p-4 space-y-2"
                      >
                        <h3 className="font-bold text-navy">Mô tả chi tiết kết quả thực hiện</h3>
                        <p className="whitespace-pre-line leading-relaxed text-ink">
                          {item.resultText || 'Chưa cập nhật mô tả chi tiết.'}
                        </p>
                      </section>

                      <section
                        aria-label="Kết luận cận lâm sàng"
                        className="rounded-xl border border-sky-200 bg-sky-50/70 p-4 space-y-2"
                      >
                        <div className="flex items-center gap-2 font-bold uppercase tracking-wide text-navy">
                          <ClipboardCheck className="h-4 w-4 text-primary" aria-hidden="true" />
                          <span>Kết luận Cận lâm sàng</span>
                        </div>
                        <div className="rounded-lg border border-sky-200 bg-white p-3 text-sm font-bold text-navy">
                          {item.conclusion || 'Chưa cập nhật kết luận.'}
                        </div>
                        <p className="text-[11px] text-sub">
                          Người thực hiện / duyệt:{' '}
                          <strong className="text-navy">
                            {item.finalizedByStaffName ||
                              item.verifiedByStaffName ||
                              item.enteredByStaffName ||
                              'Chưa cập nhật'}
                          </strong>
                        </p>
                      </section>
                    </CardContent>
                  </Card>
                </div>
              </div>
            </>
          )}
        </main>

        {item && (
          <footer className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-white px-6 py-3 print:hidden">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => navigate('/diagnostic/results')}
              className="gap-1.5"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              <span>Quay lại</span>
            </Button>

            <div className="flex flex-wrap items-center gap-2.5">
              {item.document.hasDocument && item.document.reportFileUrl && (
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={handleOpenDocument}
                  className="gap-1.5"
                >
                  <FileDown className="h-4 w-4" aria-hidden="true" />
                  <span>Tải tài liệu đính kèm</span>
                </Button>
              )}

              <Button
                type="button"
                variant="primary"
                size="sm"
                onClick={() => window.print()}
                className="gap-1.5 shadow-xs"
              >
                <Printer className="h-4 w-4" aria-hidden="true" />
                <span>In phiếu kết quả</span>
              </Button>
            </div>
          </footer>
        )}
      </div>
    </div>
  );
}
