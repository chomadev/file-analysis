using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FileAnalysis.CLI;

/// <summary>
/// Minimal terminal chat REPL: an Ollama model (with native tool-calling) wired directly to the
/// FileAnalysis MCP server over stdio. No Open WebUI, no bridge process — just this and Ollama.
/// </summary>
public static class ChatSession
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string mcpDllPath, string ollamaBaseUrl, string model, CancellationToken ct)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "FileAnalysis MCP",
            Command = "dotnet",
            Arguments = [mcpDllPath],
        });

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: ct);

        Console.WriteLine($"Tools carregadas: {string.Join(", ", tools.Select(t => t.Name))}");
        Console.WriteLine("Digite sua pergunta (ou 'sair' para encerrar).\n");

        var ollamaTools = tools.Select(t => new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = JsonNode.Parse(t.JsonSchema.GetRawText()),
            },
        }).ToArray();

        using var http = new HttpClient { BaseAddress = new Uri(ollamaBaseUrl), Timeout = TimeSpan.FromMinutes(3) };
        var messages = new JsonArray();

        while (!ct.IsCancellationRequested)
        {
            Console.Write("voce> ");
            var input = Console.ReadLine();
            if (input is null)
                break;
            if (input.Trim().Equals("sair", StringComparison.OrdinalIgnoreCase))
                break;
            if (string.IsNullOrWhiteSpace(input))
                continue;

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = input });

            for (var round = 0; round < 6; round++)
            {
                var request = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = JsonNode.Parse(messages.ToJsonString()),
                    ["tools"] = new JsonArray(ollamaTools.Select(t => JsonNode.Parse(t.ToJsonString())).ToArray()),
                    ["stream"] = false,
                    ["think"] = false,
                };

                using var response = await http.PostAsJsonAsync("api/chat", request, JsonOptions, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Ollama returned an empty response.");
                var message = body["message"]!.AsObject();
                messages.Add(JsonNode.Parse(message.ToJsonString())!);

                var toolCalls = message["tool_calls"]?.AsArray();
                if (toolCalls is null || toolCalls.Count == 0)
                {
                    Console.WriteLine($"\nqwen3> {message["content"]}\n");
                    break;
                }

                foreach (var call in toolCalls)
                {
                    var fnName = call!["function"]!["name"]!.GetValue<string>();
                    var fnArgsNode = call["function"]!["arguments"]!.AsObject();
                    Console.WriteLine($"  [tool] {fnName}({fnArgsNode.ToJsonString()})");

                    var arguments = fnArgsNode.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

                    string resultText;
                    try
                    {
                        var result = await mcpClient.CallToolAsync(fnName, arguments, cancellationToken: ct);
                        resultText = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
                    }
                    catch (Exception ex)
                    {
                        resultText = $"error calling tool: {ex.Message}";
                    }

                    messages.Add(new JsonObject { ["role"] = "tool", ["content"] = resultText, ["name"] = fnName });
                }
            }
        }
    }
}
