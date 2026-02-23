using System.CommandLine;
using Spectre.Console;

namespace RoslynRag.Cli.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var command = new Command("init", "Create a roslyn-rag.json config file with defaults");

        command.SetAction(async (_, ct) =>
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), ConfigLoader.FileName);

            if (File.Exists(path))
            {
                AnsiConsole.MarkupLine($"[yellow]{ConfigLoader.FileName} already exists.[/]");
                return;
            }

            ConfigLoader.WriteDefaults(path);
            AnsiConsole.MarkupLine($"[green]Created {ConfigLoader.FileName} with default settings.[/]");
        });

        return command;
    }
}
