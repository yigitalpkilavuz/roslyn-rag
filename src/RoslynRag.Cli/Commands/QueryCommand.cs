using System.CommandLine;
using RoslynRag.Core.Models;
using RoslynRag.Query;
using Spectre.Console;

namespace RoslynRag.Cli.Commands;

public static class QueryCommand
{
    public static Command Create(Func<QueryPipeline> pipelineFactory, RoslynRagConfig config)
    {
        var questionArg = new Argument<string>("question")
        {
            Description = "The question to ask about the codebase"
        };
        var topKOption = new Option<int>("--top-k")
        {
            Description = "Number of results to retrieve",
            DefaultValueFactory = _ => 10
        };
        var noLlmOption = new Option<bool>("--no-llm")
        {
            Description = "Return raw chunks without LLM synthesis"
        };
        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Filter results to a specific solution"
        };

        var command = new Command("query", "Ask a question about the indexed codebase")
        {
            questionArg,
            topKOption,
            noLlmOption,
            solutionOption
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var question = parseResult.GetValue(questionArg)!;
            var topK = parseResult.GetValue(topKOption);
            var noLlm = parseResult.GetValue(noLlmOption);
            var solutionFilter = parseResult.GetValue(solutionOption);
            if (solutionFilter is not null)
                solutionFilter = Path.GetFullPath(solutionFilter);

            if (!await HealthCheck.ValidateAsync(
                    config.Qdrant.Host, config.Qdrant.RestPort,
                    config.Ollama.BaseUrl,
                    [config.Ollama.EmbeddingModel, config.Ollama.LlmModel], ct))
                return;

            var pipeline = pipelineFactory();
            QueryResult? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Searching...", async _ =>
                {
                    result = await pipeline.QueryAsync(question, topK, useLlm: !noLlm, solutionId: solutionFilter, ct: ct);
                });

            if (result is null) return;

            if (result.Answer is not null)
            {
                AnsiConsole.WriteLine();
                var panel = new Panel(Markup.Escape(result.Answer))
                {
                    Header = new PanelHeader("Answer"),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(2, 1)
                };
                AnsiConsole.Write(panel);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Sources:[/]");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("#")
                .AddColumn("File")
                .AddColumn("Lines")
                .AddColumn("Method")
                .AddColumn("Score");

            for (var i = 0; i < result.Sources.Count; i++)
            {
                var s = result.Sources[i];
                var label = !string.IsNullOrEmpty(s.MethodName)
                    ? $"{s.ClassName}.{s.MethodName}"
                    : s.ClassName;

                table.AddRow(
                    $"[bold]{i + 1}[/]",
                    Markup.Escape(s.FilePath),
                    $"{s.StartLine}-{s.EndLine}",
                    Markup.Escape(label),
                    $"{s.FusedScore:F4}");
            }

            AnsiConsole.Write(table);
        });

        return command;
    }
}
