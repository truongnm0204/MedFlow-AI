import type {
  Icd10CatalogItemDto,
  SearchIcd10CodesParams,
} from '../types/consultation-workspace.types';

export const ICD10_CATALOG_MOCK: Icd10CatalogItemDto[] = [
  {
    id: 1,
    code: 'K29.5',
    nameVi: 'Viêm dạ dày mạn tính thể teo',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
  {
    id: 2,
    code: 'K21.0',
    nameVi: 'Trào ngược dạ dày - thực quản có viêm thực quản',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
  {
    id: 3,
    code: 'K76.0',
    nameVi: 'Gan nhiễm mỡ',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
  {
    id: 4,
    code: 'I10',
    nameVi: 'Tăng huyết áp vô căn (nguyên phát)',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
  {
    id: 5,
    code: 'E11',
    nameVi: 'Đái tháo đường type 2',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
  {
    id: 6,
    code: 'J02.9',
    nameVi: 'Viêm họng cấp, không đặc hiệu',
    catalogVersion: 'DEMO-ICD10',
    nodeType: 'CODE',
    isSelectable: true,
    notPrimaryDx: false,
  },
];

/** Diacritics-insensitive ICD-10 search key (NFD, strip marks, đ/Đ → d). */
export function normalizeIcd10SearchText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'd')
    .toLowerCase();
}

export function searchMockIcd10Codes(params: SearchIcd10CodesParams): Icd10CatalogItemDto[] {
  const needle = normalizeIcd10SearchText((params.q ?? '').trim());
  const limit = params.limit ?? 50;
  return ICD10_CATALOG_MOCK.filter((item) => {
    if (params.primaryOnly && item.notPrimaryDx) return false;
    if (!needle) return true;
    return normalizeIcd10SearchText(`${item.code} ${item.nameVi}`).includes(needle);
  }).slice(0, limit);
}
