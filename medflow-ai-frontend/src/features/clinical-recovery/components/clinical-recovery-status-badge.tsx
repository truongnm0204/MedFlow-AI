import { Badge } from '@/shared/ui/badge';

import {
  CLINICAL_RECOVERY_STATUS_META,
  type ClinicalRecoveryStatus,
} from '../types/clinical-recovery.types';

/** Lifecycle pill for a recovery request; unknown codes fall back to the raw server value. */
export function ClinicalRecoveryStatusBadge({ status }: { status: string }) {
  const meta = CLINICAL_RECOVERY_STATUS_META[status as ClinicalRecoveryStatus];
  return <Badge tone={meta?.tone ?? 'slate'}>{meta?.label ?? status}</Badge>;
}
