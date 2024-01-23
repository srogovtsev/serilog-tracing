using ActivityTracing.Library;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog;

const string programSourceName = "Program";

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(programSourceName)
    .AddSource(Worker.ActivitySourceName)
    .AddOtlpExporter()
    .Build();

using var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory
    .Create(builder => builder.AddSerilog(logger));

//end of configuration, let's go

using var source = new ActivitySource(programSourceName);

using var root = source.StartActivity();

logger.Information("Started");

new Worker(loggerFactory.CreateLogger<Worker>()).Run();
new Worker(loggerFactory.CreateLogger<Worker>()).Run();

logger.Information("Finished");
