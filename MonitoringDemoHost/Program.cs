﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
        var ou = new[] { "Sales", "Transport", "Accounting", "Marketing", "Support", "Development", "Research", "HumanResourceManagement", "Production", "Purchasing" };

        var random = new Random(1337); // Makes sure that random endpoints names are repeatable

        Console.WriteLine("Creating instances");
        var start = Stopwatch.StartNew();
        var tasks = new List<Task<(string name, IEndpointInstance)>>();
        for (int i = 0; i < EndpointSize; ++i)
        {
            var endpointName = $"$ParticularLabs.{ou[random.Next(ou.Length)]}.{providers[random.Next(providers.Length)]}{types[random.Next(types.Length)]}";
            await Console.Out.WriteAsync($"\tInitializing {endpointName}").ConfigureAwait(false);
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

        var hostId = UseRandomHostId
            ? Guid.NewGuid()
            : GuidUtility.Create(NamespaceIdentifier, $"{endpointName}-{instanceSuffix}");

#pragma warning disable CS0618 // Type or member is obsolete
        cfg.EnableCriticalTimePerformanceCounter();
#pragma warning restore CS0618 // Type or member is obsolete
        cfg.UniquelyIdentifyRunningInstance().UsingCustomIdentifier(hostId);

#pragma warning disable 618

        var fakeHostName = Dns.GetHostName() + instanceSuffix;
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
        //                s.Register((ref SignalEvent @event) => Console.Out.WriteAsync("f"));
        //                break;
        //            case "# of msgs successfully processed / sec":
        //                s.Register((ref SignalEvent @event) => Console.Out.WriteAsync("."));
        //                break;
        //        }
        //    }
        //});

        cfg.HeartbeatPlugin(HeartbeatQueue, HeartbeatInterval, HeartbeatTTL);

        cfg.PurgeOnStartup(true);
        return (endpointName, await Endpoint.Start(cfg).ConfigureAwait(false));
    }
}
