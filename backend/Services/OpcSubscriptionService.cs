namespace Moss.Services;

using Opc.Ua;
using Moss.Clients;

public class OpcSubscriptionService : BackgroundService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpcSubscriptionService> _logger;
    private IOpcUaClient _client;

    public OpcSubscriptionService(
        IOpcUaClient opcClient,
        IConfiguration configuration,
        ILogger<OpcSubscriptionService> logger
    ) {
        _client = opcClient;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellation) {
        _logger.LogInformation("Starting opc subscription service");

        var configSection = _configuration.GetSection("Opcua");
        string? discoveryUrl = configSection.GetValue<string>("DiscoveryUrl");
        string? username = configSection.GetValue<string>("Username");
        string? password = configSection.GetValue<string>("Password");


        if (discoveryUrl is null) {
            throw new ArgumentNullException("Opcua DiscoveryUrl must be provided in configuration");
        }
        if (username is null) {
            throw new ArgumentNullException("Opcua Username must be provided in configuration");
        }
        if (password is null) {
            throw new ArgumentNullException("Opcua Password must be provided in configuration");
        }

        await _client.ConnectAsync(discoveryUrl, new UserIdentity(username, password));
        while (!cancellation.IsCancellationRequested) {
            await Task.Delay(10);
        }

        _logger.LogInformation("Stopping opc subcription service");
    }
}
