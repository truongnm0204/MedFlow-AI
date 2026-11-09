import { CheckCircle2, Lock, Sparkles } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { ConsultationWorkspaceActionDto } from '../types/consultation-workspace.types';

export interface ConsultationAllowedActionsCardProps {
  actions: ConsultationWorkspaceActionDto[];
  selectedActionNotice: string | null;
  isOpening: boolean;
  onTriggerAction: (action: ConsultationWorkspaceActionDto) => void;
}

export function ConsultationAllowedActionsCard({
  actions,
  selectedActionNotice,
  isOpening,
  onTriggerAction,
}: ConsultationAllowedActionsCardProps) {
  return (
    <Card>
      <CardHeader className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-primary" aria-hidden="true" />
          <h3 className="text-sm font-bold text-navy">Thao tác lâm sàng được phép (UC31–UC38)</h3>
        </div>
        <Badge tone="sky">
          {actions.filter((a) => a.enabled).length}/{actions.length} khả dụng
        </Badge>
      </CardHeader>

      <CardContent className="space-y-2.5">
        {selectedActionNotice && (
          <div
            role="status"
            className="rounded-xl border border-sky-200 bg-sky-50 px-3 py-2 text-xs text-sky-900"
          >
            {selectedActionNotice}
          </div>
        )}

        <div className="space-y-2">
          {actions.map((action) => {
            const isActionDisabled =
              !action.enabled || (action.id === 'open-consultation' && isOpening);

            return (
              <div
                key={action.id}
                className={`rounded-xl border p-3 transition-colors ${
                  action.enabled
                    ? 'border-border bg-white hover:border-primary/40'
                    : 'border-border bg-canvas/70 opacity-75'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-[10px] font-bold uppercase rounded bg-canvas border border-border px-1.5 py-0.5 text-navy">
                        {action.ucCode}
                      </span>
                      <span className="text-xs font-bold text-navy truncate">{action.label}</span>
                    </div>
                    {action.disabledReason && (
                      <div className="mt-1 text-[11px] text-sub flex items-center gap-1">
                        <Lock className="h-3 w-3 shrink-0 text-amber-600" aria-hidden="true" />
                        <span>{action.disabledReason}</span>
                      </div>
                    )}
                  </div>

                  <button
                    type="button"
                    disabled={isActionDisabled}
                    onClick={() => onTriggerAction(action)}
                    className={`shrink-0 inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
                      action.enabled
                        ? 'bg-primary text-white hover:bg-primary-dark'
                        : 'bg-canvas text-sub border border-border'
                    }`}
                  >
                    {action.enabled && <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />}
                    <span>{action.enabled ? 'Thực hiện' : 'Đang khóa'}</span>
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      </CardContent>
    </Card>
  );
}
