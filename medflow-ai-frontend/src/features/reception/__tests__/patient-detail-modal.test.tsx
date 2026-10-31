import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { PATIENT_DETAILS_MOCK } from '../data/patient-detail-mock';
import { PatientDetailModal } from '../components/patient-detail-modal';

const patient = PATIENT_DETAILS_MOCK[0]!;

describe('PatientDetailModal', () => {
  it('khong render khi patient = null', () => {
    render(<PatientDetailModal patient={null} onClose={() => undefined} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('render thong tin benh nhan va lich su kham', () => {
    render(<PatientDetailModal patient={patient} onClose={() => undefined} />);

    expect(screen.getByText(/Nguyễn Văn An • Nam, 34 tuổi/)).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
    expect(screen.getByText(/0010 8801 5708/)).toBeInTheDocument();
    expect(screen.getByText('DN4010123456789')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ CÓ MẶT TẠI SẢNH')).toBeInTheDocument();
    expect(screen.getByText('Viêm họng cấp')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(patient.pastVisits.length + 1);
  });

  it('goi onClose khi bam Dong', () => {
    const onClose = vi.fn();
    render(<PatientDetailModal patient={patient} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: 'Đóng hộp thoại' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('goi onActivate khi bam Tiep nhan', () => {
    const onActivate = vi.fn();
    render(
      <PatientDetailModal patient={patient} onClose={() => undefined} onActivate={onActivate} />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Tiếp nhận / Kích hoạt lượt khám' }));
    expect(onActivate).toHaveBeenCalledWith(patient);
  });
});
