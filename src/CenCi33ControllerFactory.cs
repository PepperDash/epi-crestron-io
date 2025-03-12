using System.Collections.Generic;
using Crestron.SimplSharpPro.ThreeSeriesCards;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    public class CenCi33ControllerFactory : EssentialsPluginDeviceFactory<CenCi33Controller>
    {
        public CenCi33ControllerFactory()
        {
            MinimumEssentialsFrameworkVersion = "2.0.0";


            TypeNames = new List<string> {"cenci33"};
        }
        #region Overrides of EssentialsPluginDeviceFactory<CenCi33Controller>

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            Debug.Console(1, "Factory attempting to build new CEN-CI-3");

            var controlProperties = CommFactory.GetControlPropertiesConfig(dc);
            var ipId = controlProperties.IpIdInt;

            var cardCage = new CenCi33(ipId, Global.ControlSystem);
            var config = dc.Properties.ToObject<CenCi33Configuration>();

            return new CenCi33Controller(dc.Key, dc.Name, config, cardCage);
        }

        #endregion
    }
}