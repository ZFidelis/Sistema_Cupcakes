using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Services;

public sealed class SimulatedPaymentService
{
    public bool Approve(SimulatedPaymentMethod method, string scenario)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        return scenario switch
        {
            "Aprovar" => true,
            "Recusar" => false,
            _ => throw new ArgumentException("Escolha Aprovar ou Recusar na simulacao.", nameof(scenario))
        };
    }
}
