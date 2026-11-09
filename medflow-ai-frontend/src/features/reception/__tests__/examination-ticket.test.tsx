import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ExaminationTicketCard } from '../components/examination-ticket-card';
import { ExaminationTicketModal } from '../components/examination-ticket-modal';
import { EXAMINATION_TICKET_MOCK } from '../data/examination-ticket-mock';

describe('ExaminationTicketCard', () => {
  it('render day du thong tin phieu kham', () => {
    render(<ExaminationTicketCard ticket={EXAMINATION_TICKET_MOCK} />);

    expect(screen.getByText('BỆNH VIỆN ĐA KHOA QUỐC TẾ MEDFLOW')).toBeInTheDocument();
    expect(screen.getByText('PHIẾU KHÁM BỆNH VÀO PHÒNG')).toBeInTheDocument();
    expect(screen.getByText('009')).toBeInTheDocument();
    expect(screen.getByText('PHÒNG 206 - KHOA TIÊU HÓA (TẦNG 2, NHÀ A)')).toBeInTheDocument();
    expect(screen.getByText('ThS.BS Nguyễn Văn Hùng')).toBeInTheDocument();
    expect(screen.getByText('Trần Minh Đức (Nam, 41 tuổi)')).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0021')).toBeInTheDocument();
    expect(screen.getAllByText('VIS-2026-0415-0145').length).toBeGreaterThan(0);
    expect(screen.getByText('Khám Chuyên khoa Tiêu Hóa')).toBeInTheDocument();
    expect(screen.getByText('08:45 • Hôm nay')).toBeInTheDocument();
    expect(screen.getByText('09:00 - 09:15')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Mã vạch/ })).toBeInTheDocument();
    expect(screen.getByText(/theo dõi màn hình hiển thị số thứ tự/)).toBeInTheDocument();
  });
});

describe('ExaminationTicketModal', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('khong render khi ticket = null', () => {
    render(<ExaminationTicketModal ticket={null} onClose={() => undefined} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('goi window.print khi bam In phieu kham', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    render(<ExaminationTicketModal ticket={EXAMINATION_TICKET_MOCK} onClose={() => undefined} />);
    fireEvent.click(screen.getByRole('button', { name: 'In phiếu khám (F4)' }));
    expect(print).toHaveBeenCalledTimes(1);
  });

  it('goi window.print khi nhan F4', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    render(<ExaminationTicketModal ticket={EXAMINATION_TICKET_MOCK} onClose={() => undefined} />);
    fireEvent.keyDown(document, { key: 'F4' });
    expect(print).toHaveBeenCalledTimes(1);
  });

  it('goi onClose khi bam Dong', () => {
    const onClose = vi.fn();
    render(<ExaminationTicketModal ticket={EXAMINATION_TICKET_MOCK} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: 'Đóng phiếu khám' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
