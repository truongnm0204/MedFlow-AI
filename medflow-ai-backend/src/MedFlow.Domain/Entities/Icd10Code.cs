namespace MedFlow.Domain.Entities;

public class Icd10Code
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string? Code { get; set; }
    public string? NameVi { get; set; }
    public string? CatalogVersion { get; set; }
    public string? NodeType { get; set; }
    public bool? IsSelectable { get; set; }
    public bool? NotPrimaryDx { get; set; }
}
