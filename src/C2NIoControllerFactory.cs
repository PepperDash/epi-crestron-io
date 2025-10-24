using System;
using System.Collections.Generic;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    public class C2NIoControllerFactory : EssentialsPluginDeviceFactory<C2NIoController>
    {
        public C2NIoControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string>() { "c2nio" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create new C2N-IO Device");

            return new C2NIoController(dc.Key, GetC2NIoDevice, dc);
        }

        static C2nIo GetC2NIoDevice(DeviceConfig dc)
        {
            var control = CommFactory.GetControlPropertiesConfig(dc);
            var cresnetId = control.CresnetIdInt;
            var branchId = control.ControlPortNumber;
            var parentKey = string.IsNullOrEmpty(control.ControlPortDevKey) ? "processor" : control.ControlPortDevKey;

            if (parentKey.Equals("processor", StringComparison.CurrentCultureIgnoreCase))
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new C2nIo", parentKey);
                return new C2nIo(cresnetId, Global.ControlSystem);
            }
            var cresnetBridge = DeviceManager.GetDeviceForKey(parentKey) as IHasCresnetBranches;

            if (cresnetBridge != null)
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new C2nIo", parentKey);
                return new C2nIo(cresnetId, cresnetBridge.CresnetBranches[(uint)branchId]);
            }
            Debug.LogInformation("Device {0} is not a valid cresnet master", parentKey);
            return null;
        }
    }
}