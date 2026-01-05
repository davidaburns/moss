using Serilog;
using Moss.Services;
using Moss.Clients;
using Moss.Extensions;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<OpcUaConfiguration>(builder.Configuration.GetSection("OpcUa"));

builder.Services.AddOpenApi();
builder.Services.AddApiVersioning(setupAction => {
    setupAction.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    setupAction.AssumeDefaultVersionWhenUnspecified = true;
    setupAction.ReportApiVersions = true;
});
builder.Services.AddControllers();
builder.Services.AddOpcUaClient();
builder.Services.AddHostedService<OpcSubscriptionService>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();

#pragma warning disable ASP0014
app.UseEndpoints(endpoints => {
    endpoints.MapControllers();
});

app.Run();
