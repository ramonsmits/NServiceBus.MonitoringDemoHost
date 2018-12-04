using System;
using NServiceBus;
using NServiceBus.Metrics.ServiceControl;

public class EnableMonitoring : INeedInitialization
{
    public void Customize(EndpointConfiguration cfg)
    {
        var metrics = cfg.EnableMetrics();
        metrics.SendMetricDataToServiceControl("Particular.Monitoring", TimeSpan.FromSeconds(0.5));
    }
}
