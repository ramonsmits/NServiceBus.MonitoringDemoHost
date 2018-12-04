using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class ChaosHandler : IHandleMessages<object>
{
    static readonly double ChaosFactor = double.Parse(System.Configuration.ConfigurationManager.AppSettings["ChaosFactor"], CultureInfo.InvariantCulture);
    readonly ILog Log = LogManager.GetLogger<ChaosHandler>();
    static readonly ConcurrentDictionary<string, double> Thresholds = new ConcurrentDictionary<string, double>();

    public Task Handle(object message, IMessageHandlerContext context)
    {
        var enclosedMessageTypes = context.MessageHeaders[Headers.EnclosedMessageTypes];
        var threshold = Thresholds.GetOrAdd(enclosedMessageTypes, k =>
        {
            var chaosFactor = ThreadLocalRandom.NextDouble() * ChaosFactor;
            Log.InfoFormat($"{0} chaos factor: {1*100:N}%",enclosedMessageTypes,chaosFactor);
            return chaosFactor;
        });
        var result = ThreadLocalRandom.NextDouble();
        if (result < threshold) throw new InvalidOperationException($"Random chaos ({threshold * 100:N}% failure)");
        return Task.FromResult(0);
    }
}
