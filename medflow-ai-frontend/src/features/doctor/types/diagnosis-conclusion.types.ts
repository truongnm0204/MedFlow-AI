import type { Icd10CatalogItemDto } from './consultation-workspace.types';

export type Icd10Option = Pick<Icd10CatalogItemDto, 'code' | 'nameVi'>;

export interface DiagnosisConclusionFormValues {
  primaryIcd10Code: string;
  secondaryIcd10Codes: string[];
  differentialNotes: string;
  clinicalConclusion: string;
  lifestyleInstructions: string;
  warningSigns: string;
}

export type DiagnosisConclusionFieldErrors = Partial<
  Record<
    | 'primaryIcd10Code'
    | 'secondaryIcd10Codes'
    | 'differentialNotes'
    | 'clinicalConclusion'
    | 'lifestyleInstructions'
    | 'warningSigns',
    string
  >
>;

export type DiagnosisSaveMode = 'draft' | 'confirm';
