using Serilog;
using FluentMigrator.Runner;
using Moss.Services;
using Moss.Clients;
using Moss.Extensions;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication
    .CreateBuilder(args);

builder.Services.AddFluentMigratorCore()
    .ConfigureRunner(rb => rb
        .AddPostgres()
        .WithGlobalConnectionString(builder.Configuration.GetConnectionString("Database"))
        .ScanIn([typeof(Program).Assembly]))
    .AddLogging(lb => lb.AddFluentMigratorConsole());

builder.Services.Configure<OpcuaConfiguration>(builder.Configuration.GetSection("Opcua"));
builder.Services.AddOpenApi();
builder.Services.AddApiVersioning(setupAction => {
    setupAction.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
    setupAction.AssumeDefaultVersionWhenUnspecified = true;
    setupAction.ReportApiVersions = true;
});

builder.Services.AddControllers();
builder.Services.AddOpcuaClient();
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

using (var scope = app.Services.CreateScope()) {
    var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
    runner.MigrateUp();
}

app.Run();
