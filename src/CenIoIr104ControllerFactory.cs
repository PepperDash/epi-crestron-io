using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugins
{
    /// <summary>
    /// CEN-IO-IR-104 controller fatory
    /// </summary>
    public class CenIoIr104ControllerFactory : EssentialsPluginDeviceFactory<CenIoIr104Controller>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public CenIoIr104ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "3.0.0-fix-correct-interface-name.1";


            TypeNames = new List<string>() { "cenioir104" };
        }

        /// <summary>
        /// Build device CEN-IO-IR-104
        /// </summary>
        /// <param name="dc"></param>
        /// <returns></returns>
        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create new CEN-IO-IR-104 Device");

            var control = CommFactory.GetControlPropertiesConfig(dc);
            if (control == null)
            {
                Debug.LogDebug("Factory failed to create a new CEN-IO-IR-104 Device, control properties not found");
                return null;
            }

            var ipid = control.IpIdInt;
            if(ipid != 0) return new CenIoIr104Controller(dc.Key, dc.Name, new CenIoIr104(ipid, Global.ControlSystem));

            Debug.LogDebug("Factory failed to create a new CEN-IO-IR-104 Device using IP-ID-{IpId}", ipid);
            return null;
        }
    }
}