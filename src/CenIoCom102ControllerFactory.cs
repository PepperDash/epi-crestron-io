using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using PepperDash.Core.Logging;
using PepperDash.Core;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoCom102ControllerFactory : EssentialsPluginDeviceFactory<CenIoComController>
    {
        public CenIoCom102ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string> {"ceniocom102",};
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogInformation($"[{dc.Key}] BuildDevice: Creating CenIoCom102 Controller");

            var controlProperties = CommFactory.GetControlPropertiesConfig(dc);
            var ipId = controlProperties.IpIdInt;

            var device = new CenIoCom102(ipId, Global.ControlSystem);
            var coms = new CenIoComController(dc.Key, dc.Name, device);

            Debug.LogInformation($"[{dc.Key}] BuildDevice: Created CenIoCom102 with IP-ID-{ipId.ToString("X2")} | device is {(device == null ? "null" : "not null" )} | coms is {(coms == null ? "null" : "not null")}");

            return coms;
        }
    }
}