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

> [!NOTE]
> `Directory.Build.props.user` is added to `.gitignore` and will never be committed to Git. This prevents leaking your local folder structures or Steam installation paths.

---

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

## Packaging Releases

We have included a release packaging script `build-releases.ps1`. Running this script will clean, compile all projects in `Release` configuration, and package the compiled DLLs into clean ZIP archives.

To package the mods:
1. Open PowerShell in the root of this workspace.
2. Run the script:
   ```powershell
   .\build-releases.ps1
   ```
3. The packaged zip files will be created in the `releases/` directory in the root of the workspace:
   - `releases/BaggageTagAnyMod-v1.0.0.zip`
   - `releases/CounterSorterMod-v1.0.0.zip`
   - `releases/InnovationLevelMod-v1.0.0.zip`

---

## Publishing to GitHub Releases

To upload these mods to GitHub Releases:

### Method 1: Manual Upload (GitHub Website)
1. Go to your mod's repository page on GitHub.
2. Click **Releases** -> **Draft a new release**.
3. Create a new tag (e.g. `v1.0.0`) and enter the release title/description.
4. Drag and drop the corresponding ZIP file from the `releases/` folder (e.g., `BaggageTagAnyMod-v1.0.0.zip`) into the release attachment area.
5. Click **Publish release**.

### Method 2: GitHub CLI (`gh`)
If you have the [GitHub CLI](https://cli.github.com/) installed, you can publish directly from your terminal:
```bash
# Tag and push the commit
git tag v1.0.0
git push origin v1.0.0

# Create release and upload the zip package
gh release create v1.0.0 releases/BaggageTagAnyMod-v1.0.0.zip --title "v1.0.0" --notes "Initial release"
```
