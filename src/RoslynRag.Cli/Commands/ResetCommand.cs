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
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Reset a specific solution (resets all if omitted)"
        };

        var command = new Command("reset", "Delete index data and start fresh") { yesOption, solutionOption };

        command.SetAction(async (parseResult, ct) =>
        {
            var yes = parseResult.GetValue(yesOption);
            var solutionPath = parseResult.GetValue(solutionOption);

            if (solutionPath is not null)
            {
                await ResetSolutionAsync(solutionPath, yes, vectorStoreFactory, keywordIndexFactory, stateStoreFactory, ct);
            }
            else
            {
                await ResetAllAsync(yes, vectorStoreFactory, keywordIndexFactory, stateStoreFactory, ct);
            }
        });

        return command;
    }

    private static async Task ResetSolutionAsync(
        string solutionPath,
        bool yes,
        Func<IVectorStore> vectorStoreFactory,
        Func<IKeywordIndex> keywordIndexFactory,
        Func<IIndexStateStore> stateStoreFactory,
        CancellationToken ct)
    {
        var absolutePath = Path.GetFullPath(solutionPath);
        var displayName = Path.GetFileName(absolutePath);

        if (!yes && !AnsiConsole.Confirm($"Reset index for [bold]{Markup.Escape(displayName)}[/]?", defaultValue: false))
            return;

        var hasErrors = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Resetting...", async ctx =>
            {
                ctx.Status("Deleting Qdrant chunks...");
                try
                {
                    var vectorStore = vectorStoreFactory();
                    await vectorStore.DeleteBySolutionIdAsync(absolutePath, ct);
                    AnsiConsole.MarkupLine($"  [green]Qdrant chunks deleted for {Markup.Escape(displayName)}.[/]");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    AnsiConsole.MarkupLine($"  [red]Qdrant failed:[/] {Markup.Escape(ex.Message)}");
                }

                ctx.Status("Deleting BM25 chunks...");
                try
                {
                    var keywordIndex = keywordIndexFactory();
                    keywordIndex.Initialize();
                    keywordIndex.DeleteBySolutionId(absolutePath);
                    AnsiConsole.MarkupLine($"  [green]BM25 chunks deleted for {Markup.Escape(displayName)}.[/]");
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
                    await stateStore.DeleteAsync(absolutePath, ct);
                    AnsiConsole.MarkupLine($"  [green]State deleted for {Markup.Escape(displayName)}.[/]");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    AnsiConsole.MarkupLine($"  [red]State delete failed:[/] {Markup.Escape(ex.Message)}");
                }
            });

        AnsiConsole.MarkupLine(hasErrors
            ? "[yellow]Reset completed with errors.[/]"
            : "[green]Reset complete.[/]");
    }

    private static async Task ResetAllAsync(
        bool yes,
        Func<IVectorStore> vectorStoreFactory,
        Func<IKeywordIndex> keywordIndexFactory,
        Func<IIndexStateStore> stateStoreFactory,
        CancellationToken ct)
    {
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
                    await stateStore.DeleteAllAsync(ct);
                    AnsiConsole.MarkupLine("  [green]Index state deleted.[/]");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    AnsiConsole.MarkupLine($"  [red]State delete failed:[/] {Markup.Escape(ex.Message)}");
                }
            });

        AnsiConsole.MarkupLine(hasErrors
            ? "[yellow]Reset completed with errors. Some data may remain.[/]"
            : "[green]Reset complete.[/]");
    }
}
