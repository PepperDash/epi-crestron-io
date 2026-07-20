using System;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoComController : CrestronGenericBridgeableBaseDevice, IComPorts
    {
        private readonly IComPorts _hardware;

        public CenIoComController(string key, string name, GenericBase hardware)
            : base(key, name, hardware)
        {
            _hardware = hardware as IComPorts;
            if (_hardware == null)
                throw new ArgumentNullException("hardware", "Could not cast hardware to IComPorts");
        }

        public CrestronCollection<ComPort> ComPorts
        {
            get { return _hardware.ComPorts; }
        }

        public int NumberOfComPorts
        {
            get { return _hardware.NumberOfComPorts; }
        }

        public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
        {
            var joinMap = new CenIoComJoinMap(joinStart);

            var joinMapSerialized = JoinMapHelper.GetSerializedJoinMapForDevice(joinMapKey);

            if (!string.IsNullOrEmpty(joinMapSerialized))
                joinMap = JsonConvert.DeserializeObject<CenIoComJoinMap>(joinMapSerialized);

            if (bridge != null)
            {
                bridge.AddJoinMap(Key, joinMap);
            }
            else
            {
                Debug.LogInformation(this, "Please update config to use 'eiscapiadvanced' to get all join map features for this device.");
            }

            Debug.LogDebug(this, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));

            // TX from SIMPL to hardware COM ports
            trilist.SetStringSigAction(joinMap.Com1.JoinNumber, s =>
            {
                if (_hardware.ComPorts[1] != null)
                    _hardware.ComPorts[1].Send(s);
            });

            trilist.SetStringSigAction(joinMap.Com2.JoinNumber, s =>
            {
                if (_hardware.ComPorts[2] != null)
                    _hardware.ComPorts[2].Send(s);
            });

            // RX from hardware COM ports to SIMPL
            if (_hardware.ComPorts[1] != null)
                _hardware.ComPorts[1].SerialDataReceived += (port, args) =>
                    trilist.StringInput[joinMap.Com1.JoinNumber].StringValue = args.SerialData;

            if (_hardware.ComPorts[2] != null)
                _hardware.ComPorts[2].SerialDataReceived += (port, args) =>
                    trilist.StringInput[joinMap.Com2.JoinNumber].StringValue = args.SerialData;

            trilist.StringInput[joinMap.DeviceName.JoinNumber].StringValue = Name;

            IsOnline.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);

            trilist.OnlineStatusChange += (d, args) =>
            {
                if (!args.DeviceOnLine) return;

                trilist.StringInput[joinMap.DeviceName.JoinNumber].StringValue = Name;
                IsOnline.FireUpdate();
            };
        }
    }
}
