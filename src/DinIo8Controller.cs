using System;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.GeneralIO;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PDT.Plugins.Crestron.IO
{
    public class DinIo8Controller:CrestronGenericBaseDevice, IIOPorts
    {
        private DinIo8 _device;

        public DinIo8Controller(string key, Func<DeviceConfig, DinIo8> preActivationFunc, DeviceConfig config):base(key, config.Name)
        {
            AddPreActivationAction(() =>
            {
                _device = preActivationFunc(config);

                RegisterCrestronGenericBase(_device);
            });
        }

        #region Implementation of IIOPorts

        public CrestronCollection<Versiport> VersiPorts
        {
            get { return _device.VersiPorts; }
        }

        public int NumberOfVersiPorts
        {
            get { return _device.NumberOfVersiPorts; }
        }

        #endregion


    }
}