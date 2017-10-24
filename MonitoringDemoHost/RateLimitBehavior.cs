using System;
using System.Threading.Tasks;
using NServiceBus.Pipeline;

public class RateLimitBehavior : IBehavior<ITransportReceiveContext, ITransportReceiveContext>
{
    readonly RateGate Gate;

    public RateLimitBehavior(int occurences) : this(1, TimeSpan.FromSeconds(1.0 / occurences))
    {
    }

    public RateLimitBehavior(int occurences, TimeSpan duration)
    {
        Gate = new RateGate(occurences, duration);
    }

    public async Task Invoke(ITransportReceiveContext context, Func<ITransportReceiveContext, Task> next)
    {
        await Gate.WaitToProceed().ConfigureAwait(false);
        await next(context).ConfigureAwait(false);
    }
}