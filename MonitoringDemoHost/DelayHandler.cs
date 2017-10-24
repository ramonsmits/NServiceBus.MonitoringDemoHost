using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class DelayHandler : IHandleMessages<object>
{
    static readonly ILog Log = LogManager.GetLogger<DelayHandler>();
    readonly ConcurrentDictionary<string, int> durations = new ConcurrentDictionary<string, int>();

    public Task Handle(object message, IMessageHandlerContext context)
    {
        var enclosedMessageTypes = context.MessageHeaders[Headers.EnclosedMessageTypes];
        int duration = durations.GetOrAdd(enclosedMessageTypes, k => ThreadLocalRandom.Next(2500));
        return Task.Delay(TimeSpan.FromMilliseconds(duration));
    }
}
