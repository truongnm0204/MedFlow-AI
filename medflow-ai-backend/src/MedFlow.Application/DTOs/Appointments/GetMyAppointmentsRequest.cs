namespace MedFlow.Application.DTOs.Appointments;

public class GetMyAppointmentsRequest
{
    public string? MemberId { get; set; }
    public string? Tab { get; set; }
    public string? Keyword { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
