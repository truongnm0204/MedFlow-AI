namespace MedFlow.Application.DTOs.Services;

public class SearchExaminationServicesRequest
{
    public string? Query { get; set; }
    public string? Category { get; set; } = "all";
    public string? Department { get; set; } = "all";
    public string? PriceDurationRange { get; set; } = "all";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 6;
}
