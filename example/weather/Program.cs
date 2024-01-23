﻿using System.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;
using SerilogTracing.Instrumentation;
using SerilogTracing.Sinks.OpenTelemetry;

using var source = new ActivitySource("external");

Log.Logger = new LoggerConfiguration()
    .Enrich.WithProperty("Application", typeof(Program).Assembly.GetName().Name)
    .MinimumLevel.Override(source.Name, LogEventLevel.Verbose)
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
    // .WriteTo.SeqTracing("http://localhost:5341")
    // .WriteTo.Zipkin("http://localhost:9411")
    // .WriteTo.OpenTelemetry("http://localhost:5341/ingest/otlp/v1/logs", "http://localhost:5341/ingest/otlp/v1/traces", OtlpProtocol.HttpProtobuf, null, new Dictionary<string, object>()
    // {
    //     { "service.name", typeof(Program).Assembly.GetName().Name ?? "unknown_service" }
    // })
    .CreateLogger();

using var _ = new TracingConfiguration()
    //.Instrument.With(new ActivitySourceInstrumentor(source.Name))
    .EnableTracing();

if (args.Length != 1)
{
    Console.WriteLine("Usage: weather <POSTCODE>");
    return 1;
}

var postcode = args[0];

using var rootActivity = source.StartActivity();
Console.WriteLine(Activity.Current?.DisplayName);

using var activity = Log.Logger.StartActivity("Request weather for postcode {Postcode}", postcode);

try
{
    var weatherClient = new HttpClient { BaseAddress = new("http://localhost:5133") };
    var forecast = await weatherClient.GetStringAsync(postcode);
    Console.WriteLine(forecast);

    activity.Complete();
    return 0;
}
catch (Exception ex)
{
    activity.Complete(LogEventLevel.Fatal, ex);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <inheritdoc />
public class ActivitySourceInstrumentor : IActivityInstrumentor
{
    private readonly string _name;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="name"></param>
    public ActivitySourceInstrumentor(string name)
    {
        _name = name;
    }

    /// <inheritdoc />
    public bool ShouldSubscribeTo(string diagnosticListenerName)
    {
        Console.WriteLine(diagnosticListenerName);
        return false;
    }

    /// <inheritdoc />
    public void InstrumentActivity(Activity activity, string eventName, object eventArgs)
    {
    }
}

