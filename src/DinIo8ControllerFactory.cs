using System;
using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    public class DinIo8ControllerFactory : EssentialsPluginDeviceFactory<DinIo8Controller>
    {
        public DinIo8ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string>() { "DinIo8" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create new DinIo8 Device");

            return new DinIo8Controller(dc.Key, GetDinIo8Device, dc);
        }

        static DinIo8 GetDinIo8Device(DeviceConfig dc)
        {
            var control = CommFactory.GetControlPropertiesConfig(dc);
            var cresnetId = control.CresnetIdInt;
            var branchId = control.ControlPortNumber;
            var parentKey = string.IsNullOrEmpty(control.ControlPortDevKey) ? "processor" : control.ControlPortDevKey;

            if (parentKey.Equals("processor", StringComparison.CurrentCultureIgnoreCase))
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new DinIo8", parentKey);
                return new DinIo8(cresnetId, Global.ControlSystem);
            }
            var cresnetBridge = DeviceManager.GetDeviceForKey(parentKey) as IHasCresnetBranches;

            if (cresnetBridge != null)
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new DinIo8", parentKey);
                return new DinIo8(cresnetId, cresnetBridge.CresnetBranches[(uint)branchId]);
            }
            Debug.LogInformation("Device {0} is not a valid cresnet master", parentKey);
            return null;
        }
    }
}