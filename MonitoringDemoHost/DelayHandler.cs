using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class DelayHandler : IHandleMessages<object>
{
    static readonly ILog Log = LogManager.GetLogger<DelayHandler>();
    readonly Random Random = new Random();
    readonly int max;

    public DelayHandler()
    {
        max = Random.Next(2000);
    }

    public Task Handle(object message, IMessageHandlerContext context)
    {
        int duration;

        lock (Random)
        {
            duration = Random.Next(0, max);
        }

        Log.InfoFormat("Delaying for {0}ms", duration);

        return Task.Delay(duration);
    }
}