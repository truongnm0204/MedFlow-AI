import type { DiagnosisConclusionContext, Icd10Option } from '../types/diagnosis-conclusion.types';

export const ICD10_CATALOG_MOCK: Icd10Option[] = [
  {
    code: 'K29.5',
    nameVi: 'Viêm dạ dày mạn tính thể teo',
    nameEn: 'Chronic atrophic gastritis',
  },
  {
    code: 'K21.0',
    nameVi: 'Trào ngược dạ dày - thực quản có viêm thực quản',
    nameEn: 'Gastro-oesophageal reflux disease with oesophagitis',
  },
  { code: 'K76.0', nameVi: 'Gan nhiễm mỡ', nameEn: 'Fatty (change of) liver' },
  { code: 'I10', nameVi: 'Tăng huyết áp vô căn (nguyên phát)', nameEn: 'Essential hypertension' },
  {
    code: 'E11',
    nameVi: 'Đái tháo đường type 2',
    nameEn: 'Type 2 diabetes mellitus',
  },
  { code: 'J02.9', nameVi: 'Viêm họng cấp, không đặc hiệu', nameEn: 'Acute pharyngitis' },
];

export const DEFAULT_DIAGNOSIS_VISIT_CODE = 'VIS-2026-0415-0108';

export const DIAGNOSIS_CONCLUSION_CONTEXT_MOCK: DiagnosisConclusionContext = {
  patient: {
    fullName: 'NGUYỄN VĂN AN',
    gender: 'Nam',
    ageLabel: '34 tuổi',
    patientCode: 'PAT-2024-0108',
    visitCode: DEFAULT_DIAGNOSIS_VISIT_CODE,
    allergies: 'Penicillin, NSAIDs (Aspirin)',
    chronicDisease: 'Viêm dạ dày mạn tính',
  },
  evidence: {
    symptomsSummary:
      'Đau thượng vị âm ỉ kéo dài 3 tuần, nóng rát sau ăn, ợ hơi, ợ chua về đêm. Không sốt, không nôn ra máu, không đi ngoài phân đen.',
    tests: [
      {
        id: 'test-endoscopy',
        title: 'Nội soi dạ dày',
        result: 'Niêm mạc teo, HP (+) trên test urease nhanh.',
        status: 'COMPLETED',
      },
      {
        id: 'test-ultrasound',
        title: 'Siêu âm ổ bụng',
        result: 'Gan nhiễm mỡ độ I, không sỏi túi mật.',
        status: 'COMPLETED',
      },
      {
        id: 'test-cbc',
        title: 'Huyết học (CBC)',
        result: 'Các chỉ số trong giới hạn bình thường.',
        status: 'COMPLETED',
      },
    ],
  },
  initialValues: {
    primaryIcd10Code: '',
    secondaryIcd10Codes: [],
    differentialNotes: '',
    clinicalConclusion: '',
    lifestyleInstructions: '',
    warningSigns: '',
    followUp: { enabled: false, date: '', time: '', room: 'P.102' },
  },
};
