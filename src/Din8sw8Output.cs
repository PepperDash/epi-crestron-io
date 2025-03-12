using System;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.CrestronIO;

namespace PDT.Plugins.Crestron.IO
{
    /// <summary>
    /// Wrapper class to 
    /// </summary>
    public class Din8sw8Output :  ISwitchedOutput
    {
        readonly SwitchedLoadWithOverrideParameter _switchedOutput;

        public BoolFeedback OutputIsOnFeedback { get; protected set; }

        public Din8sw8Output(SwitchedLoadWithOverrideParameter switchedOutput)
        {
            _switchedOutput = switchedOutput;

            OutputIsOnFeedback = new BoolFeedback(new Func<bool>(() => _switchedOutput.IsOn)); 
        }

        public void On()
        {
            _switchedOutput.FullOn();
        }

        public void Off()
        {
            _switchedOutput.FullOff();
        }
    }
}