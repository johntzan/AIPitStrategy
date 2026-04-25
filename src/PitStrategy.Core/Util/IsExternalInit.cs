// Polyfill so C# 10 records compile against netstandard2.0.
// This type is provided by the BCL on net5.0+; we ship a stub for older targets.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
