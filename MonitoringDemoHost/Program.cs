using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    static bool AuditForwardingEnabled = true;
    static bool UseRandomHostId = false;
    static LogLevel LogLevel = LogLevel.Error;
    static TimeSpan HeartbeatInterval = MetricsReportingInterval;
    static TimeSpan HeartbeatTTL = TimeSpan.FromTicks(HeartbeatInterval.Ticks* 4);
    static string HeartbeatQueue = "Particular.ServiceControl";
    static string MonitoringQueue = HeartbeatQueue + ".Monitoring";

    public static (string name, IEndpointInstance instance)[] Instances;
    static async Task Main()
    {
        if (Environment.UserInteractive) Console.Title = "Monitoring demo host";

        LogManager.Use<DefaultFactory>().Level(LogLevel);

        var providers = new[] { "Payment", "Audit", "Approval", "Ledger", "Integration", "Broadcast", "Notification", "Identity", "Report", "Exchange" };
        var types = new[] { "Service", "Gateway", "Adapter", "Translator", "Provider", "Generator" };
        var ou = new[] { "Sales", "Transport", "Accounting", "Marketing", "Support", "Development", "Research", "HumanResourceManagement", "Production", "Purchasing" };

        var random = new Random(1337); // Makes sure that random endpoints names are repeatable

        Console.WriteLine("Creating instances");
        var start = Stopwatch.StartNew();
        var tasks = new List<Task<(string name, IEndpointInstance)>>();
        for (int i = 0; i < EndpointSize; ++i)
        {
            var endpointName = $"$ParticularLabs.{ou[random.Next(ou.Length)]}.{providers[random.Next(providers.Length)]}{types[random.Next(types.Length)]}";
            await Console.Out.WriteAsync($"Initializing {endpointName}").ConfigureAwait(false);
            for (int j = 0; j < i % InstanceModulo + 1; j++)
            {
                await Console.Out.WriteAsync(".").ConfigureAwait(false);
                tasks.Add(Create(endpointName, j));
            }
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
        }

        await Console.Out.WriteAsync("Starting...").ConfigureAwait(false);
        Instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Done! Took {start.Elapsed} to start {Instances.Length} instances.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Press ESC to exit...").ConfigureAwait(false);

        while (Console.ReadKey().Key != ConsoleKey.Escape)
        {
        }

        await Console.Out.WriteLineAsync("Stopping...").ConfigureAwait(false);
        await Task.WhenAll(Instances.Select(x => x.instance.Stop())).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Stopped!").ConfigureAwait(false);
    }

    static async Task<(string name, IEndpointInstance)> Create(string name, int instance)
    {
        await Task.Yield();
        var instanceSuffix = instance.ToString("000");
        var cfg = new EndpointConfiguration(name);
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


        var transport = cfg.UseTransport<MsmqTransport>();
        transport.Transactions(TransportTransactionMode.SendsAtomicWithReceive); // Lower transaction mode to prevent transaction issues with MSDTC.
        transport.ApplyLabelToMessages(headers => (headers.ContainsKey(Headers.EnclosedMessageTypes) ? headers[Headers.EnclosedMessageTypes].Substring(0, Math.Min(200, headers[Headers.EnclosedMessageTypes].Length)) + "@" : string.Empty) + DateTime.UtcNow.ToString("O"));

        var hostId = UseRandomHostId
            ? Guid.NewGuid()
            : GuidUtility.Create(NamespaceIdentifier, $"{name}-{instanceSuffix}");

#pragma warning disable CS0618 // Type or member is obsolete
        cfg.EnableCriticalTimePerformanceCounter();
#pragma warning restore CS0618 // Type or member is obsolete
        cfg.UniquelyIdentifyRunningInstance().UsingCustomIdentifier(hostId);

#pragma warning disable 618
        cfg.EnableMetrics().SendMetricDataToServiceControl(
            MonitoringQueue,
            MetricsReportingInterval,
            instance.ToString("000")
            );
#pragma warning restore 618

        cfg.HeartbeatPlugin(HeartbeatQueue, HeartbeatInterval, HeartbeatTTL);

        return (name, await Endpoint.Start(cfg).ConfigureAwait(false));
    }
}
