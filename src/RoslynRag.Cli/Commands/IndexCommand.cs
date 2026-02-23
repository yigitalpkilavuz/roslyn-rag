using System.CommandLine;
using RoslynRag.Indexing;
using Spectre.Console;

namespace RoslynRag.Cli.Commands;

public static class IndexCommand
{
    public static Command Create(Func<IndexingPipeline> pipelineFactory)
    {
        var solutionArg = new Argument<FileInfo>("solution")
        {
            Description = "Path to the .sln file"
        };
        var fullOption = new Option<bool>("--full")
        {
            Description = "Force full re-index"
        };

        var command = new Command("index", "Index a .NET solution for code intelligence")
        {
            solutionArg,
            fullOption
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var solution = parseResult.GetValue(solutionArg)!;
            var full = parseResult.GetValue(fullOption);

            if (!solution.Exists)
            {
                AnsiConsole.MarkupLine($"[red]Solution file not found:[/] {solution.FullName}");
                return;
            }

            var pipeline = pipelineFactory();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Indexing...", async ctx =>
                {
                    var statusProgress = new Progress<string>(msg =>
                    {
                        ctx.Status(msg);
                        AnsiConsole.MarkupLine($"  [grey]{EscapeMarkup(msg)}[/]");
                    });

                    var embeddingProgress = new Progress<(int completed, int total)>(p =>
                    {
                        ctx.Status($"Embedding [[{p.completed}/{p.total}]]");
                    });

                    await pipeline.IndexAsync(
                        solution.FullName,
                        forceFullIndex: full,
                        statusProgress: statusProgress,
                        embeddingProgress: embeddingProgress,
                        ct: ct);
                });

            AnsiConsole.MarkupLine("[green]Indexing complete.[/]");
        });

        return command;
    }

    private static string EscapeMarkup(string text) =>
        text.Replace("[", "[[").Replace("]", "]]");
}
