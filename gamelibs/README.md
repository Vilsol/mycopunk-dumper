# Mycopunk.GameLibs.Steam

Stripped (method bodies removed) and publicized game assemblies from Mycopunk's `Mycopunk_Data/Managed/`, for building mods without a game install (e.g. in CI).

Package version is the game version with the Steam build ID as SemVer metadata, e.g. `2.0.3+26165957`.

```xml
<PackageReference Include="Mycopunk.GameLibs.Steam" Version="2.0.3" PrivateAssets="all" />
```

`Assembly-CSharp` and `Assembly-CSharp-firstpass` are publicized: private members are public at compile time. To access them at runtime, set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in your project (Mono skips access checks for unverifiable assemblies).
