// Polyfill so that `init`-only setters and `record` types compile on netstandard2.1.
// This type is required by the compiler but does not exist in the netstandard2.1 BCL.
// It is linked into the other netstandard2.1 projects (WorldGeneration, Networking).

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
