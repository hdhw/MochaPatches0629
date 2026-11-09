# MochaPatches0629

A BepInEx (IL2CPP) plugin that points the Rec Room client at
[Mocha](https://playmocha.xyz), a private-server reimplementation of the Rec Room
backend, instead of the real one.

This is the `20230629` build's version of `MochaPatches0414` (which targets
`20230414` under MelonLoader). Two things changed between those builds:

1. **Loader: MelonLoader -> BepInEx.** 0414 was a `MelonMod`; this is a BepInEx
   `BasePlugin`. There's no per-frame `OnUpdate` on a BepInEx plugin, so the
   config-poll / maintenance-countdown / scene-cleanup loop that lived in 0414's
   `Core.OnUpdate` now lives on `MochaBehaviour`, a `MonoBehaviour` `Core.Load()`
   adds to the scene (see `AddComponent<MochaBehaviour>()`).
2. **No Photon patches.** In `20230414` the client builds its own Photon
   `AppSettings` from obfuscated client methods, so 0414 has to dump the game and
   patch those methods directly (`PhotonPatch`, `PhotonPatch2`, `PhotonPatch3`,
   backed by `PhotonConnection.cs`). As of this build the client instead fetches
   its Photon connection info from the server, so none of that exists here -
   `PhotonConnection.cs` is gone and `HarmonyPatches.cs` has no Photon patch at
   all.

## Launch validation fix

`LaunchValidationFix.cs` bypasses a client-side launch validation check that
otherwise blocks the client on startup. The obfuscated names it targets (Rec
Room renames these every build):

- `FBONHDMKOAM` (`Assembly-CSharp`) - the launch-validation class; carries the
  `cachedTempHash` field this was located from.
- `KGHKJOCLFMF` - the method on that class producing the validation promise
  (returns `ADCACBFKFKM`).
- `MDOIJNHMNCN` (`RecRoom.Promises.Runtime`) - the concrete promise type
  implementing `ADCACBFKFKM`; carries the `"Cancelled"` string constant.
- `JGLIBLIFPPP` - the method on the promise the prefix invokes to resolve it
  immediately.

All four were cross-checked directly against `20230629`'s decompiled dump, not
just carried over from a screenshot. One real bug already found and fixed:
`MDOIJNHMNCN` overloads `JGLIBLIFPPP` - one overload takes `Action<string>` and
returns `ADCACBFKFKM`, the other takes a single `string` and returns `void`.
Looking it up with a bare `GetMethod(name, flags)` is ambiguous between the two
and throws `AmbiguousMatchException`, which is why `Apply()` never got past that
line before. It's now resolved by pinning the `string` overload explicitly.

**If the promise still doesn't resolve on a live launch**, `MDOIJNHMNCN` also has
a public parameterless `void LOJGLKLMFMO()` - try swapping `IForgotName` to that
instead. I can't launch the actual Windows/VR client from here to verify which
one is correct at runtime; this needs an actual launch test.

## Building

```
build.bat
```

or, for the obfuscated release build:

```
build.obfuscated.bat
```

### Interop assemblies (read this before building)

The `.csproj` currently points `MochaCoreDir` / `InteropDir` at
`../../Documents/CoffeeNet/core` and `../../Documents/CoffeeNet/interop` -
that's a **borrowed, already-generated** BepInEx core + `20230629` interop set
from a different (unrelated) project on this machine, used here only because
regenerating ~300 interop DLLs from scratch wasn't worth it tonight. It's
verified to actually be `20230629`: the obfuscated `global-metadata.dat` was
already repaired and matched against `build_info.json`'s `game_assembly_size`,
and every `LaunchValidationFix` constant above was independently checked
against the real decompiled dump, not just trusted from that other project.

Point both paths at your own `core/` + `interop/` (BepInEx install + Il2CppInterop
generator output for this exact `GameAssembly.dll` / `global-metadata.dat`) once
you have one, so this project doesn't depend on CoffeeNet's continuing to exist.

To target a different client version entirely: dump that build, regenerate
interop assemblies for it, point the csproj at that build's `core/`+`interop/`,
and rebuild. Every obfuscated name above will need re-finding in a decompiler
against the new dump - Rec Room renames them per build.

## Layout

- `Core.cs` - the plugin's entry point (`BepInPlugin`)
- `MochaBehaviour.cs` - the per-frame loop (config poll, maintenance countdown,
  scene-load cleanup) that 0414 ran from `MelonMod.OnUpdate`
- `MochaLog.cs` - thin wrapper over the plugin's `ManualLogSource`
- `HarmonyPatches.cs` - the actual patches (nameserver redirect, TLS bypass, EAC
  challenge-response bypass - no Photon patch, see above)
- `LaunchValidationFix.cs` - see "Launch validation fix" above
- `SteamCache.cs` - reads the local Steam id, for tagging requests
- `Integrity.cs` - the plugin's own tamper/debugger detection
- `MochaGuard/` - the MochaGuard anti-cheat watchdogs (see below)
- `Decoy.cs` - the `ObfuscatedByTopLevelOpsecObf` red-herring namespace (see
  Obfuscation)

## Obfuscation

Same posture as 0414's build.obfuscated.bat / Obfuscar.xml: everything renames
except Harmony's reflection-resolved method names and the `Load`/`Unload`
plugin entry points, strings are XOR-hidden, and `ObfuscatedByTopLevelOpsecObf`
is left readable on purpose as a decoy. `UseUnicodeNames` must stay `false` -
see 0414's README for why.

## MochaGuard

Same design as 0414 (see that README for the full detection-tier writeup); the
only change here is the plumbing that used to be MelonLoader-specific:

- `GuardRuntime.Start` took a `MelonMod host` in 0414 (to read
  `host.HarmonyInstance` and `host.GetType().Assembly`). Here it takes
  `(Harmony harmony, Assembly hostAssembly)` directly, called from `Core.Load()`
  as `GuardRuntime.Start(HarmonyInstance, typeof(Core).Assembly)`.
- `Integrity.CheckMelons()` enumerated `MelonLoader.MelonBase.RegisteredMelons`
  in 0414. It now walks `BepInEx.Bootstrap.Chainloader.PluginInfos` instead -
  same idea (flag any loaded plugin that isn't on the allow-list), different
  registry. Wrapped in the same try/catch every other check here already uses,
  so if this specific BepInEx API assumption is off it degrades to
  `Inconclusive` rather than crashing.
- `HarmonyPatchGuard`'s allowed-owner list keeps `"melonloader"` (harmless if
  unused) and adds `"bepinex"` alongside the existing `"il2cppinterop"` /
  `"harmony"` framework entries.

Do not ship the standalone `MochaGuard.dll` alongside this build - the guard
would run twice.

## Distribution

Build output (`bin/`, `obj/`, `dist/`) isn't committed. Publish the built DLL as
a release artifact instead.
