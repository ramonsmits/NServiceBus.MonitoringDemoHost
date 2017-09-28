using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;

namespace Store.Shared.SelfTest
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
        Task loop;
        bool stop;
        protected override Task OnStart(IMessageSession session)
        {
            loop = Loop(session);
            return Task.CompletedTask;
        }

        async Task Loop(IMessageSession session)
        {
            var random = new Random();
            var randomMin = random.Next(100);
            var randomMax = random.Next(randomMin, 5000);

            while (!stop)
            {
                await session.SendLocal(new Ping()).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(random.Next(randomMin, randomMax))).ConfigureAwait(false);
            }
        }
        protected override Task OnStop(IMessageSession session)
        {
            stop = true;
            return loop;
        }

    }

    class Ping : IMessage
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
