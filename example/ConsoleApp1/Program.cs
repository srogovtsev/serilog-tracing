// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using SerilogTracing;
using SerilogTracing.Interop;

using var logger = new LoggerConfiguration()
    .Enrich.WithProperty("Application", typeof(Program).Assembly.GetName().Name)
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger()
    ;

using var source = new ActivitySource("qqq");

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(source.Name)
    .AddSource(LoggerActivitySource.Instance.Name)
    .AddConsoleExporter()
    .Build();

using var rootActivity = source.StartActivity();

logger.Information("Start");

using (var a1 = logger.StartActivity("Activity 1: {prop}", "abc"))
{
    logger.Information("Inside activity 1");

    using (var a2 = logger.StartActivity("Activity 2: {prop}", "qwe"))
    {
        logger.Information("Inside activity 2");
        a2.AddProperty("test", "value");
    }
}

logger.Information("End");