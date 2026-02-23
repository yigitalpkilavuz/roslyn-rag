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
            var state = await stateStore.LoadAsync(ct);

            if (state is null)
            {
                AnsiConsole.MarkupLine("[yellow]No index found. Run 'index' first.[/]");
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("Index Status")
                .AddColumn("Property")
                .AddColumn("Value");

            table.AddRow("Solution", state.SolutionPath);
            table.AddRow("Last Commit", state.LastIndexedCommitSha ?? "N/A");
            table.AddRow("Indexed At", state.IndexedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
            table.AddRow("Total Chunks", state.TotalChunks.ToString("N0"));
            table.AddRow("Total Files", state.TotalFiles.ToString("N0"));
            table.AddRow("Embedding Model", state.EmbeddingModel);
            table.AddRow("Dimensions", state.EmbeddingDimensions.ToString());

            AnsiConsole.Write(table);

            try
            {
                var vectorStore = vectorStoreFactory();
                var pointCount = await vectorStore.GetPointCountAsync(ct);
                AnsiConsole.MarkupLine($"[green]Qdrant:[/] Connected ({pointCount:N0} points)");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Qdrant:[/] {Markup.Escape(ex.Message)}");
            }
        });

        return command;
    }
}
