using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class ChaosHandler : IHandleMessages<object>
{
    readonly ILog Log = LogManager.GetLogger<ChaosHandler>();
    readonly double Thresshold = ThreadLocalRandom.NextDouble() * 0.50;

    public Task Handle(object message, IMessageHandlerContext context)
    {
        var result = ThreadLocalRandom.NextDouble();
        if (result < Thresshold) throw new Exception($"Random chaos ({Thresshold * 100:N}% failure)");
        return Task.FromResult(0);
    }
}
