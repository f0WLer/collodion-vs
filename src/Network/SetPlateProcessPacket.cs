using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class SetPlateProcessPacket
    {
        /// <summary>
        /// The validated process ID to stamp on the plate (e.g. "iodide", "chloride", "bromide").
        /// </summary>
        [ProtoMember(1)]
        public string ProcessId { get; set; } = string.Empty;

        /// <summary>
        /// Whether to apply to the offhand slot instead of the active hotbar slot.
        /// </summary>
        [ProtoMember(2)]
        public bool UseOffhand { get; set; }
    }
}
