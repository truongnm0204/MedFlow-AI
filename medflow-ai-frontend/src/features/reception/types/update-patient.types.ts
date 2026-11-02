export type PatientGender = 'MALE' | 'FEMALE' | 'OTHER';

/** Các trường lễ tân được phép chỉnh sửa (không gồm mã bệnh nhân). */
export interface PatientEditableFields {
  fullName: string;
  dateOfBirth: string; // yyyy-mm-dd
  gender: PatientGender;
  ethnicity: string;
  nationalId: string; // CCCD/VNeID 12 số
  phone: string;
  email: string;
  address: string;
  insuranceNumber: string;
  insuranceFacility: string;
  emergencyContactName: string;
  emergencyContactRelationship: string;
  emergencyContactPhone: string;
}

/** Dữ liệu hồ sơ bệnh nhân dùng làm giá trị khởi tạo cho form cập nhật. */
export interface UpdatePatientFormData extends PatientEditableFields {
  patientCode: string; // chỉ đọc
}
