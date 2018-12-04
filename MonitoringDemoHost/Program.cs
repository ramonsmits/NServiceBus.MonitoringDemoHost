using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;
using SelfTest;
using SelfTest.Messages;

class Program
{
    private static NameValueCollection AppSettings = ConfigurationManager.AppSettings;
    static Guid NamespaceIdentifier = GuidUtility.Create(Guid.Empty, "monitoringdemohost");
    static TimeSpan MetricsReportingInterval = TimeSpan.FromSeconds(0.5);
    static int EndpointSize = int.Parse(AppSettings["EndpointSize"]);
    static int InstanceModulo = int.Parse(AppSettings["InstanceModulo"]);
    static int RecoverabilityImmediateRetryCount = 1;
    static int RecoverabilityDelayedRetryCount = 5;
    static TimeSpan RecoverabilityDelayedRetryBackoffIncrement = TimeSpan.FromSeconds(1);
    static bool AuditForwardingEnabled = bool.TrueString.Equals(AppSettings["NServiceBus/AuditForwardingEnabled"], StringComparison.InvariantCultureIgnoreCase);
    static bool UseRandomHostId = false;
    static LogLevel LogLevel = (LogLevel)Enum.Parse(typeof(LogLevel), AppSettings["NServiceBus/LogLevel"], true);
    static TimeSpan HeartbeatInterval = MetricsReportingInterval;
    static TimeSpan HeartbeatTTL = TimeSpan.FromTicks(HeartbeatInterval.Ticks * 4);
    static string HeartbeatQueue = "Particular.ServiceControl";
    static string MonitoringQueue = "Particular.Monitoring";

    public static (string name, IEndpointInstance instance)[] Instances;
    static async Task Main()
    {
        InitAppDomainEventLogging();

        GCSettings.LatencyMode = GCLatencyMode.Batch;
        if (Environment.UserInteractive) Console.Title = "Monitoring demo host";

        LogManager.Use<DefaultFactory>().Level(LogLevel);

        var providers = new[] { "Payment", "Audit", "Approval", "Ledger", "Integration", "Broadcast", "Notification", "Identity", "Report", "Exchange" };
        var types = new[] { "Service", "Gateway", "Adapter", "Translator", "Provider", "Generator" };
        var ou = new[] { "Sales", "Billing", "Shipping", "Transport", "Accounting", "Marketing", "Support", "Development", "Research", "HumanResourceManagement", "Production", "Purchasing" };

        var random = new Random(1337); // Makes sure that random endpoints names are repeatable

        WL("Creating instances");
        var start = Stopwatch.StartNew();
        var tasks = new List<Task<(string name, IEndpointInstance)>>();
        for (int i = 0; i < EndpointSize; ++i)
        {
            var endpointName = $"monitoringdemo.{ou[random.Next(ou.Length)]}.{providers[random.Next(providers.Length)]}{types[random.Next(types.Length)]}";
            W($"\tInitializing {endpointName}");
            for (int j = 0; j < i % InstanceModulo + 1; j++)
            {
                W(".");
                tasks.Add(Create(endpointName, j));
            }
            WL();
        }

        UpdateConsoleTitle();

        WL("Starting...");
        Instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        WL($"Done! Took {start.Elapsed} to start {Instances.Length} instances.");
        WL("Press ESC to exit...");

        while (Console.ReadKey().Key != ConsoleKey.Escape)
        {
            await Instances[0].instance.SendLocal(new SelfTest.Ping()).ConfigureAwait(false);
        }

        WL("Stopping...");
        await Task.WhenAll(Instances.Select(x => x.instance.Stop())).ConfigureAwait(false);
        WL("Stopped!");
    }

     static async void UpdateConsoleTitle()
    {
        while (true)
        {
            Console.Title = $"{MyStartupTask.a:N0} = {MyStartupTask.b:N0}";
            await Task.Delay(1000);
        }
    }

