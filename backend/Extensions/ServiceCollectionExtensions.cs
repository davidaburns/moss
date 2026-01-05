namespace Moss.Extensions;

using Moss.Clients;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddOpcUaClient(this IServiceCollection services, Action<OpcUaConfiguration>? configure = null) {
        if (configure != null) {
            services.Configure(configure);
        }

        services.AddSingleton<IOpcUaClient, OpcUaClient>();
        return services;
    }
}
