using System.Collections.Generic;
using Crestron.SimplSharpPro.ThreeSeriesCards;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugins
{
    public class CenCi31ControllerFactory : EssentialsPluginDeviceFactory<CenCi31Controller>
    {
        public CenCi31ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "3.0.0-fix-correct-interface-name.1";


            TypeNames = new List<string> {"cenci31"};
        }

        #region Overrides of EssentialsPluginDeviceFactory<CenCi31Controller>

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.LogDebug("Factory attempting to build new CEN-CI-1");

            var controlProperties = CommFactory.GetControlPropertiesConfig(dc);
            var ipId = controlProperties.IpIdInt;

            var cardCage = new CenCi31(ipId, Global.ControlSystem);
            var config = dc.Properties.ToObject<CenCi31Configuration>();

            return new CenCi31Controller(dc.Key, dc.Name, config, cardCage);
        }

        #endregion
    }
}