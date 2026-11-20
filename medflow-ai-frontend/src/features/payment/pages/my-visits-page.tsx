import { ChevronRight, ClipboardList, Home, RefreshCw } from 'lucide-react';
import { Link } from 'react-router-dom';

import { AppHeader } from '@/shared/components/layout/app-header';
import { PatientFooter } from '@/shared/components/layout/patient-footer';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Card } from '@/shared/ui/card';

import { MyVisitsSkeleton } from '../components/my-visits-skeleton';
import { VisitCard } from '../components/visit-card';
import { useMyVisitsQuery } from '../hooks/use-my-visits';

export function MyVisitsPage() {
  const { data, isPending, isError, error, isFetching, refetch } = useMyVisitsQuery();
  const items = data?.items ?? [];

  return (
    <div className="flex min-h-screen flex-col bg-canvas font-sans text-ink antialiased">
      <AppHeader />

      <main className="flex-1 pb-16 pt-5">
        <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
          <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-xs text-sub">
            <Link to="/" className="flex items-center gap-1.5 transition-colors hover:text-primary">
              <Home className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Trang chủ</span>
            </Link>
            <ChevronRight className="h-3.5 w-3.5 text-sub/60" aria-hidden="true" />
            <span className="font-semibold text-primary">Lượt khám của tôi</span>
          </nav>

          <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h1 className="text-2xl font-extrabold text-ink sm:text-3xl">Lượt khám của tôi</h1>
              <p className="mt-1 text-xs text-sub">
                Tối đa 20 lượt khám gần nhất của bạn và người thân đã được xác thực.
              </p>
            </div>
            <Button
              variant="secondary"
              size="md"
              type="button"
              loading={isFetching}
              onClick={() => void refetch()}
            >
              <RefreshCw className="h-4 w-4" aria-hidden="true" />
              <span>Làm mới</span>
            </Button>
          </div>

          {isPending && !data ? (
            <MyVisitsSkeleton />
          ) : isError && !data ? (
            <div className="space-y-4">
              <Alert tone="error">{getErrorMessage(error)}</Alert>
              <Button variant="primary" size="lg" type="button" onClick={() => void refetch()}>
                <RefreshCw className="h-4 w-4" aria-hidden="true" />
                <span>Thử lại</span>
              </Button>
            </div>
          ) : items.length === 0 ? (
            <Card className="flex flex-col items-center gap-3 p-12 text-center">
              <ClipboardList className="h-8 w-8 text-sub" aria-hidden="true" />
              <p className="text-sm font-semibold text-ink">Chưa có lượt khám nào</p>
              <p className="max-w-md text-xs leading-relaxed text-sub">
                Lượt khám xuất hiện sau khi bạn check-in tại quầy hoặc được tiếp nhận khám vãng lai.
              </p>
              <Link
                to="/appointments"
                className="mt-1 text-xs font-semibold text-primary transition-colors hover:text-primary-dark"
              >
                Xem lịch hẹn của tôi
              </Link>
            </Card>
          ) : (
            <div className="space-y-3">
              {items.map((visit) => (
                <VisitCard key={visit.visitId} visit={visit} />
              ))}
            </div>
          )}
        </div>
      </main>

      <PatientFooter />
    </div>
  );
}
