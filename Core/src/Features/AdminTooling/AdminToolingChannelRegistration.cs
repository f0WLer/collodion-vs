using ProtoBuf;
using Vintagestory.API.Client;

namespace Photocore.AdminTooling
{
    internal static class AdminToolingChannelRegistration
    {
        internal static INetworkChannel RegisterAdminToolingMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(GiveSensitizedPlatePacket))
                .RegisterMessageType(typeof(DevelopPermissionPacket));
        }
    }

    /// <summary>Requests that the server spawn a fresh sensitized plate of the given chemistry in the
    /// requesting player's inventory. Empty chemistry falls back to the baseline iodide collodion.</summary>
    [ProtoContract]
    internal class GiveSensitizedPlatePacket
    {
        [ProtoMember(1)]
        public string Chemistry { get; set; } = "";
    }

    /// <summary>Server → client: whether this player may currently develop/finalize exposures (i.e. create
    /// server-housed photos) under the develop whitelist. Pushed on join and whenever the whitelist
    /// changes. Defaults to true so an absent packet never blocks developing.</summary>
    [ProtoContract]
    internal class DevelopPermissionPacket
    {
        [ProtoMember(1)]
        public bool Allowed { get; set; } = true;
    }
}
