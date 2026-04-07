using OTelWizard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(4317, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var app = builder.Build();

app.MapGrpcService<TraceServiceImpl>();
app.MapGrpcService<MetricsServiceImpl>();
app.MapGrpcService<LogsServiceImpl>();

Console.ForegroundColor = ConsoleColor.DarkMagenta;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║          OTel Wizard — Telemetry Viewer                 ║");
Console.WriteLine("║  Listening for OTLP on localhost:4317 (gRPC)            ║");
Console.WriteLine("║  Start ComplimentGenerator in another terminal.         ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

app.Run();
