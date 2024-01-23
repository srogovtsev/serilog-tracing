// See https://aka.ms/new-console-template for more information

using System;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SerilogTracing;

using var logger = new LoggerConfiguration()
    .Enrich.WithProperty("Application", typeof(Program).Assembly.GetName().Name)
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger()
    ;

const string SourceName = "qqq";

using var source = new ActivitySource(SourceName);

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(source.Name)
    .AddSource("Serilog")
    //.AddConsoleExporter()
    .Build();

using var rootActivity = source.StartActivity();

logger.Information("Start");

using (var _ = logger.StartActivity("Activity 1: {prop}", "abc"))
{
    logger.Information("Inside activity 1");

    using (var inner = logger.StartActivity("Activity 2: {prop}", "qwe"))
    {
        logger.Information("Inside activity 2");
        inner.AddProperty("test", "value");
    }

    using (var inner = logger.StartActivity("Completed activity"))
    {
        inner.Complete();
    }

    using (var inner = logger.StartActivity("Errored activity"))
    {
        try
        {
            throw new InvalidOperationException("Test");
        }
        catch (Exception e)
        {
            inner.Complete(LogEventLevel.Error, e);
        }
    }
}

logger.Information("End");