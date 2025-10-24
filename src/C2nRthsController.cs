using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using Crestron.SimplSharpPro.GeneralIO;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using Feedback = PepperDash.Essentials.Core.Feedback;

namespace PDT.Plugins.Crestron.IO
{
    [Description("Wrapper class for the C2N-RTHS sensor")]
    public class C2nRthsController : CrestronGenericBridgeableBaseDevice, ITemperatureSensor, IHumiditySensor
    {
        private C2nRths _device;
        private readonly CTimer _pollTimer;
        
        public IntFeedback TemperatureFeedback { get; private set; }
        public BoolFeedback TemperatureInCFeedback { get; private set; }
        public IntFeedback HumidityFeedback { get; private set; }

        public C2nRthsController(string key, Func<DeviceConfig, C2nRths> preActivationFunc, DeviceConfig config)
            : base(key, config.Name)
        {
            _pollTimer = new CTimer(
                _ => Feedbacks.ToList().ForEach(f => f.FireUpdate()), 
                this,
                TimeSpan.FromSeconds(30).Milliseconds, 
                TimeSpan.FromMinutes(5).Milliseconds);
            
            AddPreActivationAction(() =>
            {
                _device = preActivationFunc(config);
                if (_device == null)
                {
                    Debug.LogInformation(this, "ERROR: Unable to create C2nRths Device");
                    return;
                }

                RegisterCrestronGenericBase(_device);
                    
                TemperatureFeedback = new IntFeedback(() => _device.TemperatureFeedback.UShortValue);
                TemperatureInCFeedback = new BoolFeedback(() => _device.TemperatureFormat.BoolValue);
                HumidityFeedback = new IntFeedback(() => _device.HumidityFeedback.UShortValue);
                
                Feedbacks.AddRange(new List<Feedback> { TemperatureFeedback, TemperatureInCFeedback, HumidityFeedback });
                _device.BaseEvent += DeviceOnBaseEvent;
                
                _device.OnlineStatusChange += (d, args) => 
                    Debug.LogDebug(this, "Device status change... Online:{0} Temp:{1} Humidity{2}", _device.IsOnline, _device.TemperatureFeedback.UShortValue, _device.HumidityFeedback.UShortValue);
            });
        }

        private void DeviceOnBaseEvent(GenericBase device, BaseEventArgs args)
        {
            switch (args.EventId)
            {
                case C2nRths.TemperatureFeedbackEventId:
                    TemperatureFeedback.FireUpdate();
                    TemperatureInCFeedback.FireUpdate();
                    break;
                case C2nRths.HumidityFeedbackEventId:
                    HumidityFeedback.FireUpdate();
                    break;
            }
        }

        public void SetTemperatureFormat(bool setToC)
        {
            _device.TemperatureFormat.BoolValue = setToC;

            TemperatureInCFeedback.FireUpdate();
        }

        public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
        {
            var joinMap = new C2nRthsControllerJoinMap(joinStart);

            var joinMapSerialized = JoinMapHelper.GetSerializedJoinMapForDevice(joinMapKey);

            if (!string.IsNullOrEmpty(joinMapSerialized))
                joinMap = JsonConvert.DeserializeObject<C2nRthsControllerJoinMap>(joinMapSerialized);

            if (bridge != null)
            {
                bridge.AddJoinMap(Key, joinMap);
            }
            else
            {
                Debug.LogInformation(this, "Please update config to use 'eiscapiadvanced' to get all join map features for this device.");
            }

            Debug.LogDebug(this, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));


            trilist.SetBoolSigAction(joinMap.TemperatureFormat.JoinNumber, SetTemperatureFormat);



            IsOnline.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
            TemperatureFeedback.LinkInputSig(trilist.UShortInput[joinMap.Temperature.JoinNumber]);
            HumidityFeedback.LinkInputSig(trilist.UShortInput[joinMap.Humidity.JoinNumber]);

            trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;

            trilist.OnlineStatusChange += (d, args) =>
            {
                if (!args.DeviceOnLine) return;

                UpdateFeedbacksWhenOnline();

                trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;
            };
        }

        private void UpdateFeedbacksWhenOnline()
        {
            IsOnline.FireUpdate();
            TemperatureFeedback.FireUpdate();
            TemperatureInCFeedback.FireUpdate();
            HumidityFeedback.FireUpdate();
        }

        #region PreActivation

        private static C2nRths GetC2nRthsDevice(DeviceConfig dc)
        {
            var control = CommFactory.GetControlPropertiesConfig(dc);
            var cresnetId = control.CresnetIdInt;
            var branchId = control.ControlPortNumber;
            var parentKey = string.IsNullOrEmpty(control.ControlPortDevKey) ? "processor" : control.ControlPortDevKey;

            if (parentKey.Equals("processor", StringComparison.CurrentCultureIgnoreCase))
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new C2nRths", parentKey);
                return new C2nRths(cresnetId, Global.ControlSystem);
            }
            var cresnetBridge = DeviceManager.GetDeviceForKey(parentKey) as IHasCresnetBranches;

            if (cresnetBridge != null)
            {
                Debug.LogInformation("Device {0} is a valid cresnet master - creating new C2nRths", parentKey);
                return new C2nRths(cresnetId, cresnetBridge.CresnetBranches[(uint)branchId]);
            }
            Debug.LogInformation("Device {0} is not a valid cresnet master", parentKey);
            return null;
        }
        #endregion

        public class C2nRthsControllerFactory : EssentialsPluginDeviceFactory<C2nRthsController>
        {
            public C2nRthsControllerFactory()
            {
                MinimumEssentialsFrameworkVersion = "2.0.0";


                TypeNames = new List<string>() { "c2nrths" };
            }

            public override EssentialsDevice BuildDevice(DeviceConfig dc)
            {
                Debug.LogDebug("Factory Attempting to create new C2N-RTHS Device");

                return new C2nRthsController(dc.Key, GetC2nRthsDevice, dc);
            }
        }
    }
}