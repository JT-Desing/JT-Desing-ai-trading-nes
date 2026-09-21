using System;
using System.Threading;
using System.Threading.Tasks;

namespace NesTrading.Core.Agents.Providers
{
    public static class AgentProviderExecutor
    {
        public static async Task<ProviderCompletion> CompleteAsync(
            IAgentTextProvider provider,
            ProviderRequest request,
            CancellationToken cancellationToken)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (request == null) throw new ArgumentNullException(nameof(request));

            var started = DateTime.UtcNow;

            if (cancellationToken.IsCancellationRequested)
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.Cancelled,
                    "unknown",
                    request.Model,
                    started,
                    DateTime.UtcNow);

            var remaining = request.DeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.TimedOut,
                    "unknown",
                    request.Model,
                    started,
                    DateTime.UtcNow);

            try
            {
                var providerTask = provider.CompleteAsync(request, cancellationToken);
                var timeoutTask = Task.Delay(remaining, cancellationToken);
                var winner = await Task.WhenAny(providerTask, timeoutTask).ConfigureAwait(false);

                if (winner != providerTask)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return ProviderCompletion.FromStatus(
                            ProviderCompletionStatus.Cancelled,
                            "unknown",
                            request.Model,
                            started,
                            DateTime.UtcNow);

                    return ProviderCompletion.FromStatus(
                        ProviderCompletionStatus.TimedOut,
                        "unknown",
                        request.Model,
                        started,
                        DateTime.UtcNow);
                }

                return await providerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ProviderCompletion.FromStatus(
                    cancellationToken.IsCancellationRequested
                        ? ProviderCompletionStatus.Cancelled
                        : ProviderCompletionStatus.TimedOut,
                    "unknown",
                    request.Model,
                    started,
                    DateTime.UtcNow);
            }
            catch
            {
                return ProviderCompletion.FromStatus(
                    ProviderCompletionStatus.Failed,
                    "unknown",
                    request.Model,
                    started,
                    DateTime.UtcNow);
            }
        }
    }
}
