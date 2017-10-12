using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class DelayHandler : IHandleMessages<object>
{
    static readonly ILog Log = LogManager.GetLogger<DelayHandler>();
    readonly int max = ThreadLocalRandom.Next(10000);

    public Task Handle(object message, IMessageHandlerContext context)
    {
        int duration = ThreadLocalRandom.Next(0, max);
        return Task.Delay(TimeSpan.FromMilliseconds(duration));
    }
}
