using System;
using Crestron.SimplSharpPro;
using PepperDash.Essentials.Core;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoComController : CrestronGenericBaseDevice, IComPorts
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
    }
}