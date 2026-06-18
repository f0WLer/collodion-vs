using Photochemistry.Plates;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Photochemistry.AdminTooling
{
    // Server-side operator-tooling startup composition.
    // Keeps config bootstrap/logging details out of ModSystem callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        internal void ConfigureServerOperatorToolingStartup(ICoreServerAPI api)
        {
            _owner.ApplyConfig(ConfigLifecycle.LoadOrCreate(api, PhotochemistryModSystem.ConfigFileName));

            BestEffort.Try(_owner.BestEffortLogger,
                "log server config load",
                () => api.Logger.Notification($"photochemistry: loaded config '{PhotochemistryModSystem.ConfigFileName}'"));

            // Resolve the channel directly — _owner.ServerChannel is not yet assigned at this
            // point in startup (it is set later by ConfigureServerCameraCaptureStartup).
            api.Network.GetChannel("photochemistry")
               .SetMessageHandler<GiveSensitizedPlatePacket>(OnGiveSensitizedPlateReceived);
        }

        private void OnGiveSensitizedPlateReceived(IServerPlayer player, GiveSensitizedPlatePacket packet)
        {
            if (_owner.ModApi?.World == null) return;

            Item? item = _owner.ModApi.World.GetItem(new AssetLocation("photochemistry", "sensitizedplate"));
            if (item == null) return;

            var stack = new ItemStack(item, 1);
            PlateAttributes.SetStage(stack, PlateStage.Sensitized);
            PlateAttributes.SetNameLangCode(stack, "photochemistry:plate-name-sensitized");
            PlateDryingTransition.ResetTimer(_owner.ModApi.World, stack, PlateDryingTransition.ResolveWetDurationHours(_owner.ModApi));

            if (!player.InventoryManager.TryGiveItemstack(stack))
                _owner.ModApi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        }
    }
}
