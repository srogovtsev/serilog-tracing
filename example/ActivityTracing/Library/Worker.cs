namespace ActivityTracing.Library;

internal class Worker(ILogger<Worker> logger)
{
    //usually this would be separate, but let's pretend
    public const string ActivitySourceName = "MyLibrary";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Guid _id = Guid.NewGuid();

    public void Run()
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = ActivitySource.StartActivity(name: "Worker Run", kind: ActivityKind.Internal, tags: new Dictionary<string, object?>
        {
            {"WorkerId", _id}
        });
        logger.LogInformation("Worker {WorkerId} was run", _id);
    }
}