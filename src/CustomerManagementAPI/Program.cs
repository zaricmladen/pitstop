
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
string serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "CustomerManagementAPI";


builder.Services.AddSingleton(new ActivitySource(serviceName));
// Configure important OpenTelemetry settings, the console exporter, and instrumentation library
builder.Services.AddOpenTelemetry().WithTracing(tcb =>
{
    tcb
    .AddSource(serviceName)
    .SetResourceBuilder(
        ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: "1.0"))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddEntityFrameworkCoreInstrumentation()
    .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri("http://jaeger-collector:4317");
            });
});



// setup logging
/*builder.Host.UseSerilog((context, logContext) => 
    logContext
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.WithMachineName()
);*/

// add DBContext
var sqlConnectionString = builder.Configuration.GetConnectionString("CustomerManagementCN");
builder.Services.AddDbContext<CustomerManagementDBContext>(options => options.UseSqlServer(sqlConnectionString));

// add messagepublisher
builder.Services.UseRabbitMQMessagePublisher(builder.Configuration);

// Add framework services
builder.Services
    .AddMvc(options => options.EnableEndpointRouting = false)
    .AddNewtonsoftJson();

// Register the Swagger generator, defining one or more Swagger documents
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CustomerManagement API", Version = "v1" });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CustomerManagementDBContext>();

// setup MVC
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMvc();
app.UseDefaultFiles();
app.UseStaticFiles();

// Enable middleware to serve generated Swagger as a JSON endpoint.
app.UseSwagger();

// Enable middleware to serve swagger-ui (HTML, JS, CSS etc.), specifying the Swagger JSON endpoint.
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CustomerManagement API - v1");
});

// auto migrate db
using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    scope.ServiceProvider.GetService<CustomerManagementDBContext>().MigrateDB();
}

app.UseHealthChecks("/hc");

app.MapControllers();

app.Run();
