using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class Program
{
    static Guid NamespaceIdentifier = GuidUtility.Create(Guid.Empty, "monitoringdemohost");
    static TimeSpan MetricsReportingInterval = TimeSpan.FromSeconds(2.5);
    static int EndpointSize = 25;
    static int InstanceModulo = 4;
    static int RecoverabilityImmediateRetryCount = 1;
    static int RecoverabilityDelayedRetryCount = 5;
    static TimeSpan RecoverabilityDelayedRetryBackoffIncrement = TimeSpan.FromSeconds(1);
    static bool AuditForwardingEnabled = false;
    static bool UseRandomHostId = false;
    static LogLevel LogLevel = LogLevel.Error;
    static TimeSpan HeartbeatInterval = MetricsReportingInterval;
    static TimeSpan HeartbeatTTL = TimeSpan.FromTicks(HeartbeatInterval.Ticks * 4);
    static string HeartbeatQueue = "Particular.ServiceControl";
    static string MonitoringQueue = "Particular.Monitoring";

    public static (string name, IEndpointInstance instance)[] Instances;
    static async Task Main()
    {
        if (Environment.UserInteractive) Console.Title = "Monitoring demo host";

        LogManager.Use<DefaultFactory>().Level(LogLevel);

        var providers = new[] { "Payment", "Audit", "Approval", "Ledger", "Integration", "Broadcast", "Notification", "Identity", "Report", "Exchange" };
        var types = new[] { "Service", "Gateway", "Adapter", "Translator", "Provider", "Generator" };
        var ou = new[] { "Sales",  "Billing", "Shipping", "Transport", "Accounting", "Marketing", "Support", "Development", "Research", "HumanResourceManagement", "Production", "Purchasing" };

        var random = new Random(1337); // Makes sure that random endpoints names are repeatable

        WL("Creating instances");
        var start = Stopwatch.StartNew();
        var tasks = new List<Task<(string name, IEndpointInstance)>>();
        for (int i = 0; i < EndpointSize; ++i)
        {
            var endpointName = $"{ou[random.Next(ou.Length)]}.{providers[random.Next(providers.Length)]}{types[random.Next(types.Length)]}";
            W($"\tInitializing {endpointName}");
            for (int j = 0; j < i % InstanceModulo + 1; j++)
            {
                W(".");
                tasks.Add(Create(endpointName, j));
            }
            WL();
        }

        WL("Starting...");
        Instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        WL($"Done! Took {start.Elapsed} to start {Instances.Length} instances.");
        WL("Press ESC to exit...");

        while (Console.ReadKey().Key != ConsoleKey.Escape)
        {
        }

        WL("Stopping...");
        await Task.WhenAll(Instances.Select(x => x.instance.Stop())).ConfigureAwait(false);
        WL("Stopped!");
    }

    static async Task<(string name, IEndpointInstance)> Create(string endpointName, int instanceNr)
    {
        var instanceSuffix = instanceNr.ToString("000");
        var cfg = new EndpointConfiguration(endpointName);
        //cfg.MakeInstanceUniquelyAddressable(instanceSuffix);
        if (Debugger.IsAttached) cfg.EnableInstallers();
        if (AuditForwardingEnabled) cfg.ForwardReceivedMessagesTo("audit");
        cfg.UsePersistence<InMemoryPersistence>();
        cfg.SendFailedMessagesTo("error");
        cfg.Recoverability()
            .Immediate(c => c.NumberOfRetries(RecoverabilityImmediateRetryCount))
            .Delayed(c => c.NumberOfRetries(RecoverabilityDelayedRetryCount).TimeIncrease(RecoverabilityDelayedRetryBackoffIncrement));

        cfg.RegisterComponents(x => x.ConfigureComponent<DelayHandler>(DependencyLifecycle.SingleInstance));
        cfg.RegisterComponents(x => x.ConfigureComponent<ChaosHandler>(DependencyLifecycle.SingleInstance));
        cfg.DefineCriticalErrorAction(ctx =>
        {
            Environment.FailFast("NServiceBus CriticalError", ctx.Exception);
            return Task.CompletedTask;
        });
        cfg.LimitMessageProcessingConcurrencyTo(4 * Environment.ProcessorCount);

        var transport = cfg.UseTransport<RabbitMQTransport>();
        transport.ConnectionString("host=localhost");
        transport.DelayedDelivery().DisableTimeoutManager();
        var pipeline = cfg.Pipeline;
        pipeline.Register(
            behavior: new RandomMessageTypeIncoming(),
            description: nameof(RandomMessageTypeIncoming));

        pipeline.Register(
            //behavior: new RateLimitBehavior(ThreadLocalRandom.Next(1, 10) * 100, TimeSpan.FromSeconds(10)),
            behavior: new RateLimitBehavior(100, TimeSpan.FromSeconds(10)),
            description: nameof(RateLimitBehavior));

        var hostId = UseRandomHostId
            ? Guid.NewGuid()
            : GuidUtility.Create(NamespaceIdentifier, $"{endpointName}-{instanceSuffix}");

#pragma warning disable CS0618 // Type or member is obsolete
        cfg.EnableCriticalTimePerformanceCounter();
#pragma warning restore CS0618 // Type or member is obsolete
        cfg.UniquelyIdentifyRunningInstance().UsingCustomIdentifier(hostId);

#pragma warning disable 618

        string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        var fakeHostName = Dns.GetHostName() + instanceSuffix;
        fakeHostName += "." + domainName;
        var instanceId = endpointName + "@" + fakeHostName;

        var metrics = cfg.EnableMetrics();
        metrics.SendMetricDataToServiceControl(
            MonitoringQueue,
            MetricsReportingInterval
            , instanceId
            );
#pragma warning restore 618

        //metrics.RegisterObservers(x =>
        //{
        //    foreach (var s in x.Signals)
        //    {
        //        switch (s.Name)
        //        {
        //            case "# of msgs failures / sec":
        //                s.Register((ref SignalEvent @event) => W("f"));
        //                break;
        //            case "# of msgs successfully processed / sec":
        //                s.Register((ref SignalEvent @event) => W("."));
        //                break;
        //        }
        //    }
        //});

        cfg.HeartbeatPlugin(HeartbeatQueue, HeartbeatInterval, HeartbeatTTL);

        cfg.PurgeOnStartup(true);
        return (endpointName, await Endpoint.Start(cfg).ConfigureAwait(false));
    }

    static void WL(string value = null)
    {
        Console.Out.WriteLineAsync(value).ConfigureAwait(false);
    }
    static void W(string value = null)
    {
        Console.Out.WriteAsync(value).ConfigureAwait(false);
    }
}
