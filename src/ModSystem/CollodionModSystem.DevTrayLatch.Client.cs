using System;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private void OnClientDevTrayLatchTick(float dt)
        {
            if (ClientApi == null) return;

            try
            {
                // Only clear when RMB is actually released.
                if (GetRightMouseDown()) return;

                var attrs = ClientApi.World?.Player?.Entity?.Attributes;
                if (attrs == null) return;

                attrs.RemoveAttribute(BlockDevelopmentTray.TimedNeedReleaseKey);
            }
            catch
            {
                // ignore
            }
        }
    }
}
