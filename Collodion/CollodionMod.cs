namespace Photochemistry
{
    // Baseline collodion head. The shared PhotochemistryModSystem is abstract and lives in
    // Photochemistry.Core.dll (which must contain no instantiable ModSystem); this thin concrete subclass
    // is the single ModSystem in the collodion mod DLL. It adds nothing — baseline is the unextended core.
    public class CollodionMod : PhotochemistryModSystem
    {
    }
}
