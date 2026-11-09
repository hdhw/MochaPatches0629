using System;
using HarmonyLib;
using BestHTTP;
using BestHTTP.SignalRCore;
using Org.BouncyCastle.Crypto.Tls;
using RecRoom.AntiCheat;

namespace Mocha.Patch
{
    public class HarmonyPatches
    {
        // As of this build the client fetches its Photon connection info (app ids,
        // version, region) from the server itself rather than through client-side
        // AppSettings overrides, so the PhotonPatch* classes 0414 needed are gone -
        // see README ("photon patching").

        // Redirects any ns.rec.net request (nameserver lookups, including the versioned
        // RecNet one - ns.rec.net/?v=2 - used by the SignalR/RecNet connection setup) to
        // playmocha.xyz, and stamps every outgoing request with our auth headers.
        internal static void RedirectAndTag(HTTPRequest request)
        {
            string url = request.Uri == null ? null : request.Uri.ToString();
            if (url != null && url.Contains("ns.rec.net"))
            {
                request.Uri = new Il2CppSystem.Uri("https://playmocha.xyz/nameserver");
                url = "https://playmocha.xyz/nameserver";
            }
            MochaLog.Msg("Caught request: " + url);

            request.SetHeader("X-Mocha-SteamId", SteamCache.CachedSteamId64);
            request.SetHeader("X-Mocha-Build", Core.ModVersion);
            request.SetHeader("X-Mocha-Sig", Integrity.LastSnapshot);
        }

        [HarmonyPatch(typeof(HTTPManager), "SendRequest", new Type[]
        {
            typeof(HTTPRequest)
        })]
        internal class NSPatch
        {
            private static void Prefix(ref HTTPRequest request)
            {
                RedirectAndTag(request);
            }
        }

        // BestHTTP.SignalRCore's negotiate/connect calls go through HTTPRequest.Send()
        // directly instead of HTTPManager.SendRequest - without this, the RecNet SignalR
        // connection never gets redirected and the client hangs on "Connecting to RecNet"
        // trying to reach the real ns.rec.net-derived host.
        [HarmonyPatch(typeof(HTTPRequest), "Send")]
        internal class NSPatchDirectSend
        {
            private static void Prefix(HTTPRequest __instance)
            {
                RedirectAndTag(__instance);
            }
        }

        // HubConnection (RecNet's SignalR hub, "Connecting to RecNet") takes its target
        // Uri directly in its constructor - it never goes through HTTPRequest/HTTPManager
        // at all, so NSPatch/NSPatchDirectSend never see it. Log what host it's actually
        // trying to reach; redirect any *.rec.net host to playmocha.xyz keeping the
        // original path, same as the nameserver redirect.
        internal static Il2CppSystem.Uri RedirectHubUri(Il2CppSystem.Uri uri)
        {
            string url = uri == null ? null : uri.ToString();
            MochaLog.Msg("HubConnection target: " + url);
            if (url != null && url.Contains("rec.net"))
            {
                string path = uri.PathAndQuery;
                string redirected = "https://playmocha.xyz" + path;
                MochaLog.Msg("Redirecting hub connection to: " + redirected);
                return new Il2CppSystem.Uri(redirected);
            }
            return uri;
        }

        [HarmonyPatch(typeof(HubConnection), MethodType.Constructor, new Type[]
        {
            typeof(Il2CppSystem.Uri), typeof(BestHTTP.SignalRCore.IProtocol)
        })]
        internal class HubConnectionPatch2Arg
        {
            private static void Prefix(ref Il2CppSystem.Uri hubUri)
            {
                hubUri = RedirectHubUri(hubUri);
            }
        }

        [HarmonyPatch(typeof(HubConnection), MethodType.Constructor, new Type[]
        {
            typeof(Il2CppSystem.Uri), typeof(BestHTTP.SignalRCore.IProtocol), typeof(HubOptions)
        })]
        internal class HubConnectionPatch3Arg
        {
            private static void Prefix(ref Il2CppSystem.Uri hubUri)
            {
                hubUri = RedirectHubUri(hubUri);
            }
        }

        [HarmonyPatch(typeof(LegacyTlsAuthentication), "NotifyServerCertificate")]
        internal class AlwaysVerifyTLSPatch
        {
            private static bool Prefix()
            {
                return false;
            }
        }

        [HarmonyPatch(typeof(EACManager), "GenerateChallengeResponse")]
        internal class EACPatch
        {
            private static bool Prefix(ref string __result)
            {
                __result = "\"AQAAAGd9O3h2ynQW6Y/1MhdZC8VoHygxyTzmiRvAfpiRtJEBQ+NVaXMStRTsYQk42H1hbB7NGKhIpgfShk+ADtRW9EU/YF5320eGmINJZAqkm3pyX9QF/w1QT4IB2EQfOqTfpryQ3QFchXYqDgg/SbX+/X9mgzssbflIw3OAK+c=\"";
                return false;
            }
        }
    }
}
