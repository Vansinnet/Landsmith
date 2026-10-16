# Building Landsmith

This repository mirrors the source of Landsmith 0.1.0 from the author's mod workspace (commit 93fd745). Releases are built and validated there; the downloads are on the [releases page](https://github.com/Vansinnet/Landsmith/releases).

Requirements: the .NET SDK (10 or newer) and Valheim 1.0.20 with BepInEx 5.4.23.5.

1. Create `reference/assemblies/` in the repository root and copy these assemblies into it from `Valheim/valheim_Data/Managed/` and `Valheim/BepInEx/core/`: `BepInEx.dll`, `0Harmony.dll`, `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.TextRenderingModule.dll`. Alternatively pass `-p:ValheimReferenceDir=<folder>/` to the build.
2. Build: `dotnet build src/Landsmith/Landsmith.csproj -c Release`. The plugin is `src/Landsmith/bin/Release/net472/Landsmith.dll`.
3. Tests (no game needed): `dotnet run --project tests/Landsmith.Tests -c Release`.

Never commit game, Unity or BepInEx assemblies. Engine-facing details are in `src/Landsmith/docs/CONTRACTS.md`, the in-game test plan in `src/Landsmith/docs/TESTING.md`.
