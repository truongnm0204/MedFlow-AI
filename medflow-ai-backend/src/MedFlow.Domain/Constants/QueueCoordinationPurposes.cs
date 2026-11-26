namespace MedFlow.Domain.Constants;

public static class QueueCoordinationPurposes
{
    public const string ResultConclusion = "RESULT_CONCLUSION";
    public const string ClinicalReview = "CLINICAL_REVIEW";

    public static bool IsValid(string? value)
        => value is ResultConclusion or ClinicalReview;
}
