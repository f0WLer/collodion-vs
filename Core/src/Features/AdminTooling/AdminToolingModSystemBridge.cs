namespace Photochemistry.AdminTooling
{
    internal sealed partial class AdminToolingModSystemBridge
    {
        private readonly PhotochemistryModSystem _owner;

        internal AdminToolingModSystemBridge(PhotochemistryModSystem owner)
        {
            _owner = owner;
        }
    }
}
