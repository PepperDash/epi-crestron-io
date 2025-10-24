﻿using System.Collections.Generic;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.Lighting;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Core.CrestronIO;

namespace PDT.Plugins.Crestron.IO
{
    public class Din8sw8Controller : EssentialsDevice, ISwitchedOutputCollection
    {
        // Need to figure out some sort of interface to make these switched outputs behave like processor relays so they can be used interchangably

        public Din8Sw8 SwitchModule { get; private set; }
        
        /// <summary>
        /// Collection of generic switched outputs
        /// </summary>
        public Dictionary<uint, ISwitchedOutput> SwitchedOutputs { get; private set; }

        public Din8sw8Controller(string key, uint cresnetId)
            : base(key)
        {
            SwitchedOutputs = new Dictionary<uint, ISwitchedOutput>();

            SwitchModule = new Din8Sw8(cresnetId, Global.ControlSystem);

            if (SwitchModule.Register() != eDeviceRegistrationUnRegistrationResponse.Success)
            {
                Debug.LogVerbose(this, "Error registering Din8sw8. Reason: {0}", SwitchModule.RegistrationFailureReason);
            }

            PopulateDictionary();
        }

        /// <summary>
        /// Populates the generic collection with the loads from the Crestron collection
        /// </summary>
        void PopulateDictionary()
        {
            foreach (var item in SwitchModule.SwitchedLoads)
            {
                SwitchedOutputs.Add(item.Number, new Din8sw8Output(item));
            }
        }
    }

    public class Din8sw8ControllerFactory : EssentialsPluginDeviceFactory<Din8sw8Controller>
    {
        public Din8sw8ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string>() { "din8sw8" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory Attempting to create new Din8sw8Controller Device");
            var comm = CommFactory.GetControlPropertiesConfig(dc);

            return new Din8sw8Controller(dc.Key, comm.CresnetIdInt);

        }
    }

}