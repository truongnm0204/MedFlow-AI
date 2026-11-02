import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { INITIAL_DOCTORS } from '../data/doctor-demo-data';
import { DoctorListPage } from '../pages/doctor-list-page';
import { useDoctorDemoStore } from '../store/doctor-demo.store';

describe('DoctorListPage (SCR-ADM-DOC-01)', () => {
  beforeEach(() => {
    useDoctorDemoStore.setState({
      doctors: [...INITIAL_DOCTORS],
      draft: null,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    Reflect.deleteProperty(URL, 'createObjectURL');
    Reflect.deleteProperty(URL, 'revokeObjectURL');
  });

  const renderPage = () =>
    render(
      <MemoryRouter initialEntries={['/admin/doctors']}>
        <Routes>
          <Route path="/admin/doctors" element={<DoctorListPage />}>
            <Route path=":doctorId/edit" element={<div>Edit Modal Mock</div>} />
          </Route>
          <Route path="/admin/doctors/new" element={<div>Create Page Mock</div>} />
        </Routes>
      </MemoryRouter>,
    );

  const getDisplayedRowLinks = () => screen.getAllByRole('link', { name: /^Chỉnh sửa / });

  it('displays overview metric counts from store doctors', () => {
    renderPage();

    const overview = screen.getByRole('region', { name: 'Tổng quan bác sĩ' });
    expect(within(overview).getByText('08')).toBeInTheDocument();
    expect(within(overview).getByText('06')).toBeInTheDocument();
    expect(within(overview).getByText('02')).toBeInTheDocument();
    expect(within(overview).getByText('05')).toBeInTheDocument();
  });

  it('toggles doctor active status and updates metrics', () => {
    renderPage();

    const suspendBtn = screen.getByRole('button', {
      name: 'Tạm ngừng BS. CKI Lê Hoàng Minh',
    });
    fireEvent.click(suspendBtn);

    const updated = useDoctorDemoStore.getState().doctors.find((d) => d.id === 'DOC-0142');
    expect(updated?.active).toBe(false);
    expect(
      screen.getByRole('button', { name: 'Kích hoạt BS. CKI Lê Hoàng Minh' }),
    ).toBeInTheDocument();

    const overview = screen.getByRole('region', { name: 'Tổng quan bác sĩ' });
    expect(within(overview).getAllByText('05').length).toBeGreaterThanOrEqual(2);
    expect(within(overview).getByText('03')).toBeInTheDocument();
  });

  it('searches doctors by specialty, license, and shows empty state when no match', () => {
    renderPage();

    const searchInput = screen.getByLabelText('Tìm bác sĩ');

    fireEvent.change(searchInput, { target: { value: 'tim mạch' } });
    expect(getDisplayedRowLinks()).toHaveLength(1);
    expect(screen.getByText('DOC-0089')).toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: '008910' } });
    expect(getDisplayedRowLinks()).toHaveLength(1);
    expect(screen.getByText('DOC-0231')).toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: 'khong-co' } });
    expect(screen.queryAllByRole('link', { name: /^Chỉnh sửa / })).toHaveLength(0);
    expect(screen.getByText('Không tìm thấy bác sĩ phù hợp.')).toBeInTheDocument();
  });

  it('filters doctors by department and status combined', () => {
    renderPage();

    fireEvent.change(screen.getByLabelText('Lọc khoa phòng'), {
      target: { value: 'Khoa Nội Tiêu hóa' },
    });
    expect(getDisplayedRowLinks()).toHaveLength(3);

    fireEvent.change(screen.getByLabelText('Lọc trạng thái'), {
      target: { value: 'true' },
    });
    expect(getDisplayedRowLinks()).toHaveLength(2);
    expect(screen.getByText('DOC-0142')).toBeInTheDocument();
    expect(screen.getByText('DOC-0198')).toBeInTheDocument();
    expect(screen.queryByText('DOC-0231')).not.toBeInTheDocument();
  });

  it('filters doctors by degree', () => {
    renderPage();

    fireEvent.change(screen.getByLabelText('Lọc học hàm học vị'), {
      target: { value: 'Thạc sĩ Y học' },
    });

    expect(getDisplayedRowLinks()).toHaveLength(2);
    expect(screen.getByText('DOC-0210')).toBeInTheDocument();
    expect(screen.getByText('DOC-0198')).toBeInTheDocument();
  });

  it('paginates beyond 8 items and resets to page 1 when filter changes', () => {
    useDoctorDemoStore.setState({
      doctors: [
        ...INITIAL_DOCTORS,
        {
          ...INITIAL_DOCTORS[0],
          id: 'DOC-0300',
          staffId: 'NV-1300',
          name: 'BS. Test Phân Trang',
        },
      ],
    });

    renderPage();

    expect(getDisplayedRowLinks()).toHaveLength(8);
    expect(screen.queryByText('BS. Test Phân Trang')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '2' }));
    expect(getDisplayedRowLinks()).toHaveLength(1);
    expect(screen.getByText('BS. Test Phân Trang')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Tìm bác sĩ'), {
      target: { value: 'DOC' },
    });
    expect(getDisplayedRowLinks()).toHaveLength(8);
  });

  it('exports CSV with UTF-8 BOM and neutralizes formula injection payloads', async () => {
    const createObjectURL = vi.fn((_obj: Blob | MediaSource) => 'blob:mock');
    const revokeObjectURL = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', {
      value: createObjectURL,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      value: revokeObjectURL,
      configurable: true,
      writable: true,
    });

    const downloads: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });

    useDoctorDemoStore.setState({
      doctors: [INITIAL_DOCTORS[0], { ...INITIAL_DOCTORS[1], name: '=cmd|"/c calc"!A1' }],
    });

    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Xuất CSV/i }));

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    const blob = createObjectURL.mock.calls[0][0] as Blob;
    const bytes = new Uint8Array(await blob.arrayBuffer());
    expect(Array.from(bytes.slice(0, 3))).toEqual([0xef, 0xbb, 0xbf]);

    const lines = (await blob.text()).split('\r\n');
    expect(lines).toHaveLength(3);
    expect(lines[0].startsWith('"Mã BS","Họ tên"')).toBe(true);
    expect(lines[1]).toContain('"Đang nhận lịch"');
    expect(lines[2]).toContain('"\'=cmd|""/c calc""!A1"');
    expect(downloads).toEqual(['danh-sach-bac-si-minh-hoa.csv']);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock');
  });

  it('exports only filtered doctors to CSV', async () => {
    const createObjectURL = vi.fn((_obj: Blob | MediaSource) => 'blob:mock');
    Object.defineProperty(URL, 'createObjectURL', {
      value: createObjectURL,
      configurable: true,
      writable: true,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      value: vi.fn(),
      configurable: true,
      writable: true,
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    renderPage();

    fireEvent.change(screen.getByLabelText('Lọc trạng thái'), {
      target: { value: 'false' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Xuất CSV/i }));

    const blob = createObjectURL.mock.calls[0][0] as Blob;
    const lines = (await blob.text()).split('\r\n');
    expect(lines).toHaveLength(3);
    expect(lines[1]).toContain('DOC-0210');
    expect(lines[2]).toContain('DOC-0231');
  });

  it('handles Ctrl+K focus shortcut and F2 navigation shortcut', () => {
    renderPage();

    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(document.activeElement).toBe(screen.getByLabelText('Tìm bác sĩ'));

    fireEvent.keyDown(window, { key: 'F2' });
    expect(screen.getByText('Create Page Mock')).toBeInTheDocument();
  });

  it('renders nested edit modal outlet while keeping the list visible', () => {
    renderPage();

    fireEvent.click(screen.getByRole('link', { name: 'Chỉnh sửa BS. CKI Lê Hoàng Minh' }));

    expect(screen.getByText('Edit Modal Mock')).toBeInTheDocument();
    expect(screen.getByText('DOC-0089')).toBeInTheDocument();
  });
});
