using FileAnalysis.Infrastructure;
using FileAnalysis.MCP.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// An MCP client may launch this process from any working directory, so resolve config
// relative to the executable's own directory rather than relying on the default CWD-based lookup.
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);

// stdio transport uses stdout for the JSON-RPC protocol stream, so all logging must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddFileAnalysisInfrastructure(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FileSearchTools>()
    .WithTools<CleanupTools>();

await builder.Build().RunAsync();
