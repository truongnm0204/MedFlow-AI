export interface PrescriptionDrugLineItem {
  id: string;
  drugCode: string;
  drugName: string;
  activeIngredient: string;
  dosageForm: string;
  unit: string;
  quantity: number;
  unitPrice: number;
  dosageInstruction: string;
  mealTiming: string;
  warningNote?: string;
}

export interface PrescriptionCatalogOption {
  drugCode: string;
  drugName: string;
  activeIngredient: string;
  dosageForm: string;
  unit: string;
  unitPrice: number;
  defaultQuantity: number;
  defaultDosage: string;
  defaultMealTiming: string;
  isAllergyContraindicated?: boolean;
  contraindicationReason?: string;
}

export interface CreatePrescriptionContext {
  patientName: string;
  patientCode: string;
  gender: string;
  ageLabel: string;
  visitCode: string;
  rxCode: string;
  primaryDiagnosis: string;
  allergyAlert: string;
  contraindications: string;
  doctorName: string;
  roomLabel: string;
  defaultItems: PrescriptionDrugLineItem[];
  catalog: PrescriptionCatalogOption[];
  defaultAdvice: string;
}
