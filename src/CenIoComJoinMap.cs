using PepperDash.Essentials.Core;

namespace PDT.Plugins.Crestron.IO
{
    public class CenIoComJoinMap : JoinMapBaseAdvanced
    {
        [JoinName("Com1")]
        public JoinDataComplete Com1 = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 1,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Com 1 TX/RX",
                JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
                JoinType = eJoinType.Serial
            });

        [JoinName("Com1")]
        public JoinDataComplete Com2 = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 3,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Com 2 TX/RX",
                JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
                JoinType = eJoinType.Serial
            });

        public CenIoComJoinMap(uint joinStart)
            : base(joinStart, typeof(CenIoComJoinMap))
        {
        }
    }
}