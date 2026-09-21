using System;
using System.Threading;
using System.Threading.Tasks;

namespace NesTrading.Core.Agents.Providers
{
    public sealed class FakeAgentTextProvider : IAgentTextProvider
    {
        private readonly ProviderCompletion _completion;
        private readonly TimeSpan _delay;
        private readonly Exception _exception;

        private FakeAgentTextProvider(ProviderCompletion completion, TimeSpan delay, Exception exception)
        {
            _completion = completion;
            _delay = delay;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public static FakeAgentTextProvider Return(ProviderCompletion completion) =>
            new FakeAgentTextProvider(completion, TimeSpan.Zero, null);

        public static FakeAgentTextProvider Delayed(ProviderCompletion completion, TimeSpan delay)
        {
            if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
            return new FakeAgentTextProvider(completion, delay, null);
        }

        public static FakeAgentTextProvider Throw(Exception exception) =>
            new FakeAgentTextProvider(null, TimeSpan.Zero, exception ?? throw new ArgumentNullException(nameof(exception)));

        public async Task<ProviderCompletion> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            CallCount++;

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (_exception != null)
                throw _exception;

            return _completion;
        }
    }
}
