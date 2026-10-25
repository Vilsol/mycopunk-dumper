# Mycopunk.GameLibs.Steam

Stripped (method bodies removed) and publicized game assemblies from Mycopunk's `Mycopunk_Data/Managed/`, for building mods without a game install (e.g. in CI).

Package version is `<game version>.<Steam build ID>`, e.g. `2.0.2.26120128`.

```xml
<PackageReference Include="Mycopunk.GameLibs.Steam" Version="2.0.2.*" PrivateAssets="all" />
```

`Assembly-CSharp` and `Assembly-CSharp-firstpass` are publicized: private members are public at compile time. To access them at runtime, set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in your project (Mono skips access checks for unverifiable assemblies).
