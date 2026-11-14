using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface ISePayWebhookService
{
    Task<SePayWebhookResult> HandleAsync(SePayWebhookRequest request, CancellationToken ct = default);
}
