import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { TransferRoomModal } from '../components/transfer-room-modal';
import { TRANSFER_QUEUE_VISIT_MOCK } from '../data/transfer-room-mock';

const visit = TRANSFER_QUEUE_VISIT_MOCK;

function renderModal(onSubmit = vi.fn(), onClose = vi.fn()) {
  render(<TransferRoomModal visit={visit} onClose={onClose} onSubmit={onSubmit} />);
  return { onSubmit, onClose };
}

describe('TransferRoomModal', () => {
  it('khong render khi visit = null', () => {
    render(<TransferRoomModal visit={null} onClose={() => undefined} onSubmit={() => undefined} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('render thong tin benh nhan, phong hien tai va lich su', () => {
    renderModal();
    expect(screen.getByText(/CHI TIẾT LƯỢT CHỜ/)).toBeInTheDocument();
    expect(screen.getByText(/Nguyễn Văn An • Nam, 34 tuổi/)).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
    expect(screen.getByText('VIS-2026-0415-0108')).toBeInTheDocument();
    expect(
      screen.getByText(/Phòng 204 - BS.CKI Trần Quốc Tuấn • Khoa Tiêu Hóa/),
    ).toBeInTheDocument();
    expect(screen.getByText(/Phòng 206 - ThS.BS Nguyễn Văn Hùng/)).toBeInTheDocument();
    expect(screen.getByText(/Phòng 208 - BS.CKI Phạm Minh Trí/)).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(visit.history.length);
  });

  it('bao loi khi chua chon phong va ly do', () => {
    const { onSubmit } = renderModal();
    fireEvent.click(screen.getByRole('button', { name: 'Lưu điều phối và cập nhật hàng đợi' }));
    expect(screen.getByText('Vui lòng chọn buồng khám đích')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng chọn lý do chuyển buồng')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('bat buoc ghi chu khi ly do la Khac', () => {
    const { onSubmit } = renderModal();
    fireEvent.click(screen.getByLabelText(/Phòng 206/));
    fireEvent.click(screen.getByLabelText('Khác'));
    fireEvent.click(screen.getByRole('button', { name: 'Lưu điều phối và cập nhật hàng đợi' }));
    expect(screen.getByText(/Vui lòng nhập ghi chú/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('goi onSubmit voi payload hop le', () => {
    const { onSubmit } = renderModal();
    fireEvent.click(screen.getByLabelText(/Phòng 208/));
    fireEvent.click(screen.getByLabelText('Cân bằng tải buồng khám'));
    fireEvent.change(screen.getByLabelText('Ghi chú lý do'), { target: { value: ' Quá tải ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu điều phối và cập nhật hàng đợi' }));
    expect(onSubmit).toHaveBeenCalledWith({
      visitCode: visit.visitCode,
      targetRoomId: 'room-208',
      reasonCode: 'LOAD_BALANCING',
      note: 'Quá tải',
    });
  });

  it('goi onClose khi bam Dong / Huy bo', () => {
    const { onClose } = renderModal();
    fireEvent.click(screen.getByRole('button', { name: 'Đóng / Hủy bỏ' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
