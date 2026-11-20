import { Card } from '@/shared/ui/card';
import { Spinner } from '@/shared/ui/spinner';

export function PaymentPageSkeleton() {
  return (
    <Card className="flex flex-col items-center justify-center gap-3 p-12 text-sm text-sub">
      <Spinner className="h-6 w-6" />
      <p>Đang tải thông tin viện phí…</p>
    </Card>
  );
}
