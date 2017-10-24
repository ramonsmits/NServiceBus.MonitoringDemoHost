﻿using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;

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
        Task loop;
        bool stop;

        protected override Task OnStart(IMessageSession session)
        {
            loop = Task.Run(() => Loop(session));
            return Task.CompletedTask;
        }

        async Task Loop(IMessageSession session)
        {
            var min = ThreadLocalRandom.Next(10);
            var max = ThreadLocalRandom.Next(min, 500);

            while (!stop)
            {
                session.SendLocal(new Ping()).ConfigureAwait(false);
                int delay = ThreadLocalRandom.Next(min, max);
                await Task.Delay(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(false);
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
