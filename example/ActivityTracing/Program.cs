using ActivityTracing.Library;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

const string programSourceName = "Program";

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(programSourceName)
    .AddSource(Worker.ActivitySourceName)
    .AddSource("Serilog")
    .AddOtlpExporter()
    .Build();

using var logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Literate))
    .CreateLogger();

using var loggerFactory = LoggerFactory
    .Create(builder => builder.AddSerilog(logger));

//end of configuration, let's go

using var _ = new TracingConfiguration().EnableTracing();

using var source = new ActivitySource(programSourceName);

using var root = source.StartActivity();

using var serilog = logger.StartActivity("Serilog root");

logger.Information("Started");

new Worker(loggerFactory.CreateLogger<Worker>()).Run();
new Worker(loggerFactory.CreateLogger<Worker>()).Run();

logger.Information("Finished");
