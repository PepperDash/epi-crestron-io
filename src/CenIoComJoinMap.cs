using PepperDash.Essentials.Core;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoComJoinMap : JoinMapBaseAdvanced
    {
        [JoinName("Name")]
        public JoinDataComplete Name = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 1,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Device name",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Serial
            });

        [JoinName("IsOnline")]
        public JoinDataComplete IsOnline = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 1,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Device online status",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Digital
            });

        [JoinName("Com1Tx")]
        public JoinDataComplete Com1Tx = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 2,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "COM 1 transmit from SIMPL to device",
                JoinCapabilities = eJoinCapabilities.FromSIMPL,
                JoinType = eJoinType.Serial
            });

        [JoinName("Com1Rx")]
        public JoinDataComplete Com1Rx = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 2,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "COM 1 receive from device to SIMPL",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Serial
            });

        [JoinName("Com2Tx")]
        public JoinDataComplete Com2Tx = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 3,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "COM 2 transmit from SIMPL to device",
                JoinCapabilities = eJoinCapabilities.FromSIMPL,
                JoinType = eJoinType.Serial
            });

        [JoinName("Com2Rx")]
        public JoinDataComplete Com2Rx = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 3,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "COM 2 receive from device to SIMPL",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Serial
            });

        public CenIoComJoinMap(uint joinStart)
            : base(joinStart, typeof(CenIoComJoinMap))
        {
        }
    }
}