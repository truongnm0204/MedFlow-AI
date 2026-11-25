import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DepartmentCreatePage } from '@/features/admin-departments/pages/department-create-page';
import { DepartmentEditPage } from '@/features/admin-departments/pages/department-edit-page';
import { DoctorCreatePage } from '@/features/admin-doctors/pages/doctor-create-page';
import { DoctorEditModal } from '@/features/admin-doctors/pages/doctor-edit-modal';
import { RoomCreatePage } from '@/features/admin-rooms/pages/room-create-page';
import { RoomEditPage } from '@/features/admin-rooms/pages/room-edit-page';
import { ExaminationServiceCreatePage } from '@/features/admin-services/pages/examination-service-create-page';
import { ServiceEditModal } from '@/features/admin-services/pages/service-edit-modal';
import { useExaminationServiceStore } from '@/features/admin-services/store/examination-service.store';
import { ExaminationSlotCreatePage } from '@/features/admin-slots/pages/examination-slot-create-page';
import { SlotEditModal } from '@/features/admin-slots/pages/slot-edit-modal';
import { useExaminationSlotStore } from '@/features/admin-slots/store/examination-slot.store';
import { StaffAccountCreatePage } from '@/features/admin-staff/pages/staff-account-create-page';
import { StaffAccountEditPage } from '@/features/admin-staff/pages/staff-account-edit-page';
import { useAuthStore } from '@/features/auth/store/auth.store';
import {
  resetDiagnosticResultEntryMockState,
  resolveMockDiagnosticOrdersForResultEntry,
  resolveMockDiagnosticResultEntryDetail,
} from '@/features/diagnostic/data/diagnostic-result-entry-mock';
import { DiagnosticResultEntryPage } from '@/features/diagnostic/pages/diagnostic-result-entry-page';
import type { GetDiagnosticOrdersForResultEntryParams } from '@/features/diagnostic/types/diagnostic.types';
import { CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK } from '@/features/doctor/data/consultation-workspace-mock';
import { DoctorConsultationWorkspacePage } from '@/features/doctor/pages/doctor-consultation-workspace-page';
import { DoctorCreatePrescriptionPage } from '@/features/doctor/pages/doctor-create-prescription-page';
import { DoctorDiagnosisPage } from '@/features/doctor/pages/doctor-diagnosis-page';
import { ACCESS_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { apiClient } from '@/shared/lib/axios';
import { resetNavigationGuardManagerForTests } from '@/shared/lib/navigation-guard-manager';

function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function loginAsRole(role: 'DOCTOR' | 'DIAGNOSTIC_STAFF' | 'SYSTEM_ADMIN') {
  localStorage.setItem(ACCESS_TOKEN_KEY, `tok-${role}`);
  useAuthStore.setState({
    user: {
      id: 501,
      email: `${role.toLowerCase()}@medflow.vn`,
      fullName: 'Nhân sự Kiểm thử',
      staffCode: 'HOS01-STF-501',
      roles: [role],
      permissions: [
        'MEDICAL_RECORD_WRITE',
        'DIAGNOSTIC_ORDER',
        'PRESCRIPTION_ISSUE',
        'DIAGNOSTIC_RESULT_READ',
        'DIAGNOSTIC_RESULT_WRITE',
      ],
      patientCode: null,
    },
    accessToken: `tok-${role}`,
    refreshToken: `rtok-${role}`,
    isAuthenticated: true,
    sessionEpoch: 1,
  });
}

describe('Issue #825 — 16 Forms Unsaved Change Protection & Edge-Case Matrix', () => {
  beforeEach(() => {
    resetNavigationGuardManagerForTests();
    localStorage.clear();
    sessionStorage.clear();
    resetDiagnosticResultEntryMockState();
    useExaminationServiceStore.setState({ services: [], draft: null });
    useExaminationSlotStore.setState({ slots: [], draft: null });
  });

  afterEach(() => {
    resetNavigationGuardManagerForTests();
    vi.restoreAllMocks();
  });

  // ---------------------------------------------------------------------------
  // 1-3. Clinical Doctor Forms + Background Refetch Preservation
  // ---------------------------------------------------------------------------
  it('[1/16] DoctorConsultationWorkspacePage blocks leaving dirty UC33 clinical form and preserves unsaved edits on background refetch', async () => {
    loginAsRole('DOCTOR');
    let revisionNo = 1;
    vi.spyOn(apiClient, 'get').mockImplementation(async () => ({
      data: {
        ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
        clinicalContext: {
          ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK.clinicalContext,
          currentRevisionNo: revisionNo,
          presentingComplaint: revisionNo === 1 ? 'Đau ngực trái' : 'Triệu chứng mới từ server',
        },
      },
    }));

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212/clinical-examination']}>
          <Routes>
            <Route
              path="/doctor/consultation/:visitIdOrCode/clinical-examination"
              element={<DoctorConsultationWorkspacePage />}
            />
            <Route path="/doctor/assigned-queue" element={<div>Hàng đợi Bác sĩ Mock</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const complaintInput = await screen.findByLabelText('Lý do đến khám / Triệu chứng chính');
    fireEvent.change(complaintInput, {
      target: { value: 'Đau ngực lan lên vai trái đang gõ dở' },
    });

    // Simulate TanStack Query background refetch returning a newer revision
    revisionNo = 2;
    await act(async () => {
      await qc.invalidateQueries();
    });

    // Doctor's unsaved text MUST NOT be overwritten!
    expect((complaintInput as HTMLTextAreaElement).value).toBe(
      'Đau ngực lan lên vai trái đang gõ dở',
    );
    expect(
      await screen.findByText(/Dữ liệu bệnh án vừa có phiên bản mới trên máy chủ/i),
    ).toBeInTheDocument();

    // Navigating away via Back button is blocked
    fireEvent.click(screen.getByRole('button', { name: 'Quay lại hàng đợi được phân công' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));
    expect(screen.queryByTestId('unsaved-changes-modal')).not.toBeInTheDocument();

    // Confirm leave navigates to queue
    fireEvent.click(screen.getByRole('button', { name: 'Quay lại hàng đợi được phân công' }));
    fireEvent.click(screen.getByRole('button', { name: 'Rời trang & hủy thay đổi' }));
    expect(await screen.findByText('Hàng đợi Bác sĩ Mock')).toBeInTheDocument();
  });

  it('[2/16] DoctorDiagnosisPage blocks navigation when dirty, preserves dirty text on background refetch, and unlocks after backend draft save', async () => {
    loginAsRole('DOCTOR');
    let serverRev = 1;
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (String(url).startsWith('/doctor/icd10-codes')) {
        return { data: [{ code: 'I20.9', nameVi: 'Cơn đau thắt ngực' }] };
      }
      return {
        data: {
          ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
          clinicalContext: {
            ...CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK.clinicalContext,
            currentRevisionNo: serverRev,
          },
        },
      };
    });
    vi.spyOn(apiClient, 'post').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    } as AxiosResponse);

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212/diagnosis']}>
          <Routes>
            <Route
              path="/doctor/consultation/:visitIdOrCode/diagnosis"
              element={<DoctorDiagnosisPage />}
            />
            <Route
              path="/doctor/consultation/:visitIdOrCode"
              element={<div>Bàn khám Lâm sàng Mock</div>}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const conclusionField = await screen.findByLabelText(/Kết luận lâm sàng/i);
    fireEvent.change(conclusionField, {
      target: { value: 'Kết luận chẩn đoán đang nhập dở cần bảo vệ' },
    });

    // Background refetch does not wipe conclusionField
    serverRev = 2;
    await act(async () => {
      await qc.invalidateQueries();
    });
    expect((conclusionField as HTMLTextAreaElement).value).toBe(
      'Kết luận chẩn đoán đang nhập dở cần bảo vệ',
    );

    // Back button blocked
    fireEvent.click(screen.getByRole('button', { name: 'Quay lại phiên khám' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));

    // Save draft to backend -> unlocks navigation
    fireEvent.click(screen.getByRole('button', { name: /Lưu nháp/i }));
    expect(await screen.findByText(/Đã lưu nháp chẩn đoán/i)).toBeInTheDocument();

    // Ensure ZERO PHI written to sessionStorage
    expect(sessionStorage.length).toBe(0);

    fireEvent.click(screen.getByRole('button', { name: 'Quay lại phiên khám' }));
    expect(await screen.findByText('Bàn khám Lâm sàng Mock')).toBeInTheDocument();
  });

  it('[3/16] DoctorCreatePrescriptionPage blocks leaving dirty prescription lines and keeps lock when save fails', async () => {
    loginAsRole('DOCTOR');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK,
    } as AxiosResponse);
    vi.spyOn(apiClient, 'put').mockRejectedValueOnce(new Error('Lỗi máy chủ 500'));

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/doctor/consultation/VIS-2026-0212/prescription']}>
          <Routes>
            <Route
              path="/doctor/consultation/:visitIdOrCode/prescription"
              element={<DoctorCreatePrescriptionPage />}
            />
            <Route
              path="/doctor/consultation/:visitIdOrCode"
              element={<div>Bàn khám Lâm sàng Mock</div>}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const medCodeInput = await screen.findByLabelText('Mã thuốc dòng 1');
    fireEvent.change(medCodeInput, { target: { value: 'PARA-500' } });
    fireEvent.change(screen.getByLabelText('Lịch dùng dòng 1'), {
      target: { value: 'Ngày 2 lần sau ăn' },
    });

    // Submit fails -> form remains dirty & protected
    fireEvent.click(screen.getByRole('button', { name: 'Lưu đơn nháp' }));
    expect(await screen.findByText('Lỗi máy chủ 500')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Quay lại bàn khám lâm sàng' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  // ---------------------------------------------------------------------------
  // 4. Diagnostic Result Entry (Order Switch without URL change + Background Refetch)
  // ---------------------------------------------------------------------------
  it('[4/16] DiagnosticResultEntryPage guards switching CLS orders without URL change and does NOT remount dirty form when server stateVersion increments', async () => {
    loginAsRole('DIAGNOSTIC_STAFF');
    let bumpedVersion = 0;

    vi.spyOn(apiClient, 'get').mockImplementation(async (url, config) => {
      if (url === '/diagnostic/results') {
        const list = resolveMockDiagnosticOrdersForResultEntry(
          config?.params as GetDiagnosticOrdersForResultEntryParams | undefined,
        );
        return {
          data: {
            ...list,
            items: list.items.map((item) => ({
              ...item,
              stateVersion: item.stateVersion + bumpedVersion,
            })),
          },
        };
      }
      if (typeof url === 'string' && url.startsWith('/diagnostic/results/')) {
        const code = decodeURIComponent(url.split('/')[3]);
        const detail = resolveMockDiagnosticResultEntryDetail(code);
        return {
          data: {
            ...detail,
            order: {
              ...detail.order,
              stateVersion: detail.order.stateVersion + bumpedVersion,
            },
          },
        };
      }
      throw new Error(`Unexpected GET ${String(url)}`);
    });

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/diagnostic/results']}>
          <DiagnosticResultEntryPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const resultTextarea = await screen.findByLabelText('Nội dung kết quả cận lâm sàng');
    fireEvent.change(resultTextarea, {
      target: { value: 'Chỉ số Glucose máu tĩnh mạch: 6.4 mmol/L (đang nhập)' },
    });

    // Background refetch bumps stateVersion -> form MUST NOT remount or lose text
    bumpedVersion = 5;
    await act(async () => {
      await qc.invalidateQueries();
    });
    expect(
      (screen.getByLabelText('Nội dung kết quả cận lâm sàng') as HTMLTextAreaElement).value,
    ).toBe('Chỉ số Glucose máu tĩnh mạch: 6.4 mmol/L (đang nhập)');

    // Click another order row in table (no URL change!) -> blocked by UnsavedChangesModal
    const otherOrderCells = screen.getAllByText(/DIA-202610-/);
    const currentOrderCode = otherOrderCells[0].textContent;
    const differentOrderCell = otherOrderCells.find(
      (el) => el.textContent && el.textContent !== currentOrderCode,
    );
    expect(differentOrderCell).toBeDefined();
    fireEvent.click(differentOrderCell!);

    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));
    expect(
      (screen.getByLabelText('Nội dung kết quả cận lâm sàng') as HTMLTextAreaElement).value,
    ).toBe('Chỉ số Glucose máu tĩnh mạch: 6.4 mmol/L (đang nhập)');
  });

  // ---------------------------------------------------------------------------
  // 5-6. Department Create & Edit (Draft Success/Fail + In-Flight Submit Edits)
  // ---------------------------------------------------------------------------
  it('[5/16] DepartmentCreatePage handles draft save failure vs success and protects new edits typed while submit API is in-flight', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: [{ id: 1, hospitalCode: 'HOS-01', name: 'Bệnh viện MedFlow Hà Nội' }],
    } as AxiosResponse);

    let resolvePost!: (val: AxiosResponse) => void;
    const postSpy = vi.spyOn(apiClient, 'post').mockImplementation(
      () =>
        new Promise<AxiosResponse>((resolve) => {
          resolvePost = resolve;
        }),
    );

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/departments/new']}>
          <Routes>
            <Route path="/admin/departments/new" element={<DepartmentCreatePage />} />
            <Route path="/admin/departments" element={<div>Danh mục Khoa phòng</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await screen.findByRole('option', { name: 'Bệnh viện MedFlow Hà Nội' });
    fireEvent.change(screen.getByLabelText('Mã khoa phòng *'), { target: { value: 'DEP-088' } });
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Tim Mạch' },
    });
    fireEvent.change(screen.getByLabelText('Cơ sở *'), { target: { value: '1' } });

    // 1) Draft save failure (QuotaExceededError) -> keeps isDirty = true!
    vi.spyOn(Storage.prototype, 'setItem').mockImplementationOnce(() => {
      throw new DOMException('QuotaExceededError');
    });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu bản nháp' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/Không thể lưu bản nháp/i);

    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));

    // 2) Draft save success -> resets baseline (isDirty = false), then further edit re-dirties
    fireEvent.click(screen.getByRole('button', { name: 'Lưu bản nháp' }));
    expect(screen.getByRole('status')).toHaveTextContent('Đã lưu bản nháp khoa phòng DEP-088.');

    // Re-dirty and test in-flight submit edit
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Tim Mạch Can Thiệp' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Khai báo khoa phòng' }));

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledTimes(1);
    });

    // While POST is still pending, user edits the name again!
    fireEvent.change(screen.getByLabelText('Tên khoa phòng *'), {
      target: { value: 'Khoa Tim Mạch Can Thiệp - Chỉnh thêm lúc đang lưu' },
    });

    await act(async () => {
      resolvePost({ data: { id: 88, code: 'DEP-088' } } as AxiosResponse);
    });

    // Post-submit edit MUST NOT be lost and navigation is guarded!
    expect(await screen.findByTestId('unsaved-changes-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ở lại tiếp tục nhập' }));
    expect((screen.getByLabelText('Tên khoa phòng *') as HTMLInputElement).value).toBe(
      'Khoa Tim Mạch Can Thiệp - Chỉnh thêm lúc đang lưu',
    );
  });

  it('[6/16] DepartmentEditPage blocks Sidebar Link & Cancel when dirty', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            id: '10',
            code: 'DEP-010',
            name: 'Khoa Nội',
            facility: 'MedFlow Cơ sở 1',
            isActive: true,
            status: 'ACTIVE',
          },
        ],
      },
    } as AxiosResponse);

    const qc = createTestQueryClient();
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/departments/10/edit']}>
          <Routes>
            <Route path="/admin/departments/:id/edit" element={<DepartmentEditPage />} />
            <Route path="/admin/rooms" element={<div>Danh mục Phòng</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const nameInput = await screen.findByLabelText('Tên khoa phòng *');
    fireEvent.change(nameInput, { target: { value: 'Khoa Nội Chuyên Sâu' } });

    // Click Sidebar Link in AdminPortalShell (without modifying admin-portal-shell.tsx!)
    fireEvent.click(screen.getByRole('link', { name: /Buồng khám & Kỹ thuật/i }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  // ---------------------------------------------------------------------------
  // 7-16. Remaining 10 Admin Create & Edit Forms
  // ---------------------------------------------------------------------------
  it('[7/16 & 8/16] RoomCreatePage and RoomEditPage guard unsaved room changes', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            id: 1,
            roomCode: 'RM-101',
            name: 'Phòng 101',
            departmentName: 'Khoa Nội',
            roomType: 'OUTPATIENT',
            location: 'Tầng 1',
            capacity: 2,
            status: 'ACTIVE',
            isActive: true,
          },
        ],
      },
    } as AxiosResponse);

    const qc = createTestQueryClient();
    const createView = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/rooms/new']}>
          <Routes>
            <Route path="/admin/rooms/new" element={<RoomCreatePage />} />
            <Route path="/admin/rooms" element={<div>Room List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.change(screen.getByLabelText('Mã phòng *'), { target: { value: 'RM-999' } });
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    createView.unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/rooms/RM-101/edit']}>
          <Routes>
            <Route path="/admin/rooms/:id/edit" element={<RoomEditPage />} />
            <Route path="/admin/rooms" element={<div>Room List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const editName = await screen.findByLabelText('Tên phòng *');
    fireEvent.change(editName, { target: { value: 'Phòng 101 Mới' } });
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  it('[9/16 & 10/16] DoctorCreatePage (including auxiliary work history) and DoctorEditModal guard unsaved changes', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            id: 10,
            doctorCode: 'DOC-0010',
            staffCode: 'STF-0010',
            fullName: 'BS. Lê Văn B',
            email: 'b@medflow.vn',
            degree: 'Thạc sĩ Y học',
            specialtyName: 'Nội tổng quát',
            departmentName: 'Khoa Nội',
            licenseNumber: '12345/BYT',
            roomCode: 'PK-101',
            isActive: true,
          },
        ],
      },
    } as AxiosResponse);

    const qc = createTestQueryClient();
    const createView = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/doctors/new']}>
          <Routes>
            <Route path="/admin/doctors/new" element={<DoctorCreatePage />} />
            <Route path="/admin/doctors" element={<div>Doctor List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    // Dirty via auxiliary work history milestone (outside react-hook-form!)
    fireEvent.click(screen.getByRole('button', { name: '⊕ Thêm mốc đào tạo' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    createView.unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/doctors/DOC-0010/edit']}>
          <Routes>
            <Route path="/admin/doctors/:doctorId/edit" element={<DoctorEditModal />} />
            <Route path="/admin/doctors" element={<div>Doctor List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const docName = await screen.findByDisplayValue('BS. Lê Văn B');
    fireEvent.change(docName, { target: { value: 'BS. Lê Văn B Chỉnh Sửa' } });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  it('[11/16 & 12/16] StaffAccountCreatePage (zero password storage) and StaffAccountEditPage guard unsaved changes', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            staffId: 21,
            code: 'STF-0021',
            fullName: 'Điều dưỡng Mai',
            email: 'mai@medflow.vn',
            authProvider: 'LOCAL',
            facility: 'MedFlow Cơ sở 1 - Hà Nội',
            department: 'Khoa Khám bệnh',
            roleCode: 'RECEPTION',
            role: 'Lễ tân',
            status: 'ACTIVE',
          },
        ],
      },
    } as AxiosResponse);

    const qc = createTestQueryClient();
    const createView = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/staff/new']}>
          <Routes>
            <Route path="/admin/staff/new" element={<StaffAccountCreatePage />} />
            <Route path="/admin/staff" element={<div>Staff List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.change(screen.getByLabelText('Mật khẩu tạm thời *'), {
      target: { value: 'TempPass@123' },
    });
    expect(sessionStorage.length).toBe(0);
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    createView.unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/staff/STF-0021/edit']}>
          <Routes>
            <Route path="/admin/staff/:id/edit" element={<StaffAccountEditPage />} />
            <Route path="/admin/staff" element={<div>Staff List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const fullNameInput = await screen.findByDisplayValue('Điều dưỡng Mai');
    fireEvent.change(fullNameInput, { target: { value: 'Điều dưỡng Mai Anh' } });
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  it('[13/16 & 14/16] ExaminationServiceCreatePage and ServiceEditModal guard unsaved edits and preserve dirty modal fields on refetch', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: {
        data: [
          {
            id: 101,
            serviceCode: 'EX-1-01',
            name: 'Khám Nội Tổng quát',
            description: 'Mô tả gốc',
            category: 'Khám Lâm sàng',
            departmentName: 'Khoa Khám bệnh',
            price: 300000,
            allowsOnlineBooking: true,
            isActive: true,
            status: 'ACTIVE',
          },
        ],
      },
    } as AxiosResponse);

    const qc = createTestQueryClient();
    const createView = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/services/new']}>
          <Routes>
            <Route path="/admin/services/new" element={<ExaminationServiceCreatePage />} />
            <Route path="/admin/services" element={<div>Services List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.change(screen.getByPlaceholderText('EX-2-09'), { target: { value: 'EX-5-05' } });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    createView.unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/services/EX-1-01/edit']}>
          <Routes>
            <Route path="/admin/services/:serviceId/edit" element={<ServiceEditModal />} />
            <Route path="/admin/services" element={<div>Services List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const serviceNameInput = await screen.findByDisplayValue('Khám Nội Tổng quát');
    fireEvent.change(serviceNameInput, { target: { value: 'Khám Nội VIP Đang Sửa' } });

    // Background store update does not wipe dirty input
    act(() => {
      useExaminationServiceStore.getState().updateService('101', { name: 'Tên Server Mới' });
    });
    expect((serviceNameInput as HTMLInputElement).value).toBe('Khám Nội VIP Đang Sửa');

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });

  it('[15/16 & 16/16] ExaminationSlotCreatePage and SlotEditModal guard unsaved edits and preserve dirty modal fields on refetch', async () => {
    loginAsRole('SYSTEM_ADMIN');
    vi.spyOn(apiClient, 'get').mockImplementation(async (url) => {
      if (String(url).includes('/admin/examination-slots')) {
        return {
          data: {
            data: [
              {
                id: 1,
                code: 'SLT-001',
                doctorId: 10,
                doctorName: 'BS. Lan',
                serviceId: 5,
                serviceName: 'Khám Nội',
                serviceCode: 'EX-1-01',
                serviceType: 'OUTPATIENT',
                specialty: 'Nội tiết',
                room: 'PK-102',
                capacityBooked: 2,
                capacityMax: 8,
                date: '2026-11-27',
                startTime: '08:00',
                endTime: '08:30',
                status: 'OPEN',
                facility: 'MedFlow Cơ sở 1',
              },
            ],
          },
        } as AxiosResponse;
      }
      return { data: { data: [] } } as AxiosResponse;
    });

    const qc = createTestQueryClient();
    const createView = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/slots/new']}>
          <Routes>
            <Route path="/admin/slots/new" element={<ExaminationSlotCreatePage />} />
            <Route path="/admin/slots" element={<div>Slots List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    fireEvent.change(screen.getByLabelText(/Mã slot/i), { target: { value: 'SLT-NEW-01' } });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
    createView.unmount();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/slots/SLT-001/edit']}>
          <Routes>
            <Route path="/admin/slots/:slotId/edit" element={<SlotEditModal />} />
            <Route path="/admin/slots" element={<div>Slots List</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await screen.findByDisplayValue('SLT-001');
    fireEvent.click(screen.getByRole('button', { name: '12' }));

    // Background store update does not wipe dirty capacityMax=12
    act(() => {
      useExaminationSlotStore.getState().updateSlot('1', { capacityMax: 6 });
    });

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.getByTestId('unsaved-changes-modal')).toBeInTheDocument();
  });
});
