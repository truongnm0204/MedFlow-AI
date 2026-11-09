using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reception;

public sealed class ReceptionPatientSearchService : IReceptionPatientSearchService
{
    private const string TabAll = "ALL";
    private const string TabHasAppointment = "HAS_APPOINTMENT";
    private const string TabCheckedIn = "CHECKED_IN";
    private const string TabNoAppointment = "NO_APPOINTMENT";
    private const int MaxQueryLength = 100;
    private const int MaxPageSize = 100;
    private const string CompletedStatus = "COMPLETED";
    private const string NotUpdated = "Chưa cập nhật";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ReceptionPatientSearchService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SearchPatientsResponseDto> SearchPatientsAsync(
        SearchPatientsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tab = NormalizeTab(request.Tab);
        if (request.Page < 1)
            throw new ValidationException("Page phải lớn hơn hoặc bằng 1.");
        if (request.PageSize < 1 || request.PageSize > MaxPageSize)
            throw new ValidationException($"PageSize phải nằm trong khoảng 1 đến {MaxPageSize}.");

        var term = request.Query?.Trim();
        if (term is { Length: > MaxQueryLength })
            throw new ValidationException($"Từ khóa tìm kiếm tối đa {MaxQueryLength} ký tự.");

        var todayStart = AppointmentBookingRules.GetVietnamNow(_timeProvider).Date;
        var todayEnd = todayStart.AddDays(1);

        var todayAppointments = _db.Appointments.AsNoTracking()
            .Where(a => a.PatientId != null
                && a.Slot != null
                && a.Slot.StartsAt >= todayStart
                && a.Slot.StartsAt < todayEnd
                && a.Status != AppointmentBookingRules.CancelledStatus
                && a.Status != AppointmentBookingRules.NoShowStatus);

        var patients = _db.Patients.AsNoTracking().Where(p => p.Status == "ACTIVE");

        if (!string.IsNullOrEmpty(term))
        {
            var lowered = term.ToLowerInvariant();
            if (IsIdentityNumber(term))
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(term))).ToLowerInvariant();
                patients = patients.Where(p =>
                    p.PatientCode.ToLower().Contains(lowered)
                    || p.FullName.ToLower().Contains(lowered)
                    || (p.Phone != null && p.Phone.ToLower().Contains(lowered))
                    || p.IdentificationHash == hash);
            }
            else
            {
                patients = patients.Where(p =>
                    p.PatientCode.ToLower().Contains(lowered)
                    || p.FullName.ToLower().Contains(lowered)
                    || (p.Phone != null && p.Phone.ToLower().Contains(lowered)));
            }
        }

        var hasAppointment = patients.Where(p => todayAppointments.Any(a => a.PatientId == p.Id));
        var checkedIn = patients.Where(p => todayAppointments.Any(a =>
            a.PatientId == p.Id && a.Status == AppointmentBookingRules.CheckedInStatus));
        var noAppointment = patients.Where(p => !todayAppointments.Any(a => a.PatientId == p.Id));

        var counts = new SearchTabCountsDto(
            await patients.CountAsync(ct),
            await hasAppointment.CountAsync(ct),
            await checkedIn.CountAsync(ct),
            await noAppointment.CountAsync(ct));

        var filtered = tab switch
        {
            TabHasAppointment => hasAppointment,
            TabCheckedIn => checkedIn,
            TabNoAppointment => noAppointment,
            _ => patients,
        };

        var totalCount = tab switch
        {
            TabHasAppointment => counts.HasAppointment,
            TabCheckedIn => counts.CheckedIn,
            TabNoAppointment => counts.NoAppointment,
            _ => counts.All,
        };

        var pagePatients = await filtered
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var pageIds = pagePatients.Select(p => p.Id).ToList();

        var appointmentRows = await todayAppointments
            .Where(a => pageIds.Contains(a.PatientId!.Value))
            .Select(a => new
            {
                a.Id,
                PatientId = a.PatientId!.Value,
                a.Status,
                StartsAt = a.Slot!.StartsAt,
                DoctorName = a.Slot.Doctor != null && a.Slot.Doctor.Staff != null
                    ? a.Slot.Doctor.Staff.FullName
                    : null,
                ServiceDepartment = a.Slot.Service != null && a.Slot.Service.Department != null
                    ? a.Slot.Service.Department.Name
                    : null,
                RoomName = a.Slot.Room != null ? a.Slot.Room.Name : null,
                RoomDepartment = a.Slot.Room != null && a.Slot.Room.Department != null
                    ? a.Slot.Room.Department.Name
                    : null,
            })
            .ToListAsync(ct);

        // One summary per patient: prefer the checked-in appointment, otherwise the earliest one.
        var todayByPatient = appointmentRows
            .GroupBy(r => r.PatientId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => r.Status == AppointmentBookingRules.CheckedInStatus)
                    .ThenBy(r => r.StartsAt)
                    .ThenBy(r => r.Id)
                    .First());

        var visitCounts = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId != null && pageIds.Contains(v.PatientId.Value) && v.Status == CompletedStatus)
            .GroupBy(v => v.PatientId!.Value)
            .Select(g => new { PatientId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Total, ct);

        var items = pagePatients
            .Select(p =>
            {
                TodayAppointmentSummaryDto? today = null;
                if (todayByPatient.TryGetValue(p.Id, out var row))
                {
                    today = new TodayAppointmentSummaryDto(
                        row.Id,
                        row.StartsAt?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                        row.DoctorName ?? NotUpdated,
                        row.ServiceDepartment ?? row.RoomDepartment ?? NotUpdated,
                        row.RoomName ?? NotUpdated,
                        row.Status ?? string.Empty);
                }

                return new PatientSearchResultItemDto(
                    p.Id,
                    p.PatientCode,
                    p.FullName,
                    p.Gender,
                    p.DateOfBirth,
                    string.IsNullOrWhiteSpace(p.IdentificationHash) ? null : PatientProfileService.MaskedVerifiedIdentityText,
                    p.Phone,
                    p.Address,
                    today,
                    visitCounts.GetValueOrDefault(p.Id));
            })
            .ToList();

        return new SearchPatientsResponseDto(items, totalCount, request.Page, request.PageSize, counts);
    }

    private static bool IsIdentityNumber(string term) =>
        term.Length == 12 && term.All(char.IsAsciiDigit);

    private static string NormalizeTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
            return TabAll;

        var key = AppointmentBookingRules.NormalizeStatusKey(tab);
        return key is TabAll or TabHasAppointment or TabCheckedIn or TabNoAppointment
            ? key
            : throw new ValidationException("Tab không hợp lệ. Giá trị hợp lệ: ALL, HAS_APPOINTMENT, CHECKED_IN, NO_APPOINTMENT.");
    }
}
