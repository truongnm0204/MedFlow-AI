namespace MedFlow.Application.DTOs.Reception;

public sealed class SearchPatientsRequest
{
    public string? Query { get; set; }
    public string? Tab { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
