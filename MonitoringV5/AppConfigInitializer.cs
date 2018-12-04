using System.Configuration;
using System.Linq;

namespace NServiceBus.Metrics.ServiceControl
{
    class AppConfigInitializer : INeedInitialization
    {
        const string DefaultQueue = "Particular.Monitoring";
        const string MonitoringDestinationKey = "ServiceControl/Monitoring/Address";

        public void Customize(BusConfiguration configuration)
        {
            var log = NServiceBus.Logging.LogManager.GetLogger<AppConfigInitializer>();
            var appSettings = ConfigurationManager.AppSettings;
            var address = DefaultQueue;
            var exists = appSettings.AllKeys.Contains(MonitoringDestinationKey);

            log.InfoFormat("AppSetting '{0}' exist: {1}", MonitoringDestinationKey, exists);

            if (exists) address = appSettings[MonitoringDestinationKey];

            log.InfoFormat("Service control monitoring address: {0} ", address);
            configuration.SendMetricDataToServiceControl(address);
        }
    }

}