using System.Globalization;

namespace MedFlow.Application.Services.Realtime;

public static class RealtimeGroupNames
{
    public static string VisitPayment(long visitId) =>
        $"visit:{visitId.ToString(CultureInfo.InvariantCulture)}";

    public static string AppointmentQueue(long appointmentId) =>
        $"appointment:{appointmentId.ToString(CultureInfo.InvariantCulture)}";

    public static string PatientBucketServicePoint(long servicePointId) =>
        $"patient-bucket:sp:{servicePointId.ToString(CultureInfo.InvariantCulture)}";

    public static string PatientBucketRoom(long roomId) =>
        $"patient-bucket:room:{roomId.ToString(CultureInfo.InvariantCulture)}";

    public static string HospitalQueue(long hospitalId) =>
        $"hospital:{hospitalId.ToString(CultureInfo.InvariantCulture)}:queue";

    public static string HospitalServicePoint(long hospitalId, long servicePointId) =>
        $"hospital:{hospitalId.ToString(CultureInfo.InvariantCulture)}:sp:{servicePointId.ToString(CultureInfo.InvariantCulture)}";

    public static string HospitalRoom(long hospitalId, long roomId) =>
        $"hospital:{hospitalId.ToString(CultureInfo.InvariantCulture)}:room:{roomId.ToString(CultureInfo.InvariantCulture)}";
}
