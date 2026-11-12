import type {
  DiagnosisConclusionFormValues,
  DiagnosisSaveMode,
  Icd10Option,
} from '../types/diagnosis-conclusion.types';
import type {
  ConsultationDiagnosisStatus,
  ConsultationWorkspaceResponseDto,
  RecordDiagnosisRequest,
} from '../types/consultation-workspace.types';

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function toDiagnosisFormValues(
  workspace: ConsultationWorkspaceResponseDto,
): DiagnosisConclusionFormValues {
  const diagnoses = workspace.clinicalContext.diagnoses ?? [];
  return {
    primaryIcd10Code: diagnoses.find((item) => item.isPrimary)?.code ?? '',
    secondaryIcd10Codes: diagnoses.filter((item) => !item.isPrimary).map((item) => item.code),
    clinicalConclusion: workspace.clinicalContext.clinicalConclusion ?? '',
    differentialNotes: '',
    lifestyleInstructions: '',
    warningSigns: '',
  };
}

export function toDiagnosisOptionMap(
  workspace: ConsultationWorkspaceResponseDto,
): Record<string, Icd10Option> {
  const options: Record<string, Icd10Option> = {};
  for (const diagnosis of workspace.clinicalContext.diagnoses ?? []) {
    options[diagnosis.code] = { code: diagnosis.code, nameVi: diagnosis.name };
  }
  return options;
}

export function toRecordDiagnosisRequest(
  values: DiagnosisConclusionFormValues,
  workspace: ConsultationWorkspaceResponseDto,
  mode: DiagnosisSaveMode,
): RecordDiagnosisRequest {
  const status: ConsultationDiagnosisStatus = mode === 'confirm' ? 'CONFIRMED' : 'PRELIMINARY';
  const primaryCode = values.primaryIcd10Code.trim().toUpperCase();
  return {
    diagnoses: [
      { code: primaryCode, isPrimary: true, status },
      ...values.secondaryIcd10Codes.map((code) => ({
        code: code.trim().toUpperCase(),
        isPrimary: false,
        status,
      })),
    ],
    clinicalConclusion: blankToNull(values.clinicalConclusion),
    differentialNotes: blankToNull(values.differentialNotes),
    lifestyleInstructions: blankToNull(values.lifestyleInstructions),
    warningSigns: blankToNull(values.warningSigns),
    diagnosisStatus: status,
    isConfirmed: mode === 'confirm',
    expectedStateVersion:
      workspace.queue && workspace.queue.stateVersion >= 1 ? workspace.queue.stateVersion : null,
    expectedRevisionNo:
      (workspace.clinicalContext.currentRevisionNo ?? 0) >= 1
        ? workspace.clinicalContext.currentRevisionNo
        : null,
  };
}