    static async Task<(string name, IEndpointInstance)> Create(string endpointName, int instanceNr)
    {
        var instanceSuffix = instanceNr.ToString("000");
        var cfg = new EndpointConfiguration(endpointName);
        //cfg.MakeInstanceUniquelyAddressable(instanceSuffix);
        cfg.EnableInstallers();

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
        cfg.LimitMessageProcessingConcurrencyTo(int.Parse(ConfigurationManager.AppSettings["NServiceBus/MaxConcurrency"], CultureInfo.InvariantCulture));

        //cfg.PurgeOnStartup(true);

        var transportValue = System.Configuration.ConfigurationManager.AppSettings["Transport"];
        Console.WriteLine(transportValue);
        switch (transportValue)
        {
            case nameof(SqlServerTransport):
                var sql = cfg.UseTransport<SqlServerTransport>();
                sql.ConnectionStringName("Transport/SqlServer");
                cfg.AssemblyScanner().ExcludeAssemblies("NServiceBus.Transports.RabbitMQ");
                break;
            case nameof(MsmqTransport):
                var msmq = cfg.UseTransport<MsmqTransport>();
                msmq.Transactions(TransportTransactionMode.ReceiveOnly);
                //msmq.ConnectionStringName("Transport/MSMQ");

                msmq.DisableDeadLetterQueueing();
                //msmq.DisableConnectionCachingForSends();
                ///msmq.UseNonTransactionalQueues();
                //msmq.EnableJournaling();
                //msmq.TimeToReachQueue(timespanValue);

                cfg.AssemblyScanner().ExcludeAssemblies("NServiceBus.Transports.RabbitMQ");
                break;
            case nameof(RabbitMQTransport):
                var transport = cfg.UseTransport<RabbitMQTransport>();
                transport.ConnectionStringName("Transport/RabbitMQ");
                //transport.DelayedDelivery().DisableTimeoutManager();
                break;
            default:
                throw new NotSupportedException($"Unknown transport option '{transportValue}'");
        }




        var pipeline = cfg.Pipeline;
        pipeline.Register(behavior: new RandomMessageTypeIncoming(), description: nameof(RandomMessageTypeIncoming));

        //cfg.ApplyRateLimiting(500, TimeSpan.FromSeconds(10));

        var hostId = UseRandomHostId
            ? Guid.NewGuid()
            : GuidUtility.Create(NamespaceIdentifier, $"{endpointName}-{instanceSuffix}");

        string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        var fakeHostName = Dns.GetHostName() + instanceSuffix;
        fakeHostName += "." + domainName;
        var displayName = endpointName + "@" + fakeHostName;

#pragma warning disable CS0618 // Type or member is obsolete
        var performanceCounters = cfg.EnableWindowsPerformanceCounters();
        performanceCounters.EnableSLAPerformanceCounters(TimeSpan.FromMinutes(1));
#pragma warning restore CS0618 // Type or member is obsolete
        var identification = cfg.UniquelyIdentifyRunningInstance();
        //identification.UsingCustomIdentifier(hostId);
        identification.UsingCustomDisplayName(displayName);

#pragma warning disable 618


        var metrics = cfg.EnableMetrics();
        metrics.SendMetricDataToServiceControl(
            MonitoringQueue,
            MetricsReportingInterval
            //, displayName
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

        cfg.SendHeartbeatTo(HeartbeatQueue, HeartbeatInterval, HeartbeatTTL);

        //cfg.PurgeOnStartup(true);
        //cfg.Conventions().DefiningMessagesAs(x => typeof(MyMessage).IsAssignableFrom(x));
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

    static void InitAppDomainEventLogging()
    {
        var firstChanceLog = LogManager.GetLogger("FirstChanceException");
        var unhandledLog = LogManager.GetLogger("UnhandledException");
        var domain = AppDomain.CurrentDomain;

        domain.FirstChanceException += (o, ea) => { firstChanceLog.Debug(ea.Exception.Message, ea.Exception); };
        domain.UnhandledException += (o, ea) =>
        {
            if (ea.ExceptionObject is Exception exception)
            {
                unhandledLog.Error(exception.Message, exception);
            }
        };
    }
}
