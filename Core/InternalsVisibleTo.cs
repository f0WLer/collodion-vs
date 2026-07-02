using System.Runtime.CompilerServices;

// Heads and tests consume Core through internals rather than a curated public surface — deliberate,
// since a public API for single-owner consumers would be ceremony. The trade-off: the compiler does
// not stop head code from reaching arbitrarily deep into Core, so head thinness is enforced by the
// architecture conformance tests (tests/Architecture) instead. This is also the one sanctioned place
// where Core names its heads; Core/src must stay head-agnostic (see CoreNeverNamesAHead).
[assembly: InternalsVisibleTo("collodion")]
[assembly: InternalsVisibleTo("collodion.tests")]
[assembly: InternalsVisibleTo("kosphotography")]
