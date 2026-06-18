using System;
using Photochemistry;
using Photochemistry.CameraCapture;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Kosphotography
{
    // The kosphotography head: a superset of collodion. It inherits all baseline registration from
    // PhotochemistryModSystem (via base.Start/StartClientSide/StartServerSide) and adds the timed/automatic
    // camera item class plus the shutter stop-policy provider, config UI, and duration packet. Install
    // instead of collodion, not alongside it — PhotochemistryModSystem.ShouldLoad stands the baseline head down
    // when this derived head is present, so exactly one head registers.
    public class KosPhotographyMod : PhotochemistryModSystem
    {
        public const string KosChannelName = "kosphotography";

        private IClientNetworkChannel? _kosClientChannel;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("KosCamera", typeof(ItemKosCamera));
            ShutterSeam.PolicyProvider = new KosShutterPolicyProvider();
            api.Network.RegisterChannel(KosChannelName).RegisterMessageType(typeof(ShutterDurationPacket));
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
