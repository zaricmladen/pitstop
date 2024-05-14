
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

IHost host = Host
    .CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        string serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "NotificationService";

        services.UseRabbitMQMessageHandler(hostContext.Configuration);

        services.AddTransient<INotificationRepository>((svc) =>
        {
            var sqlConnectionString = hostContext.Configuration.GetConnectionString("NotificationServiceCN");
            return new SqlServerNotificationRepository(sqlConnectionString);
        });

        services.AddTransient<IEmailNotifier>((svc) =>
        {
            var mailConfigSection = hostContext.Configuration.GetSection("Email");
            string mailHost = mailConfigSection["Host"];
            int mailPort = Convert.ToInt32(mailConfigSection["Port"]);
            string mailUserName = mailConfigSection["User"];
            string mailPassword = mailConfigSection["Pwd"];
            return new SMTPEmailNotifier(mailHost, mailPort, mailUserName, mailPassword);
        });

        services.AddHostedService<NotificationWorker>();

        services.AddOpenTelemetry().WithTracing(tcb =>
        {
            tcb
            .AddSource(serviceName)
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: serviceName, serviceVersion: "1.0"))
            .AddSqlClientInstrumentation()
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