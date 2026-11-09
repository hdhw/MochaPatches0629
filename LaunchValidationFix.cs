using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Mocha.Patch
{
    // Bypass for the launch validation. Build 20230629 names (Rec Room renames
    // these per update, update the constants to follow):
    //   script  -> FBONHDMKOAM   (launch-validation class, carries cachedTempHash)
    //   void    -> KGHKJOCLFMF   (produces the validation promise, returns ADCACBFKFKM)
    //   promise -> MDOIJNHMNCN   (holds the "Cancelled" const + 7 voids in dump order)
    //   iforgot -> JGLIBLIFPPP   (5th void after the "Cancelled" const)
    // The interop assemblies are regenerated per build, so the targets are resolved
    // by name at load time instead of compile-time refs. If a build lacks the types
    // the fix is skipped silently.
    internal static class LaunchValidationFix
    {
        private const string ScriptAssembly = "Assembly-CSharp";
        private const string ScriptClass = "FBONHDMKOAM";
        private const string VoidName = "KGHKJOCLFMF";

        private const string PromiseAssembly = "RecRoom.Promises.Runtime";
        private const string PromiseClass = "MDOIJNHMNCN";
        private const string IForgotName = "JGLIBLIFPPP";

        private static MethodInfo _voidMethod;
        private static MethodInfo _iforgot;
        private static Type _promiseType;

        internal static void Apply()
        {
            try
            {
                // Assembly-CSharp/RecRoom.Promises.Runtime are only JIT-loaded on demand,
                // well after Core.Load() runs - checking AppDomain.GetAssemblies() here just
                // sees they aren't loaded yet. Force-load them by name instead; BepInEx's own
                // interop assembly resolver (wired up before any plugin loads) finds them in
                // BepInEx/interop and loads them immediately.
                Assembly script, promises;
                try
                {
                    script = Assembly.Load(ScriptAssembly);
                    promises = Assembly.Load(PromiseAssembly);
                }
                catch (Exception loadEx)
                {
                    MochaLog.Warning("LaunchValidationFix: interop assemblies not loaded: " + loadEx.Message);
                    return;
                }

                Type scriptType = script.GetType(ScriptClass, throwOnError: false);
                _promiseType = promises.GetType(PromiseClass, throwOnError: false);
                if (scriptType == null || _promiseType == null)
                {
                    MochaLog.Warning("LaunchValidationFix: build does not have " + ScriptClass + " / " + PromiseClass);
                    return;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;
                _voidMethod = scriptType.GetMethod(VoidName, flags);
                // MDOIJNHMNCN overloads JGLIBLIFPPP - one takes Action<string> and
                // returns ADCACBFKFKM, the other takes a single string and returns
                // void. GetMethod(name, flags) alone is ambiguous between them and
                // throws AmbiguousMatchException. We want the string overload - it's
                // the one Prefix below actually invokes with a string argument, so
                // disambiguate on that signature explicitly. If this build's promise
                // still doesn't resolve, MDOIJNHMNCN also has a public parameterless
                // void LOJGLKLMFMO() - try that name instead.
                _iforgot = _promiseType.GetMethod(
                    IForgotName, flags, null, new[] { typeof(string) }, null);
                if (_voidMethod == null || _iforgot == null)
                {
                    MochaLog.Warning("LaunchValidationFix: methods not found: " + VoidName + " / " + IForgotName);
                    return;
                }

                Core.HarmonyInstance.Patch(
                    _voidMethod,
                    prefix: new HarmonyMethod(typeof(LaunchValidationFix).GetMethod(
                        nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));

                MochaLog.Msg("LaunchValidationFix: patched " + ScriptClass + "." + VoidName);
            }
            catch (Exception ex)
            {
                MochaLog.Warning("LaunchValidationFix: " + ex.Message);
            }
        }

        // Create a fresh promise, run it through the 5th void after "Cancelled"
        // (iforgot) so the validation completes immediately, and return it.
        private static bool Prefix(ref object __result)
        {
            try
            {
                object promise = Activator.CreateInstance(_promiseType);
                _iforgot.Invoke(promise, new object[] { string.Empty });
                __result = promise;
                return false;
            }
            catch (Exception ex)
            {
                MochaLog.Warning("LaunchValidationFix: prefix failed: " + ex.Message);
                return true;
            }
        }
    }
}
