# Airport Baggage Simulator Mods

This workspace contains source code for three BepInEx mods for *Airport Baggage Simulator*:
1. **BaggageTagAnyMod**: Adds a fourth setting ("Any") to the Sorter (Tag) component to route all tagged baggage.
2. **CounterSorterMod**: Adds automated features, upgrades, and direction options to counter sorters.
3. **InnovationLevelMod**: Extends the "Innovation" player skill with 4 additional upgrade levels.

A utility project **Decompiler** is also included to assist in decompiling game assemblies for reference.

---

## Developer Setup

Since these mods reference proprietary game assemblies, they are configured to look for game libraries in a directory defined dynamically via MSBuild. 

To compile the projects locally:

1. Create a file named `Directory.Build.props.user` in the root of this workspace directory.
2. Define the `<GameDir>` property with the absolute path to your game installation directory. 

Example `Directory.Build.props.user`:
```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Airport Baggage Simulator</GameDir>
  </PropertyGroup>
</Project>
```

## Local Compilation

Once configured, you can build all projects from the terminal:
```bash
dotnet build
```

Or target a specific mod:
```bash
dotnet build BaggageTagAnyMod
```

---

## AI Attribution & Code Generation Notice

This repository contains source code that was generated and designed with the assistance of **Antigravity**, an agentic AI coding assistant designed by Google DeepMind.

