using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using SelfTest.Messages;

namespace SelfTest
{
    class SelfTestFeature : Feature
    {
        public SelfTestFeature()
        {
            EnableByDefault();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.RegisterStartupTask(new MyStartupTask());
        }
    }

    class MyStartupTask : FeatureStartupTask
    {
        public static long a;
        public static long b;
        private static long count;
        private readonly long current = ++count;
        Task loop;
        bool stop;

        protected override async Task OnStart(IMessageSession session)
        {
            loop = Task.Run(() => Loop(session));
            await Console.Out.WriteAsync($"{current}: Started").ConfigureAwait(false);
        }

        static readonly TimeSpan SelfTestDelayBase = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["SelfTest/DelayBase"], CultureInfo.InvariantCulture);
        static readonly TimeSpan SelfTestDelayMin = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["SelfTest/DelayMin"], CultureInfo.InvariantCulture);
        static readonly TimeSpan SelfTestDelayMax = TimeSpan.Parse(System.Configuration.ConfigurationManager.AppSettings["SelfTest/DelayMax"], CultureInfo.InvariantCulture);


        async Task Loop(IMessageSession session)
        {
            var min = (int)SelfTestDelayBase.TotalMilliseconds + ThreadLocalRandom.Next((int)SelfTestDelayMin.TotalMilliseconds);
            var max = ThreadLocalRandom.Next(min, (int)SelfTestDelayMax.TotalMilliseconds);

            await Console.Out.WriteLineAsync($"{current}: {min}-{max},").ConfigureAwait(false);

            var gate = new RateGate(10, TimeSpan.FromSeconds(1));
            while (!stop)
            {
                await gate.WaitToProceed()
                    .ConfigureAwait(false);
                Interlocked.Increment(ref a);
                var send = Task.Run(async () =>
                {
                    try
                    {
                        await session.SendLocal(new Ping())
                            .ConfigureAwait(false);
                        Interlocked.Increment(ref b);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                });
            }
        }

        protected override Task OnStop(IMessageSession session)
        {
            stop = true;
            return loop;
        }

    }

    class Ping :
        //MyBase, MyMessage,
        IMessage
    {
    }

    class PingHandler : IHandleMessages<Ping>
    {
        static readonly ILog Log = LogManager.GetLogger(nameof(SelfTestFeature));

        public Task Handle(Ping message, IMessageHandlerContext context)
        {
            return Task.CompletedTask;
        }
    }

}
namespace SelfTest.Messages
{
    public class MyBase : MyMessage
    {
    }

    public interface MyMessage
    {
    }

}
