using ProtoBuf;
using Vintagestory.API.Client;

namespace Photochemistry.AdminTooling
{
    // Packet DTO and channel registration for AdminTooling network messages.
    internal static class AdminToolingChannelRegistration
    {
        internal static INetworkChannel RegisterAdminToolingMessageTypes(INetworkChannel channel)
        {
            return channel
                .RegisterMessageType(typeof(GiveSensitizedPlatePacket));
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
}
