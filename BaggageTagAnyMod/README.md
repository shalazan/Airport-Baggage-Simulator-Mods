# Baggage Tag Sorter "Any" Setting Mod
A BepInEx mod for *Airport Baggage Simulator* that upgrades the Sorter (Tag) component with a fourth setting, "Any", allowing it to divert any tagged baggage (red, yellow, or green).

## Developer Setup
Since this mod references proprietary game assemblies, they are configured to look for game libraries in a directory defined dynamically via MSBuild.

To compile the projects locally:
1. Create a file named `Directory.Build.props.user` in the root of the workspace directory.
2. Define the `<GameDir>` property with the absolute path to your game installation directory.

Example `Directory.Build.props.user`:
```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Airport Baggage Simulator</GameDir>
  </PropertyGroup>
</Project>
```

---

## AI Attribution & Code Generation Notice

This repository contains source code that was generated and designed with the assistance of **Antigravity**, an agentic AI coding assistant designed by Google DeepMind.
