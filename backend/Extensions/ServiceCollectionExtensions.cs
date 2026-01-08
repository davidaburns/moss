namespace Moss.Extensions;

using Moss.Clients;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddOpcuaClient(this IServiceCollection services, Action<OpcuaConfiguration>? configure = null) {
        if (configure != null) {
            Console.WriteLine("Configuring service");
            services.Configure(configure);
        }

        services.AddSingleton<IOpcuaClient, OpcuaClient>();
        return services;
    }
}
