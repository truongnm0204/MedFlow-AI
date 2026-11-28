using System.Text.Json;
using MedFlow.Application.DTOs.Payments;

namespace MedFlow.UnitTests.Payments;

/// <summary>
/// Issue #863 (S1) — D2 / BR-BF02-06: không còn nhận lựa chọn chỉ định CLS từ client ở cả Cash và QR.
/// </summary>
public sealed class PaymentEnrollmentRequestContractTests
{
    private const string LegacyEnrollmentJson =
        """{"visitChargeIds":[11],"idempotencyKey":"key-863","enrollDiagnosticOrderId":400,"enrollDiagnosticOrderCode":"DIA-400"}""";

    [Fact]
    public void CashRequest_LegacyEnrollFieldsAreIgnored()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var cash = JsonSerializer.Deserialize<CollectCashPaymentRequestDto>(LegacyEnrollmentJson, options)!;

        Assert.Equal([11L], cash.VisitChargeIds!);
        Assert.Equal("key-863", cash.IdempotencyKey);
    }

    [Fact]
    public void QrRequest_LegacyEnrollFieldsAreIgnored()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var qr = JsonSerializer.Deserialize<GenerateQrPaymentRequestDto>(LegacyEnrollmentJson, options)!;

        Assert.Equal([11L], qr.VisitChargeIds!);
        Assert.Equal("key-863", qr.IdempotencyKey);
    }

    [Fact]
    public void PaymentRequestDtos_NoLongerExposeClientEnrollmentSelection()
    {
        Assert.Null(typeof(CollectCashPaymentRequestDto).GetProperty("EnrollDiagnosticOrderId"));
        Assert.Null(typeof(CollectCashPaymentRequestDto).GetProperty("EnrollDiagnosticOrderCode"));
        Assert.Null(typeof(GenerateQrPaymentRequestDto).GetProperty("EnrollDiagnosticOrderId"));
        Assert.Null(typeof(GenerateQrPaymentRequestDto).GetProperty("EnrollDiagnosticOrderCode"));
    }
}
