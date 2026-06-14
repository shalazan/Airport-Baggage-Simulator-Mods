// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using System.Reflection.Metadata;

class Program
{
    static void Main(string[] args)
    {
        string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Airport Baggage Simulator\Airport Baggage Simulator_Data\Managed";
        string scratchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch");
        Directory.CreateDirectory(scratchDir);

        string dll = Path.Combine(gameDir, "eu.3rg.game.dll");
        try
        {
            var assembly = Assembly.LoadFrom(dll);
            Console.WriteLine($"--- Loaded assembly: {assembly.FullName} ---");
            foreach (var type in assembly.GetTypes())
            {
                string name = type.FullName.ToLower();
                if (name.Contains("flip") || name.Contains("direction") || name.Contains("automat") || name.Contains("sorter") || name.Contains("airport"))
                {
                    Console.WriteLine($"Found Type: {type.FullName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading {dll}: {ex.Message}");
        }

        string[] dllPaths = new[]
        {
            Path.Combine(gameDir, "eu.3rg.game.dll")
        };

        foreach (var dllPath in dllPaths)

        {
            if (!File.Exists(dllPath))
            {
                Console.WriteLine($"DLL not found: {dllPath}");
                continue;
            }

            Console.WriteLine($"Analyzing {Path.GetFileName(dllPath)}...");
            using (var fileStream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var peFile = new PEFile(dllPath, fileStream);
                var metadata = peFile.Metadata;
                var decompiler = new CSharpDecompiler(dllPath, new DecompilerSettings());

                var matchingTypes = new List<TypeDefinitionHandle>();
                foreach (var typeHandle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(typeHandle);
                    string name = metadata.GetString(type.Name);
                    string ns = metadata.GetString(type.Namespace);
                    string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    bool matches = true;

                    if (matches)
                    {
                        matchingTypes.Add(typeHandle);
                    }
                }

                Console.WriteLine($"Found {matchingTypes.Count} matching types. Decompiling them to scratch folder...");
                string dllScratch = Path.Combine(scratchDir, Path.GetFileNameWithoutExtension(dllPath));
                Directory.CreateDirectory(dllScratch);

                foreach (var typeHandle in matchingTypes)
                {
                    var type = metadata.GetTypeDefinition(typeHandle);
                    string name = metadata.GetString(type.Name);
                    string ns = metadata.GetString(type.Namespace);
                    string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    // Escape invalid characters in file name
                    string safeName = fullName.Replace('`', '_').Replace('/', '_').Replace('\\', '_')
                                              .Replace("<", "_").Replace(">", "_")
                                              .Replace(":", "_").Replace("*", "_")
                                              .Replace("?", "_").Replace("|", "_")
                                              .Replace("\"", "_");
                    string outPath = Path.Combine(dllScratch, $"{safeName}.cs");

                    try
                    {
                        string code = decompiler.DecompileAsString(typeHandle);
                        File.WriteAllText(outPath, code);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(outPath, $"// Decompilation failed: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }

        Console.WriteLine("Done! Check files in scratch directory.");
    }
}

