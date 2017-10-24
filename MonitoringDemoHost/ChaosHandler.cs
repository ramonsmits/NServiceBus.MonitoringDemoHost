using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class ChaosHandler : IHandleMessages<object>
{
    readonly ILog Log = LogManager.GetLogger<ChaosHandler>();
    readonly ConcurrentDictionary<string, double> Thressholds = new ConcurrentDictionary<string, double>();

    public Task Handle(object message, IMessageHandlerContext context)
    {
        var enclosedMessageTypes = context.MessageHeaders[Headers.EnclosedMessageTypes];
        var thresshold = Thressholds.GetOrAdd(enclosedMessageTypes, k => ThreadLocalRandom.NextDouble() * 0.05);
        var result = ThreadLocalRandom.NextDouble();
        if (result < thresshold) throw new InvalidOperationException($"Random chaos ({thresshold * 100:N}% failure)");
        return Task.FromResult(0);
    }
}
