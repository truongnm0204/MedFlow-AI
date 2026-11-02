namespace MedFlow.Application.DTOs.Doctors;

public class SearchDoctorsRequest
{
    public string? Keyword { get; set; }
    public string? Specialty { get; set; } = "ALL";
    public string? Degree { get; set; } = "ALL";
    public bool HasScheduleToday { get; set; }
    public bool HasScheduleThisWeek { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 6;
}
