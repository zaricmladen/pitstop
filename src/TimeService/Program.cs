
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

IHost host = Host
    .CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        string serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "TimeService";

        services.UseRabbitMQMessagePublisher(hostContext.Configuration);

        services.AddHostedService<TimeWorker>();

         services.AddOpenTelemetry().WithTracing(tcb =>
        {
            tcb
            .AddSource(serviceName)
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: serviceName, serviceVersion: "1.0"))
            .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri("http://jaeger-collector:4317");
                    });
        });

        // Register the telemetry class as a singleton
        //services.AddSingleton<ITelemetry>(new Telemetry(serviceName));

    })
    /*.UseSerilog((hostContext, loggerConfiguration) =>
    {
        loggerConfiguration.ReadFrom.Configuration(hostContext.Configuration);
    })*/
    .UseConsoleLifetime()
    .Build();

await host.RunAsync();