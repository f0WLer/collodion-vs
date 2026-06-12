namespace Collodion.AdminTooling
{
    internal sealed partial class AdminToolingModSystemBridge
    {
        private readonly CollodionModSystem _owner;

        internal AdminToolingModSystemBridge(CollodionModSystem owner)
        {
            _owner = owner;
        }
    }
}
