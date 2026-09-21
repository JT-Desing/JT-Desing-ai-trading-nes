using System.Threading;
using System.Threading.Tasks;

namespace NesTrading.Core.Agents.Providers
{
    public interface IAgentTextProvider
    {
        Task<ProviderCompletion> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
    }
}
