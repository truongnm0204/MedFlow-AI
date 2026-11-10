using System.Globalization;
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
                var visitEntries = await _db.QueueEntries
                    .Where(q =>
                        q.VisitId == visit.Id &&
                        (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
                    .OrderByDescending(q => q.Id)
                    .ToListAsync(ct);

                target = visitEntries.FirstOrDefault(q =>
                    ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType));
            }
        }

        if (target is null)
        {
            throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        }

        return target;
    }

    private async Task<(Visit Visit, QueueEntry? ExistingReturnEntry, QueueEntry? ExistingExamEntry)> ResolveVisitAndQueueEntriesForReturnAsync(
        long? numericId,
        string? code,
        string rawIdOrCode,
        CancellationToken ct)
    {
        var trimmedRaw = rawIdOrCode?.Trim() ?? string.Empty;
        Visit? visit = null;

        if (numericId.HasValue &&
            (trimmedRaw.StartsWith("q-", StringComparison.OrdinalIgnoreCase) ||
             trimmedRaw.StartsWith("q_", StringComparison.OrdinalIgnoreCase)))
        {
            var queueEntry = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == numericId.Value, ct)
                ?? throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");

            if (!ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType))
            {
                throw new ConflictException("Chỉ hỗ trợ tạo hoặc kích hoạt lượt khám lại từ lượt khám lâm sàng.");
            }

            visit = await ResolveVisitFromQueueEntryForReturnAsync(queueEntry, ct);
        }
        else if (numericId.HasValue)
        {
            var candidateQueueEntry = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == numericId.Value, ct);
            if (candidateQueueEntry is not null && ClinicalQueueRules.IsClinicalExaminationQueueType(candidateQueueEntry.QueueType))
            {
                visit = await ResolveVisitFromQueueEntryForReturnAsync(candidateQueueEntry, ct);
            }
            else
            {
                visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == numericId.Value, ct);
                if (visit is null)
                {
                    if (candidateQueueEntry is not null)
                    {
                        throw new ConflictException("Chỉ hỗ trợ tạo hoặc kích hoạt lượt khám lại từ lượt khám lâm sàng.");
                    }

                    throw new NotFoundException("Không tìm thấy lượt khám hoặc lượt xếp hàng.");
                }
            }
        }
        else if (!string.IsNullOrEmpty(code))
        {
            visit = await _db.Visits.FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == code, ct);
            if (visit is null &&
                code.StartsWith("VIS-", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(code.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVisitId) &&
                parsedVisitId > 0)
            {
                visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == parsedVisitId, ct);
            }

            if (visit is null)
            {
                throw new NotFoundException("Không tìm thấy lượt khám hoặc lượt xếp hàng.");
            }
        }

        if (visit is null)
        {
            throw new NotFoundException("Không tìm thấy lượt khám hoặc lượt xếp hàng.");
        }

        var trackedEntries = await _db.QueueEntries
            .Where(q =>
                (q.VisitId == visit.Id ||
                 (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value)) &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var existingReturnEntry = trackedEntries.FirstOrDefault(q =>
            ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation);
        var existingExamEntry = trackedEntries.FirstOrDefault(q =>
            ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType) &&
            ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeExamination);

        if (existingReturnEntry is not null &&
            AppointmentBookingRules.NormalizeStatusKey(existingReturnEntry.Status) is "COMPLETED" or "DONE")
        {
            var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            var hasSubsequentDiagnosticEntry = trackedEntries.Any(q =>
                string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                q.Id > existingReturnEntry.Id);

            if (normalizedVisitStatus is "AWAITING_RESULTS" or "AWAITING_DIAGNOSTIC" ||
                hasSubsequentDiagnosticEntry)
            {
                existingExamEntry = existingReturnEntry;
                existingReturnEntry = null;
            }
        }

        return (visit, existingReturnEntry, existingExamEntry);
    }

    private async Task<Visit> ResolveVisitFromQueueEntryForReturnAsync(
        QueueEntry queueEntry,
        CancellationToken ct)
    {
        Visit? visit = null;
        if (queueEntry.VisitId.HasValue)
        {
            visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == queueEntry.VisitId.Value, ct);
        }

        if (visit is null && queueEntry.AppointmentId.HasValue)
        {
            visit = await _db.Visits
                .Where(v => v.AppointmentId == queueEntry.AppointmentId.Value)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (visit is null)
        {
            throw new ConflictException(
                "Bệnh nhân chưa hoàn tất tiếp nhận lượt khám (Visit), không thể tạo lượt khám lại đọc kết quả cận lâm sàng.");
        }

        return visit;
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
