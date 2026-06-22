using System.Diagnostics;

namespace FrameGeneratorDemo
{
    /// <summary>
    /// Diagnostics → the Visual Studio Output (Debug) window during an F5 session,
    /// so you can read and copy-paste them. Every line is prefixed "[FGDemo]" — set
    /// the Output pane to "Debug" and filter on that.
    /// </summary>
    internal static class Diag
    {
        public static void Log(string message) => Debug.WriteLine("[FGDemo] " + message);
    }
}
