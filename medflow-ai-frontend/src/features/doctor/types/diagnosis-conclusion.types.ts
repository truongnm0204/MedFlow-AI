export interface Icd10Option {
  code: string;
  nameVi: string;
  nameEn: string;
}

export interface DiagnosisPatientSummary {
  fullName: string;
  gender: string;
  ageLabel: string;
  patientCode: string;
  visitCode: string;
  allergies: string;
  chronicDisease: string;
}

export interface DiagnosisEvidenceItem {
  id: string;
  title: string;
  result: string;
  status: 'COMPLETED' | 'PENDING';
}

export interface DiagnosisEvidence {
  symptomsSummary: string;
  tests: DiagnosisEvidenceItem[];
}

export interface DiagnosisFollowUpForm {
  enabled: boolean;
  date: string;
  time: string;
  room: string;
}

export interface DiagnosisConclusionFormValues {
  primaryIcd10Code: string;
  secondaryIcd10Codes: string[];
  differentialNotes: string;
  clinicalConclusion: string;
  lifestyleInstructions: string;
  warningSigns: string;
  followUp: DiagnosisFollowUpForm;
}

export type DiagnosisConclusionFieldErrors = Partial<
  Record<
    | 'primaryIcd10Code'
    | 'secondaryIcd10Codes'
    | 'differentialNotes'
    | 'clinicalConclusion'
    | 'lifestyleInstructions'
    | 'warningSigns'
    | 'followUpDate'
    | 'followUpTime'
    | 'followUpRoom',
    string
  >
>;

export interface DiagnosisConclusionContext {
  patient: DiagnosisPatientSummary;
  evidence: DiagnosisEvidence;
  initialValues: DiagnosisConclusionFormValues;
}
