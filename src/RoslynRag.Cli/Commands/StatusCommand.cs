using System.CommandLine;
using RoslynRag.Core.Interfaces;
using Spectre.Console;

namespace RoslynRag.Cli.Commands;

public static class StatusCommand
{
    public static Command Create(Func<IIndexStateStore> stateStoreFactory, Func<IVectorStore> vectorStoreFactory)
    {
        var command = new Command("status", "Show index status and health");

        command.SetAction(async (_, ct) =>
        {
            var stateStore = stateStoreFactory();
            var state = await stateStore.LoadAllAsync(ct);

            if (state.Solutions.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No index found. Run 'index' first.[/]");
                return;
            }

            foreach (var (_, solutionState) in state.Solutions.OrderBy(s => s.Key))
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"[bold]{Markup.Escape(Path.GetFileName(solutionState.SolutionPath))}[/]")
                    .AddColumn("Property")
                    .AddColumn("Value");

                table.AddRow("Solution", Markup.Escape(solutionState.SolutionPath));
                table.AddRow("Last Commit", solutionState.LastIndexedCommitSha ?? "N/A");
                table.AddRow("Indexed At", solutionState.IndexedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                table.AddRow("Total Chunks", solutionState.TotalChunks.ToString("N0"));
                table.AddRow("Total Files", solutionState.TotalFiles.ToString("N0"));
                table.AddRow("Embedding Model", solutionState.EmbeddingModel);
                table.AddRow("Dimensions", solutionState.EmbeddingDimensions.ToString());

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();
            }

            try
            {
                var vectorStore = vectorStoreFactory();
                var pointCount = await vectorStore.GetPointCountAsync(ct);
                AnsiConsole.MarkupLine($"[green]Qdrant:[/] Connected ({pointCount:N0} points total)");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Qdrant:[/] {Markup.Escape(ex.Message)}");
            }
        });

        return command;
    }
}
