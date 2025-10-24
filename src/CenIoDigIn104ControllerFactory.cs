using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoDigIn104ControllerFactory : EssentialsPluginDeviceFactory<CenIoDigIn104Controller>
    {
        public CenIoDigIn104ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string>() { "ceniodigin104" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create new CEN-DIGIN-104 Device");

            var control = CommFactory.GetControlPropertiesConfig(dc);
            if (control == null)
            {
                Debug.LogDebug("Factory failed to create a new CEN-DIGIN-104 Device, control properties not found");
                return null;
            }
            var ipid = control.IpIdInt;
            if (ipid != 0) return new CenIoDigIn104Controller(dc.Key, dc.Name, new CenIoDi104(ipid, Global.ControlSystem));

            Debug.LogDebug("Factory failed to create a new CEN-IO-IR-104 Device using IP-ID-{0}", ipid);
            return null;
        }
    }
}