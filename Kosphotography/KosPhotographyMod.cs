using Photocore;
using Photocore.CameraCapture;
using Photocore.Plates;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Kosphotography
{
    // The kosphotography head: a superset of collodion. It inherits all baseline registration from
    // PhotocoreModSystem (via base.Start/StartClientSide/StartServerSide) and adds the timed/automatic
    // camera item class plus the shutter stop-policy provider, config UI, and duration packet. Install
    // instead of collodion, not alongside it — PhotocoreModSystem.ShouldLoad stands the baseline head down
    // when this derived head is present (via HeadPrecedence), so exactly one head registers.
    public class KosPhotographyMod : PhotocoreModSystem
    {
        public const string KosChannelName = "kosphotography";

        // Wins over the precedence-0 baseline head on a dual install (see PhotocoreModSystem.ShouldLoad).
        protected override int HeadPrecedence => 1;

        private IClientNetworkChannel? _kosClientChannel;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("KosCamera", typeof(ItemKosCamera));
            ShutterSeam.PolicyProvider = new KosShutterPolicyProvider();
            api.Network.RegisterChannel(KosChannelName).RegisterMessageType(typeof(ShutterDurationPacket));
            RegisterDryPlateProcess();
            RegisterSaltedPaperProcess();
        }

        // Bromide dry plate: a third process on the same rough glass plate, branched by the first
        // interaction item (silver bromide emulsion). The Bromide profile carries its physics and the
        // never-dry window; here we only declare how the player sensitises it.
        private static void RegisterDryPlateProcess()
        {
            SensitizationRegistry.Register(new SensitizationRecipe
            {
                ChemistryId = "bromide",
                Substrate = "glass",
                SensitizedItemCode = new AssetLocation("photocore", "sensitizedplate"),
                Steps = new[]
                {
                    new SensitizationStep
                    {
                        Type = SensitizationInteractionType.PourLiquid,
                        Ingredient = new AssetLocation("kosphotography", "silverbromideemulsionportion"),
                        Amount = 40,
                        Sound = new AssetLocation("game", "sounds/effect/water-fill"),
                        ActionLangCode = "kosphotography:heldhelp-plate-bromide-emulsion"
                    }
                }
            });
        }

        // Salted paper print: a separate paper substrate, salted then sensitised with silver nitrate
        // (the existing silver solution). Chloride chemistry; the paperprint medium drives its opaque look.
        private static void RegisterSaltedPaperProcess()
        {
            AssetLocation pourSound = new("game", "sounds/effect/water-fill");
            SensitizationRegistry.Register(new SensitizationRecipe
            {
                ChemistryId = "chloride",
                Substrate = "paper",
                SensitizedItemCode = new AssetLocation("kosphotography", "sensitizedpaper"),
                Steps = new[]
                {
                    new SensitizationStep
                    {
                        Type = SensitizationInteractionType.PourLiquid,
                        Ingredient = new AssetLocation("game", "brineportion"),
                        Amount = 40,
                        Sound = pourSound,
                        ActionLangCode = "kosphotography:heldhelp-paper-salt"
                    },
                    new SensitizationStep
                    {
                        Type = SensitizationInteractionType.PourLiquid,
                        Ingredient = new AssetLocation("photocore", "silversolutionportion"),
                        Amount = 40,
                        Sound = pourSound,
                        ActionLangCode = "kosphotography:heldhelp-paper-silver"
                    }
                }
            });
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            _kosClientChannel = api.Network.GetChannel(KosChannelName);
            ShutterSeam.ConfigUi = new KosShutterConfigUi(api, this);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            api.Network.GetChannel(KosChannelName).SetMessageHandler<ShutterDurationPacket>(OnShutterDurationReceived);
        }

        internal void SendShutterDuration(int seconds)
            => _kosClientChannel?.SendPacket(new ShutterDurationPacket { DurationSeconds = seconds });

        private void OnShutterDurationReceived(IServerPlayer fromPlayer, ShutterDurationPacket packet)
        {
            int d = Math.Clamp(packet.DurationSeconds,
                KosCameraAttrs.MinShutterDurationSeconds, KosCameraAttrs.MaxShutterDurationSeconds);

            ItemSlot? slot = fromPlayer?.InventoryManager?.ActiveHotbarSlot;
            if (slot?.Itemstack?.Collectible is ItemKosCamera)
            {
                slot.Itemstack.Attributes.SetInt(KosCameraAttrs.ShutterDurationAttr, d);
                slot.MarkDirty();
            }
        }

        public override void Dispose()
        {
            // Clear the static seam so a later collodion-only load isn't left holding kos hooks.
            if (ShutterSeam.PolicyProvider is KosShutterPolicyProvider) ShutterSeam.PolicyProvider = null;
            if (ShutterSeam.ConfigUi is KosShutterConfigUi) ShutterSeam.ConfigUi = null;
            base.Dispose();
        }
    }

    // Client → server: persist a timed camera's burst duration onto the player's active camera stack.
    [ProtoContract]
    internal class ShutterDurationPacket
    {
        [ProtoMember(1)] public int DurationSeconds { get; set; }
    }
}
