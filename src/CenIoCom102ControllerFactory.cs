using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

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
            var controlProperties = CommFactory.GetControlPropertiesConfig(dc);
            var ipId = controlProperties.IpIdInt;

            var device = new CenIoCom102(ipId, Global.ControlSystem);
            return new CenIoComController(dc.Key, dc.Name, device);
        }
    }
}