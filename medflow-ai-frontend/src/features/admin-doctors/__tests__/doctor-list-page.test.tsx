import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { DoctorListPage } from '../pages/doctor-list-page';
import { useDoctorDemoStore } from '../store/doctor-demo.store';
import { INITIAL_DOCTORS } from '../data/doctor-demo-data';

describe('DoctorListPage (SCR-ADM-DOC-01 & SCR-ADM-DOC-05)', () => {
  beforeEach(() => {
    // Reset store before each test
    useDoctorDemoStore.setState({
      doctors: JSON.parse(JSON.stringify(INITIAL_DOCTORS)),
      draft: null,
    });
  });

  const renderPage = () =>
    render(
      <MemoryRouter>
        <DoctorListPage />
      </MemoryRouter>,
    );

  it('renders metrics cards with ĐANG HOẠT ĐỘNG and NGỪNG HOẠT ĐỘNG matching Figma', () => {
    renderPage();

    expect(screen.getByText('TỔNG SỐ BÁC SĨ CƠ SỞ')).toBeInTheDocument();
    expect(screen.getByText('ĐANG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('NGỪNG HOẠT ĐỘNG')).toBeInTheDocument();
    expect(screen.getByText('TRÌNH ĐỘ SAU ĐẠI HỌC')).toBeInTheDocument();
    expect(screen.getByText('Được phân công khi đủ điều kiện')).toBeInTheDocument();
    expect(screen.getByText('Không nhận lịch hoặc phân công mới')).toBeInTheDocument();
  });

  it('filters doctors by status "Đang hoạt động" and "Ngừng hoạt động"', () => {
    renderPage();

    const statusSelect = screen.getByLabelText('Lọc trạng thái');
    expect(statusSelect).toBeInTheDocument();

    // Select "Ngừng hoạt động"
    fireEvent.change(statusSelect, { target: { value: 'false' } });
    const inactiveBadges = screen.getAllByText('Ngừng hoạt động');
    expect(inactiveBadges.length).toBeGreaterThanOrEqual(1);

    // Select "Đang hoạt động"
    fireEvent.change(statusSelect, { target: { value: 'true' } });
    const activeBadges = screen.getAllByText('Đang hoạt động');
    expect(activeBadges.length).toBeGreaterThanOrEqual(1);
  });

  it('opens DoctorDeactivateModal and deactivates doctor upon confirmation', async () => {
    renderPage();

    // Find active doctor DOC-0142
    const doctorRow = screen.getByText('DOC-0142').closest('tr');
    expect(doctorRow).toBeInTheDocument();

    // Open action menu for DOC-0142
    const moreBtn = screen.getByRole('button', { name: 'Thao tác BS. CKI Lê Hoàng Minh' });
    fireEvent.click(moreBtn);

    // Click "Ngừng hoạt động" in dropdown
    const deactivateBtn = screen.getByRole('button', {
      name: 'Ngừng hoạt động BS. CKI Lê Hoàng Minh',
    });
    fireEvent.click(deactivateBtn);

    // Deactivation modal should now be open
    const dialog = screen.getByRole('dialog');
    expect(
      within(dialog).getByRole('heading', { name: 'Ngừng hoạt động bác sĩ?' }),
    ).toBeInTheDocument();
    expect(within(dialog).getByText(/BS\. CKI Lê Hoàng Minh/)).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Bác sĩ sẽ không nhận lịch hoặc phân công mới/i),
    ).toBeInTheDocument();

    // Confirm deactivation
    const confirmBtn = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    fireEvent.click(confirmBtn);

    // Modal closes
    expect(screen.queryByRole('heading', { name: 'Ngừng hoạt động bác sĩ?' })).toBeNull();

    // Feedback notification shows
    expect(
      screen.getByText(
        /Đã chuyển trạng thái bác sĩ BS\. CKI Lê Hoàng Minh sang Ngừng hoạt động\./i,
      ),
    ).toBeInTheDocument();

    // Doctor DOC-0142 should now be inactive in the store
    const storeDoctors = useDoctorDemoStore.getState().doctors;
    const doc142 = storeDoctors.find((d) => d.id === 'DOC-0142');
    expect(doc142?.active).toBe(false);
  });
});
