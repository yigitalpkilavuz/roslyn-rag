using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynRag.Core.Models;
using Spectre.Console;

namespace RoslynRag.Cli;

internal static class ConfigLoader
{
    public const string FileName = "roslyn-rag.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        TypeInfoResolver = ConfigJsonContext.Default
    };

    public static RoslynRagConfig Load()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), FileName);

        if (!File.Exists(path))
            return new RoslynRagConfig();

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.RoslynRagConfig);
            return config ?? new RoslynRagConfig();
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Invalid {FileName}: {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Using default configuration.[/]");
            return new RoslynRagConfig();
        }
    }

    public static void WriteDefaults(string path)
    {
        var config = new RoslynRagConfig();
        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.RoslynRagConfig);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

[JsonSerializable(typeof(RoslynRagConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
internal partial class ConfigJsonContext : JsonSerializerContext;
