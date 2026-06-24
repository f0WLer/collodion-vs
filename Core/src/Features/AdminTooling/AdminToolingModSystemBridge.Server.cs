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

            // Operator disk-audit tooling: /photoadmin (controlserver-gated).
            ServerPhotoCommands.Register(api, _owner);
        }

        private void OnGiveSensitizedPlateReceived(IServerPlayer player, GiveSensitizedPlatePacket packet)
        {
            if (_owner.ModApi?.World == null) return;

            // Operator-only debug action: mirror the client-side dialog gate so a modified client can't
            // request free plates without the server-operator privilege.
            if (!player.HasPrivilege("controlserver")) return;

            // Resolve the chemistry's sensitized item + substrate from its recipe so the plate exposes with
            // the right per-process timing and develops on the right medium. Falls back to glass iodide.
            string chemistry = string.IsNullOrEmpty(packet.Chemistry)
                ? PlateAttributes.ChemistryCollodion : packet.Chemistry.ToLowerInvariant();
            SensitizationRecipe? recipe = SensitizationRegistry.ByChemistry(chemistry);
            AssetLocation itemCode = recipe?.SensitizedItemCode ?? new AssetLocation("photochemistry", "sensitizedplate");

            Item? item = _owner.ModApi.World.GetItem(itemCode);
            if (item == null) return;

            var stack = new ItemStack(item, 1);
            PlateAttributes.SetStage(stack, PlateStage.Sensitized);
            PlateAttributes.SetChemistry(stack, chemistry);
            // Pin the glass-plate name; other substrates (paper) keep their own itemtype name.
            if (recipe?.Substrate is null or "glass")
                PlateAttributes.SetNameLangCode(stack, "photochemistry:plate-name-sensitized");
            PlateDryingTransition.ResetTimer(_owner.ModApi.World, stack, PlateDryingTransition.ResolveWetDurationHours(_owner.ModApi, stack));

            if (!player.InventoryManager.TryGiveItemstack(stack))
                _owner.ModApi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        }
    }
}
