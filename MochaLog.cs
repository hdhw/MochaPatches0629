using BepInEx.Logging;

namespace Mocha.Patch
{
    internal static class MochaLog
    {
        internal static ManualLogSource Source { get; set; }

        internal static void Msg(string message) => Source?.LogInfo(message);
        internal static void Warning(string message) => Source?.LogWarning(message);
        internal static void Error(string message) => Source?.LogError(message);
    }
}
