using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugins
{
    /// <summary>
    /// CEN-IO-RY Controller factory
    /// </summary>
    public class CenIoRy104ControllerFactory : EssentialsPluginDeviceFactory<CenIoRy104Controller>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public CenIoRy104ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "3.0.0";


            TypeNames = new List<string>() { "ceniory104" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create a new CEN-IO-RY-104 Device");

            var controlPropertiesConfig = CommFactory.GetControlPropertiesConfig(dc);
            if (controlPropertiesConfig == null)
            {
                Debug.LogDebug("Factory failed to create a new CEN-IO-RY-104 Device, control properties not found");
                return null;
            }

            var ipid = controlPropertiesConfig.IpIdInt;
            if (ipid != 0) return new CenIoRy104Controller(dc.Key, dc.Name, new CenIoRy104(ipid, Global.ControlSystem));
            
            Debug.LogDebug("Factory failed to create a new CEN-IO-RY-104 Device using IP-ID-{IpId}", ipid);
            return null;
        }
    }
}