
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

IHost host = Host
    .CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        string serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "WorkshopManagementEventHandler";
        services.UseRabbitMQMessageHandler(hostContext.Configuration);

        services.AddTransient<WorkshopManagementDBContext>((svc) =>
        {
            var sqlConnectionString = hostContext.Configuration.GetConnectionString("WorkshopManagementCN");
            var dbContextOptions = new DbContextOptionsBuilder<WorkshopManagementDBContext>()
                .UseSqlServer(sqlConnectionString)
                .Options;
            var dbContext = new WorkshopManagementDBContext(dbContextOptions);

            DBInitializer.Initialize(dbContext);

            return dbContext;
        });

        services.AddHostedService<EventHandlerWorker>();

        services.AddOpenTelemetry().WithTracing(tcb =>
        {
            tcb
            .AddSource(serviceName)
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: serviceName, serviceVersion: "1.0"))
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri("http://jaeger-collector:4317");
                    });
        });

    })
    /*.UseSerilog((hostContext, loggerConfiguration) =>
    {
        loggerConfiguration.ReadFrom.Configuration(hostContext.Configuration);
    })*/
    .UseConsoleLifetime()
    .Build();

await host.RunAsync();