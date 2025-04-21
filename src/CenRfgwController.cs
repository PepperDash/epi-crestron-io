using System;
using System.Collections.Generic;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using Crestron.SimplSharpPro.Gateways;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    [Description("Wrapper class for Crestron Infinet-EX Gateways")]
    public class CenRfgwController : CrestronGenericBaseDevice, IHasReady
    {
        public event EventHandler<IsReadyEventArgs> IsReadyEvent;

        public bool IsReady { get; private set; }

        private GatewayBase _gateway;

        public GatewayBase GateWay
        {
            get { return _gateway; }
        }

        /// <summary>
        /// Constructor for the on-board gateway
        /// </summary>
        /// <param name="key"></param>
        /// <param name="name"></param>
        /// <param name="gateway"></param>
        public CenRfgwController(string key, string name, GatewayBase gateway) :
            base(key, name, gateway)
        {
            _gateway = gateway;
            IsReady = true;
            FireIsReadyEvent(IsReady);
        }

        public CenRfgwController(string key, Func<DeviceConfig, GatewayBase> preActivationFunc, DeviceConfig config) :
            base(key, config.Name)
        {
            IsReady = false;
            FireIsReadyEvent(IsReady);
            AddPreActivationAction(() =>
            {
                _gateway = preActivationFunc(config);

                IsReady = true;
                RegisterCrestronGenericBase(_gateway);
                FireIsReadyEvent(IsReady);

            });
        }

        public static GatewayBase GetIpRfGateway(DeviceConfig dc, bool shareable)
        {
            var control = CommFactory.GetControlPropertiesConfig(dc);
            var type = dc.Type;
            var ipId = control.IpIdInt;

            switch (dc.Type.ToLowerInvariant())
            {
                case "cengwexer":
                    {
                        if (shareable)
                            return new CenGwExErEthernetSharable(ipId, Global.ControlSystem);

                        return new CenGwExEr(ipId, Global.ControlSystem);
                    }
                case "cenerfgwpoe":
                    {
                        if (shareable)
                            return new CenErfgwPoeEthernetSharable(ipId, Global.ControlSystem);

                        return new CenErfgwPoe(ipId, Global.ControlSystem);
                    }
                case "cenrfgwex":
                    {
                        if(shareable)
                            return new CenRfgwExEthernetSharable(ipId, Global.ControlSystem);
                        return new CenRfgwEx(ipId, Global.ControlSystem);
                    }
                default:
                    Debug.LogWarning("Device {device} is not a valid shared ethernet gateway", dc.Type);
                    return null;
            }
        }

        private void FireIsReadyEvent(bool data)
        {
            var handler = IsReadyEvent;
            if (handler == null) return;

            handler(this, new IsReadyEventArgs(data));

        }

        public static GatewayBase GetCenRfgwCresnetController(DeviceConfig dc)
        {
            var control = CommFactory.GetControlPropertiesConfig(dc);
            var type = dc.Type;
            var cresnetId = control.CresnetIdInt;
            var branchId = control.ControlPortNumber;
            var parentKey = string.IsNullOrEmpty(control.ControlPortDevKey) ? "processor" : control.ControlPortDevKey;

            if (parentKey.Equals("processor", StringComparison.CurrentCultureIgnoreCase))
            {
                Debug.LogDebug("Device {parent} is a valid cresnet master - creating new CenRfgw");

                if (type.Equals("cenerfgwpoe", StringComparison.InvariantCultureIgnoreCase))
                {
                    return new CenErfgwPoeCresnet(cresnetId, Global.ControlSystem);
                }
                if (type.Equals("cenrfgwex", StringComparison.InvariantCultureIgnoreCase))
                {
                    return new CenRfgwExCresnet(cresnetId, Global.ControlSystem);
                }
            }

            if (DeviceManager.GetDeviceForKey(parentKey) is ICresnetBridge cresnetBridge)
            {
                Debug.LogDebug("Device {parent} is a valid cresnet master - creating new CenRfgw", parentKey);

                if (type.Equals("cenerfgwpoe", StringComparison.InvariantCultureIgnoreCase))
                {
                    return new CenErfgwPoeCresnet(cresnetId, cresnetBridge.Branches[(uint)branchId]);
                }
                if (type.Equals("cenrfgwex", StringComparison.InvariantCultureIgnoreCase))
                {
                    return new CenRfgwExCresnet(cresnetId, cresnetBridge.Branches[(uint)branchId]);
                }
            }

            Debug.LogWarning("Device {parent} is not a valid cresnet master", parentKey);
            return null;
        }

        public enum EExGatewayType
        {
            Ethernet,
            EthernetShared,
            Cresnet
        }


        #region Factory

        public class CenRfgwControllerFactory : EssentialsPluginDeviceFactory<CenRfgwController>
        {
            public CenRfgwControllerFactory()
            {
                MinimumEssentialsFrameworkVersion = "2.0.0";

                TypeNames = new List<string> {"cenrfgwex", "cenerfgwpoe", "cengwexer"};
            }

            public override EssentialsDevice BuildDevice(DeviceConfig dc)
            {

                Debug.LogDebug("Factory Attempting to create new RF Gateway Device");

                var props = dc.Properties.ToObject<EssentialsRfGatewayConfig>();                

                switch (props.GatewayType)
                {
                    case (EExGatewayType.Ethernet):                        
                    case (EExGatewayType.EthernetShared):
                        return new CenRfgwController(dc.Key, dc.Name, GetIpRfGateway(dc, props.GatewayType == EExGatewayType.EthernetShared));
                    case (EExGatewayType.Cresnet):
                        return new CenRfgwController(dc.Key, GetCenRfgwCresnetController, dc);
                }
                return null;
            }
        }

        #endregion
    }

    
}


