/** Stage E2 — minimal operational journey slip (QE-21 / E2-R09). Read-only; no payment or PHI detail. */

export interface JourneySlipTicketDto {
  queueEntryId: string | null;
  queueType: string;
  ticketNo: string;
  servicePointName: string | null;
  status: string;
}

export interface JourneySlipDto {
  visitId: number;
  visitCode: string;
  presenceState: string;
  currentAssignedDoctorDisplay: string | null;
  currentExaminationRoomDisplay: string | null;
  tickets: JourneySlipTicketDto[];
  servingDate: string;
  generatedAtUtc: string;
  /** Opaque lookup token `MEDFLOW-VISIT:{visitCode}` — scanning resolves the visit, not a bearer credential. */
  internalLookupQr: string;
}

export interface JourneySlipPrintResultDto {
  visitId: number;
  printedAtUtc: string;
  printAuditRecorded: boolean;
  note: string;
}
