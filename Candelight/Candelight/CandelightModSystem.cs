using System;
using Vintagestory.API.Common;

namespace Candlelight
{
    public class Core : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockClass("Candlelight:BlockCandelabra", typeof(BlockCandelabra));
            api.RegisterBlockEntityClass("Candlelight:BlockEntityCandelabra", typeof(BlockEntityCandelabra));
        }
    }
}