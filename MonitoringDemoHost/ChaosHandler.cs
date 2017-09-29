using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class ChaosHandler : IHandleMessages<object>
{
    readonly ILog Log = LogManager.GetLogger<ChaosHandler>();
    static readonly Random Random = new Random();
    readonly double Thresshold;

    public ChaosHandler()
    {
        lock (Random)
        {
            Thresshold = Random.NextDouble() * 0.50;
        }
    }

    public Task Handle(object message, IMessageHandlerContext context)
    {
        double result;

        lock (Random)
        {
            result = Random.NextDouble();
        }

        if (result < Thresshold) throw new Exception($"Random chaos ({Thresshold * 100:N}% failure)");
        return Task.FromResult(0);
    }
}
