using System;
using System.Net.Http;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace Mocha.Patch
{
    [BepInPlugin("xyz.playmocha.patcher", "MochaPatches", ModVersion)]
    public class Core : BasePlugin
    {
        public const string ModVersion = "1.1.0";

        // all patches go under one id so nothing else can fight over them
        internal static readonly Harmony HarmonyInstance = new Harmony("xyz.playmocha.patcher");

        internal const string ConfigUrl = "https://playmocha.xyz/api/config/v2";

        internal static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10.0)
        };

        public override void Load()
        {
            MochaLog.Source = Log;

            // No-op: keeps the ObfuscatedByTopLevelOpsecObf decoy in the call graph so it
            // survives to the shipped DLL as a red herring. Arms nothing, does nothing.
            ObfuscatedByTopLevelOpsecObf.OpsecGuard.Arm();

            // PhotonConnection removed - as of this build the client fetches Photon
            // connection info from the server itself; see README ("photon patching").

            SteamCache.Load();
            Integrity.Snapshot();

            HarmonyInstance.PatchAll(typeof(Core).Assembly);
            LaunchValidationFix.Apply();

            // MochaGuard ships inside this assembly as of 1.0.8. It was a standalone
            // MelonMod under MelonLoader; under BepInEx it's a static runtime this
            // plugin drives directly, started last so the config fetch, Steam id and
            // the first integrity snapshot are already done before its watchdog
            // threads spin up.
            MochaGuard.GuardRuntime.Start(HarmonyInstance, typeof(Core).Assembly);

            MochaLog.Msg("mocha patch " + ModVersion + " loaded");

            AddComponent<MochaBehaviour>();
        }

        public override bool Unload()
        {
            MochaGuard.GuardRuntime.Stop();
            return base.Unload();
        }

        internal static bool IsMilestone(int m)
        {
            return m == 60 || m == 30 || m == 15 || (m >= 0 && m <= 10);
        }

        internal static bool ApplyMaintenance(int minutes, bool show)
        {
            bool result;
            try
            {
                Il2CppSystem.Type type = Il2CppSystem.Type.GetType("SessionManager");
                if (type == null)
                {
                    MochaLog.Warning("SessionManager il2cpp type not found");
                    result = false;
                }
                else
                {
                    UnityEngine.Object target = UnityEngine.Object.FindObjectOfType(type);
                    if (target == null)
                    {
                        result = false;
                    }
                    else
                    {
                        const Il2CppSystem.Reflection.BindingFlags bindingFlags =
                            Il2CppSystem.Reflection.BindingFlags.Instance |
                            Il2CppSystem.Reflection.BindingFlags.NonPublic |
                            Il2CppSystem.Reflection.BindingFlags.Public;
                        Il2CppSystem.Reflection.FieldInfo field = type.GetField("serverMaintenanceMinuteVOs", bindingFlags);
                        if (field == null)
                        {
                            MochaLog.Warning("serverMaintenanceMinuteVOs field not found");
                            result = false;
                        }
                        else
                        {
                            Il2CppSystem.Type elementType = field.FieldType.GetElementType();
                            int count = show ? 1 : 0;
                            Il2CppSystem.Array array = Il2CppSystem.Array.CreateInstance(elementType, count);
                            if (show)
                            {
                                Il2CppSystem.Object instance = Il2CppSystem.Activator.CreateInstance(elementType);
                                Il2CppSystem.Reflection.FieldInfo startsInMinutes = elementType.GetField("StartsInMinutes", bindingFlags);
                                if (startsInMinutes != null)
                                {
                                    startsInMinutes.SetValue(instance, Box(minutes));
                                }
                                array.SetValue(instance, 0);
                            }
                            field.SetValue(target, array);
                            result = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MochaLog.Warning("applymaintenance failed son im crine : " + ex.Message);
                result = false;
            }
            return result;
        }

        private static unsafe Il2CppSystem.Object Box(int value)
        {
            IntPtr ptr = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<int>.NativeClassPtr, new IntPtr(&value));
            return new Il2CppSystem.Object(ptr);
        }
    }
}
