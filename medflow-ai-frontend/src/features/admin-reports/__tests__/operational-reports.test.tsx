import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';

import { INITIAL_OPERATIONAL_REPORTS } from '../data/operational-reports-data';
import { OperationalReportsPage } from '../pages/operational-reports-page';
import { useOperationalReportStore } from '../store/operational-report.store';

describe('OperationalReportsPage (SCR-ADM-RPT-01)', () => {
  beforeEach(() => {
    useOperationalReportStore.setState({
      reports: INITIAL_OPERATIONAL_REPORTS,
      searchQuery: '',
      periodFilter: '22/10/2026',
      facilityFilter: '',
      categoryFilter: '',
      page: 1,
      pageSize: 20,
      selectedIds: [],
    });
  });

  const renderPage = () =>
    render(
      <MemoryRouter>
        <OperationalReportsPage />
      </MemoryRouter>,
    );

  it('renders sidebar with MedflowLogo, clean header breadcrumbs, and 4 KPI metric cards', () => {
    renderPage();

    expect(screen.getByAltText('MedFlow AI')).toBeInTheDocument();
    expect(
      screen.getByRole('complementary', { name: 'Danh mục quản trị cơ sở' }),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Báo cáo vận hành').length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText('SCR-ADM-RPT-01')).not.toBeInTheDocument();
    expect(screen.queryByText(/HL7\/FHIR/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Bản xem trước')).not.toBeInTheDocument();
    expect(screen.getByText('326')).toBeInTheDocument();
    expect(screen.getByText('18')).toBeInTheDocument();
    expect(screen.getAllByText('94%').length).toBeGreaterThan(0);
    expect(screen.getByText('08')).toBeInTheDocument();
  });

  it('renders the 3 operational charts with peak badge and department/status breakdowns', () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Lượt tiếp nhận theo giờ' })).toBeInTheDocument();
    expect(screen.getByText('Cao điểm 11:00 · 42 lượt')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Lượt khám theo khoa' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Trạng thái lượt khám' })).toBeInTheDocument();
    expect(screen.getByText('306 lượt')).toBeInTheDocument();
  });

  it('renders all 8 operational report rows (RPT-101 to RPT-108)', () => {
    renderPage();

    expect(screen.getByText('RPT-101')).toBeInTheDocument();
    expect(screen.getByText('Tiếp nhận ngoại trú')).toBeInTheDocument();
    expect(screen.getByText('RPT-104')).toBeInTheDocument();
    expect(screen.getByText('RPT-108')).toBeInTheDocument();
    expect(screen.getByText('Hoạt động tài khoản')).toBeInTheDocument();
  });

  it('filters reports by search query and resets filters', () => {
    renderPage();

    const searchInput = screen.getByLabelText('Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp');
    fireEvent.change(searchInput, { target: { value: 'RPT-106' } });

    expect(screen.getByText('Công suất buồng khám')).toBeInTheDocument();
    expect(screen.queryByText('Tiếp nhận ngoại trú')).not.toBeInTheDocument();

    const resetBtn = screen.getByRole('button', { name: /Đặt lại/i });
    fireEvent.click(resetBtn);

    expect(screen.getByText('Tiếp nhận ngoại trú')).toBeInTheDocument();
  });

  it('filters reports by period and category', () => {
    renderPage();

    const periodSelect = screen.getByLabelText('Lọc kỳ báo cáo');
    fireEvent.change(periodSelect, { target: { value: 'Tuần 42 · 2026' } });

    expect(screen.getByText('RPT-104')).toBeInTheDocument();
    expect(screen.queryByText('RPT-101')).not.toBeInTheDocument();

    const categorySelect = screen.getByLabelText('Lọc loại báo cáo');
    fireEvent.change(categorySelect, { target: { value: 'Sử dụng phòng' } });

    expect(screen.getByText('RPT-106')).toBeInTheDocument();
    expect(screen.queryByText('RPT-104')).not.toBeInTheDocument();
  });

  it('focuses search input on Ctrl+K shortcut and refreshes a report needing refresh', () => {
    renderPage();

    const searchInput = screen.getByLabelText('Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp');
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(document.activeElement).toBe(searchInput);

    const refreshButton = screen.getByLabelText('Làm mới báo cáo RPT-104');
    fireEvent.click(refreshButton);

    expect(screen.queryByLabelText('Làm mới báo cáo RPT-104')).not.toBeInTheDocument();
  });
});
