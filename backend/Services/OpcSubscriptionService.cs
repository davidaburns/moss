namespace Moss.Services;

using Opc.Ua;
using Moss.Clients;

public class OpcSubscriptionService : BackgroundService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpcSubscriptionService> _logger;
    private IOpcuaClient _client;

    public OpcSubscriptionService(
        IOpcuaClient opcClient,
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

        var connected = await _client.ConnectAsync(discoveryUrl, new UserIdentity(username, password));
        if (connected) {
            _logger.LogInformation("Successfully connected to opcua server");
        };

        var subscriptionConfig = new OpcuaSubscriptionConfiguration {
            DisplayName="TEST_SUBSCRIPTION",
            PublishingEnabled=true
        };

        while (!cancellation.IsCancellationRequested) {
            await Task.Delay(10);
        }

        if (_client.IsConnected()) {
            await _client.DisconnectAsync(false, cancellation);
        }

        _logger.LogInformation("Stopping opc subcription service");
    }

    private void SubscriptionEventHandler(OpcuaSubscriptionEventArgs args) {
        _logger.LogInformation(
            "MonitoredItemNotification[{0}]: {1}, Previous={2}, Current={3}, Source={4}, Local={5}",
            args.Sequence,
            args.Node,
            args.PreviousValue,
            args.Value,
            args.SourceTimestamp,
            args.LocalTimestamp
        );
    }
}
