using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    private async Task<QueueEntry> FindExaminationQueueEntryForActionAsync(
        long? numericId,
        string? code,
        CancellationToken ct)
    {
        QueueEntry? target = null;
        if (numericId.HasValue)
        {
            target = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == numericId.Value, ct);
        }
        else if (!string.IsNullOrEmpty(code))
        {
            var visit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.VisitCode == code, ct);
            if (visit is not null)
            {
                target = await _db.QueueEntries
                    .Where(q => q.VisitId == visit.Id && (q.QueueType == null || q.QueueType == "EXAMINATION"))
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (target is null)
        {
            throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        }

        return target;
    }

    private static (QueueBoardBucket Bucket, QueueBoardEntry Entry)? FindBoardEntry(
        QueueBoard board,
        long queueEntryId)
    {
        foreach (var b in board.Buckets)
        {
            var found = b.Entries.FirstOrDefault(e => e.QueueEntry?.Id == queueEntryId);
            if (found is not null)
            {
                return (b, found);
            }
        }

        return null;
    }

    private static string ValidateCoordinationReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 5 || trimmed.Length > 300)
        {
            throw new ValidationException("Lý do điều phối phải từ 5 đến 300 ký tự.");
        }

        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException("Lý do điều phối không được chứa ký tự < hoặc >.");
        }

        return trimmed;
    }

    private static string? ValidateCoordinationNotes(string? notes)
    {
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmed is not null)
        {
            if (trimmed.Length > 300)
            {
                throw new ValidationException("Ghi chú tối đa 300 ký tự.");
            }

            if (trimmed.Contains('<') || trimmed.Contains('>'))
            {
                throw new ValidationException("Ghi chú không được chứa ký tự < hoặc >.");
            }
        }

        return trimmed;
    }

    private static bool IsTerminalVisit(Visit? visit)
        => AppointmentBookingRules.NormalizeStatusKey(visit?.Status) is "COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE";
}
