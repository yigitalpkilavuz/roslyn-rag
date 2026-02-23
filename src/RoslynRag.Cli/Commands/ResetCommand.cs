using System.CommandLine;
using RoslynRag.Core.Interfaces;
using Spectre.Console;

namespace RoslynRag.Cli.Commands;

public static class ResetCommand
{
    public static Command Create(
        Func<IVectorStore> vectorStoreFactory,
        Func<IKeywordIndex> keywordIndexFactory,
        Func<IIndexStateStore> stateStoreFactory)
    {
        var yesOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        var command = new Command("reset", "Delete all index data and start fresh") { yesOption };

        command.SetAction(async (parseResult, ct) =>
        {
            var yes = parseResult.GetValue(yesOption);
            if (!yes && !AnsiConsole.Confirm("This will delete all index data. Continue?", defaultValue: false))
                return;

            var hasErrors = false;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Resetting...", async ctx =>
                {
                    ctx.Status("Deleting Qdrant collection...");
                    try
                    {
                        var vectorStore = vectorStoreFactory();
                        await vectorStore.DeleteCollectionAsync(ct);
                        AnsiConsole.MarkupLine("  [green]Qdrant collection deleted.[/]");
                    }
                    catch (Exception ex)
                    {
                        hasErrors = true;
                        AnsiConsole.MarkupLine($"  [red]Qdrant failed:[/] {Markup.Escape(ex.Message)}");
                    }

                    ctx.Status("Deleting BM25 index...");
                    try
                    {
                        var keywordIndex = keywordIndexFactory();
                        keywordIndex.Initialize();
                        keywordIndex.DeleteAll();
                        AnsiConsole.MarkupLine("  [green]BM25 index deleted.[/]");
                    }
                    catch (Exception ex)
                    {
                        hasErrors = true;
                        AnsiConsole.MarkupLine($"  [red]BM25 failed:[/] {Markup.Escape(ex.Message)}");
                    }

                    ctx.Status("Deleting index state...");
                    try
                    {
                        var stateStore = stateStoreFactory();
                        await stateStore.DeleteAsync(ct);
                        AnsiConsole.MarkupLine("  [green]Index state deleted.[/]");
                    }
                    catch (Exception ex)
                    {
                        hasErrors = true;
                        AnsiConsole.MarkupLine($"  [red]State delete failed:[/] {Markup.Escape(ex.Message)}");
                    }
                });

            if (hasErrors)
                AnsiConsole.MarkupLine("[yellow]Reset completed with errors. Some data may remain.[/]");
            else
                AnsiConsole.MarkupLine("[green]Reset complete.[/]");
        });

        return command;
    }
}
