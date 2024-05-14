
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

IHost host = Host
    .CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        string serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "InvoiceService";

        services.UseRabbitMQMessageHandler(hostContext.Configuration);

        services.AddTransient<IInvoiceRepository>((svc) =>
        {
            var sqlConnectionString = hostContext.Configuration.GetConnectionString("InvoiceServiceCN");
            return new SqlServerInvoiceRepository(sqlConnectionString);
        });

        services.AddTransient<IEmailCommunicator>((svc) =>
        {
            var mailConfigSection = hostContext.Configuration.GetSection("Email");
            string mailHost = mailConfigSection["Host"];
            int mailPort = Convert.ToInt32(mailConfigSection["Port"]);
            string mailUserName = mailConfigSection["User"];
            string mailPassword = mailConfigSection["Pwd"];
            return new SMTPEmailCommunicator(mailHost, mailPort, mailUserName, mailPassword);
        });

        services.AddHostedService<InvoiceWorker>();

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
