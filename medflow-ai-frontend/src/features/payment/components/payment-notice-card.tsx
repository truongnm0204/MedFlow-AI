import { Info } from 'lucide-react';
import type { ReactNode } from 'react';

import { Card } from '@/shared/ui/card';

interface PaymentNoticeCardProps {
  title: string;
  message: string;
  tone?: 'info' | 'warning';
  children?: ReactNode;
}

export function PaymentNoticeCard({
  title,
  message,
  tone = 'info',
  children,
}: PaymentNoticeCardProps) {
  const boxClass =
    tone === 'warning'
      ? 'border-amber-200 bg-amber-50 text-amber-900'
      : 'border-sky-200 bg-sky-50 text-sky-900';

  return (
    <Card className="p-6">
      <div className={`flex items-start gap-3 rounded-xl border p-4 ${boxClass}`}>
        <Info className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
        <div>
          <h2 className="text-sm font-bold">{title}</h2>
          <p className="mt-1 text-sm leading-relaxed">{message}</p>
        </div>
      </div>
      {children ? <div className="mt-4">{children}</div> : null}
    </Card>
  );
}
