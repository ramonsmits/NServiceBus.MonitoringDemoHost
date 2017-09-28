using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;

class Program
{
    public static Tuple<string, IEndpointInstance>[] Instances;
    static async Task Main()
    {
        LogManager.Use<DefaultFactory>().Level(LogLevel.Error);
        var size = 25;

        Console.WriteLine("Creating instances");
        var tasks = new List<Task<Tuple<string, IEndpointInstance>>>();
        for (int i = 0; i < size; ++i)
        {
            for (int j = 0; j < i % 4 + 1; j++)
            {
                tasks.Add(Create($"labs.{i:000}", j));
            }
        }

        await Console.Out.WriteLineAsync("Starting...").ConfigureAwait(false);
        Instances = await Task.WhenAll(tasks).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Press ESC to exit...").ConfigureAwait(false);

        while (Console.ReadKey().Key != ConsoleKey.Escape)
        {
        }

        await Console.Out.WriteLineAsync("Stopping...").ConfigureAwait(false);
        await Task.WhenAll(Instances.Select(x => x.Item2.Stop())).ConfigureAwait(false);
        await Console.Out.WriteLineAsync("Stopped!").ConfigureAwait(false);
    }

    static async Task<Tuple<string, IEndpointInstance>> Create(string name, int instance)
    {
        await Task.Yield();
        var cfg = new EndpointConfiguration(name);
        //cfg.MakeInstanceUniquelyAddressable(instance.ToString("000"));
        if(Debugger.IsAttached) cfg.EnableInstallers();
        //cfg.ForwardReceivedMessagesTo("audit");
        cfg.UsePersistence<InMemoryPersistence>();
        cfg.SendFailedMessagesTo("error");

        cfg.Recoverability()
            .Immediate(c => c.NumberOfRetries(1))
            .Delayed(c => c.NumberOfRetries(5).TimeIncrease(TimeSpan.FromSeconds(1)));

        cfg.RegisterComponents(x => x.ConfigureComponent<DelayHandler>(DependencyLifecycle.SingleInstance));
        cfg.RegisterComponents(x => x.ConfigureComponent<ChaosHandler>(DependencyLifecycle.SingleInstance));
        cfg.DefineCriticalErrorAction(ctx =>
        {
            Environment.FailFast("NServiceBus CriticalError", ctx.Exception);
            return Task.CompletedTask;
        });
        cfg.LimitMessageProcessingConcurrencyTo(4 * Environment.ProcessorCount);
        var transport = cfg.UseTransport<MsmqTransport>();
        transport.Transactions(TransportTransactionMode.SendsAtomicWithReceive);
        transport.ApplyLabelToMessages(headers => (headers.ContainsKey(Headers.EnclosedMessageTypes) ? headers[Headers.EnclosedMessageTypes].Substring(0, Math.Min(200, headers[Headers.EnclosedMessageTypes].Length)) + "@" : string.Empty) + DateTime.UtcNow.ToString("O"));

#pragma warning disable 618
        cfg.EnableMetrics().SendMetricDataToServiceControl(
            "Particular.ServiceControl.Monitoring",
            TimeSpan.FromSeconds(2.5),
            instance.ToString("000")
            );
#pragma warning restore 618
        return Tuple.Create(name, await Endpoint.Start(cfg).ConfigureAwait(false));
    }
}
