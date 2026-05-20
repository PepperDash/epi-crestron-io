using System;
using System.Collections.Generic;
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
        private readonly Dictionary<uint, BridgeRegistration> _bridgeRegistrations = new Dictionary<uint, BridgeRegistration>();
        private bool _comPortHandlersRegistered;

        private class BridgeRegistration
        {
            public BasicTriList TriList { get; set; }
            public CenIoComJoinMap JoinMap { get; set; }
        }

        public CenIoComController(string key, string name, GenericBase hardware)
            : base(key, name)
        {
            if (hardware == null)
                throw new ArgumentNullException(nameof(hardware));

            _hardware = hardware as IComPorts;
            if (_hardware == null)
                throw new ArgumentException("Could not cast hardware to IComPorts", nameof(hardware));

            RegisterCrestronGenericBase(hardware);
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

            _bridgeRegistrations[trilist.ID] = new BridgeRegistration
            {
                TriList = trilist,
                JoinMap = joinMap
            };

            RegisterComPortHandlers();

            trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;

            IsOnline.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);

            trilist.SetStringSigAction(joinMap.Com1Tx.JoinNumber, value => SendToComPort(1, value));
            trilist.SetStringSigAction(joinMap.Com2Tx.JoinNumber, value => SendToComPort(2, value));

            trilist.OnlineStatusChange += (d, args) =>
            {
                if (!args.DeviceOnLine)
                    return;

                trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;
                IsOnline.FireUpdate();
            };
        }

        private void RegisterComPortHandlers()
        {
            if (_comPortHandlersRegistered)
                return;

            RegisterComPortHandler(1, Com1SerialDataReceived);
            RegisterComPortHandler(2, Com2SerialDataReceived);

            _comPortHandlersRegistered = true;
        }

        private void RegisterComPortHandler(uint portNumber, ComPortDataReceivedEvent handler)
        {
            if (NumberOfComPorts < portNumber || ComPorts[portNumber] == null)
                return;

            ComPorts[portNumber].SerialDataReceived += handler;
        }

        private void SendToComPort(uint portNumber, string value)
        {
            if (string.IsNullOrEmpty(value) || NumberOfComPorts < portNumber || ComPorts[portNumber] == null)
                return;

            ComPorts[portNumber].Send(value);
        }

        private void Com1SerialDataReceived(ComPort port, ComPortSerialDataEventArgs args)
        {
            PublishSerialData(registration => registration.JoinMap.Com1Rx.JoinNumber, args.SerialData);
        }

        private void Com2SerialDataReceived(ComPort port, ComPortSerialDataEventArgs args)
        {
            PublishSerialData(registration => registration.JoinMap.Com2Rx.JoinNumber, args.SerialData);
        }

        private void PublishSerialData(Func<BridgeRegistration, uint> joinSelector, string value)
        {
            foreach (var registration in _bridgeRegistrations.Values)
            {
                registration.TriList.StringInput[joinSelector(registration)].StringValue = value;
            }
        }
    }
}