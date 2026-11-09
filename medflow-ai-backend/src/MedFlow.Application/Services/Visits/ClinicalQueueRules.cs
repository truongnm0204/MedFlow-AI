using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Visits;

internal static class ClinicalQueueRules
{
    public const string PriorityNormal = "NORMAL";
    public const string PriorityElderly = "ELDERLY";
    public const string PriorityPregnant = "PREGNANT";
    public const string QueueTypeExamination = "EXAMINATION";
    public const string QueueTypeReturnConsultation = "RETURN_CONSULTATION";
    public const int ElderlyMinimumAge = 75;
    public const string InvalidPriorityMessage = "Mức độ ưu tiên không hợp lệ. Chỉ chấp nhận: NORMAL, ELDERLY, PREGNANT.";

    public static readonly IReadOnlySet<string> ValidPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PriorityNormal,
        PriorityElderly,
        PriorityPregnant,
    };

    public static readonly IReadOnlySet<string> ValidQueueTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        QueueTypeExamination,
        QueueTypeReturnConsultation,
    };

    public static string? NormalizePriorityOrThrow(string? rawPriority)
    {
        if (string.IsNullOrWhiteSpace(rawPriority))
            return null;

        var normalized = AppointmentBookingRules.NormalizeStatusKey(rawPriority);
        if (!ValidPriorities.Contains(normalized))
            throw new ValidationException(InvalidPriorityMessage);

        return normalized;
    }

    public static string NormalizeStoredPriority(string? storedPriority)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(storedPriority);
        return ValidPriorities.Contains(normalized) ? normalized : PriorityNormal;
    }

    public static bool IsElevated(string priority)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(priority);
        return normalized is PriorityElderly or PriorityPregnant;
    }

    public static int? CalculateAge(DateOnly? dateOfBirth, DateOnly referenceDate)
    {
        if (dateOfBirth is null)
            return null;

        var dob = dateOfBirth.Value;
        var age = referenceDate.Year - dob.Year;
        if (referenceDate < dob.AddYears(age))
            age--;

        return Math.Max(0, age);
    }

    public static string ResolveEffectivePriority(string? storedPriority, DateOnly? dateOfBirth, DateOnly referenceDate)
    {
        var normalized = NormalizeStoredPriority(storedPriority);
        if (normalized == PriorityPregnant)
            return PriorityPregnant;

        if (normalized == PriorityElderly)
            return PriorityElderly;

        var age = CalculateAge(dateOfBirth, referenceDate);
        if (age.HasValue && age.Value >= ElderlyMinimumAge)
            return PriorityElderly;

        return PriorityNormal;
    }

    public static bool IsClinicalExaminationQueueType(string? queueType)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(queueType);
        return string.IsNullOrEmpty(normalized) || ValidQueueTypes.Contains(normalized);
    }

    public static string ResolveQueueType(string? storedQueueType, string? visitStatus)
    {
        var normalizedQueueType = AppointmentBookingRules.NormalizeStatusKey(storedQueueType);
        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visitStatus);

        if (normalizedQueueType == QueueTypeReturnConsultation || normalizedVisitStatus == QueueTypeReturnConsultation)
            return QueueTypeReturnConsultation;

        return QueueTypeExamination;
    }

    public static int GetWaitingTier(string effectivePriority, bool isScheduled, string? arrivalClass)
    {
        if (IsElevated(effectivePriority))
            return 0;

        if (isScheduled && AppointmentBookingRules.NormalizeStatusKey(arrivalClass) == "ON_TIME_OR_GRACE")
            return 1;

        return 2;
    }

    public static void EnsurePatientEligible(string targetPriority, Patient? patient, DateOnly referenceDate)
    {
        if (patient is null)
            return;

        var normalizedTarget = AppointmentBookingRules.NormalizeStatusKey(targetPriority);
        if (normalizedTarget == PriorityElderly)
        {
            var age = CalculateAge(patient.DateOfBirth, referenceDate);
            if (age.HasValue && age.Value < ElderlyMinimumAge)
            {
                throw new ValidationException("Người bệnh chưa đủ 75 tuổi theo ngày sinh, không đủ điều kiện ưu tiên người cao tuổi (ELDERLY).");
            }
        }
        else if (normalizedTarget == PriorityPregnant)
        {
            var gender = AppointmentBookingRules.NormalizeStatusKey(patient.Gender);
            if (gender == "MALE")
            {
                throw new ValidationException("Người bệnh có giới tính Nam, không đủ điều kiện ưu tiên phụ nữ mang thai (PREGNANT).");
            }
        }
    }
}
