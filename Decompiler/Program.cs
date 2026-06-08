using System;
using System.IO;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

class Program
{
    static void Main(string[] args)
    {
        string gameDir = "";
        // Look for Directory.Build.props.user in the workspace root relative to output path
        string propsUserPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Directory.Build.props.user");
        if (File.Exists(propsUserPath))
        {
            var content = File.ReadAllText(propsUserPath);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"<GameDir>(.*?)</GameDir>");
            if (match.Success)
            {
                gameDir = match.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrEmpty(gameDir))
        {
            gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Airport Baggage Simulator";
        }

        string managedDir = Path.Combine(gameDir, "Airport Baggage Simulator_Data", "Managed");
        string dllPath = Path.Combine(managedDir, "eu.3rg.game.dll");
        
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"Error: Could not find game assembly at: {dllPath}");
            return;
        }

        string[] typesToDecompile = new[]
        {
            "_scripts._by_scene._game._upgrades.Upgradeable"
        };

        var decompiler = new CSharpDecompiler(dllPath, new DecompilerSettings());
        string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "scratch", "decompiled_tag_sorter.txt");
        
        string? outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        using (var writer = new StreamWriter(outputPath))
        {
            foreach (var typeName in typesToDecompile)
            {
                writer.WriteLine($"==========================================================================");
                writer.WriteLine($"TYPE: {typeName}");
                writer.WriteLine($"==========================================================================");
                try
                {
                    string code = decompiler.DecompileTypeAsString(new FullTypeName(typeName));
                    writer.WriteLine(code);
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"Error decompiling type {typeName}: {ex.Message}");
                }
                writer.WriteLine("\n\n");
            }
        }
        Console.WriteLine($"Decompilation completed! Results written to: {outputPath}");
    }
}
