namespace Moss.Clients;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

public interface IOpcuaClient {
    public Task<bool> ConnectAsync(string url, IUserIdentity identity, bool useSecurity = true, CancellationToken cancellation = default);
    public Task DisconnectAsync(bool leaveChannelOpen, CancellationToken cancellation = default);
    public Task<DataValueCollection?> ReadAsync(NodeId node, CancellationToken cancellation = default);
    public Task SubscribeTo(
        NodeId node,
        uint nodeAttribute,
        Action<OpcuaSubscriptionEventArgs> eventHandler,
        OpcuaSubscriptionConfiguration? config = null,
        bool durable = false,
        CancellationToken cancellation = default
    );

    public bool IsConnected();
}

public record OpcuaConfiguration {
    public string ApplicationName { get; set; } = "MyClient";
    public ApplicationType ApplicationType { get; set; } = ApplicationType.Client;
    public bool AddAppCertToTrustedStore { get; set; } = true;
    public bool AutoAcceptUntrustedCertificates { get; set; } = false;
    public bool AutoAcceptClients { get; set; } = true;
    public string CertificatePath { get; set; } = ".certs/share/opc-foundation/pki";
    public int TransportOperationTimeout { get; set; } = 15000;
    public int DefaultSessionTimeout { get; set; } = 60000;
    public int KeepAliveInterval { get; set; } = 5000;
    public int ReconnectPeriod { get; set; } = 1000;
    public int ReconnectPeriodBackoff { get; set; } = 15000;
    public uint SessionLifetime { get; set; } = 60 * 1000;

    public ApplicationConfiguration ToApplicationConfiguration() {
        Console.WriteLine(CertificatePath);
        return new ApplicationConfiguration {
            ApplicationName = ApplicationName,
            ApplicationType = ApplicationType,
            ApplicationUri = Utils.Format(@"urn:{0}:{1}", System.Net.Dns.GetHostName(), ApplicationName),
            SecurityConfiguration = new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier {
                    StoreType = "Directory",
                    StorePath = $"{CertificatePath}/certificate-stores/machine-default",
                    SubjectName = "MyClientSubjectName",
                },
                TrustedIssuerCertificates = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = $"{CertificatePath}/certificate-stores/ua-certificate-authorities",
                },
                TrustedPeerCertificates = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = $"{CertificatePath}/certificate-stores/ua-applications",
                },
                RejectedCertificateStore = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = $"{CertificatePath}/certificate-stores/rejected-certificates",
                },
                AddAppCertToTrustedStore = AddAppCertToTrustedStore,
                AutoAcceptUntrustedCertificates = AutoAcceptUntrustedCertificates,

            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas {
                OperationTimeout = TransportOperationTimeout
            },
            ClientConfiguration = new ClientConfiguration {
                DefaultSessionTimeout = DefaultSessionTimeout,
            },
            TraceConfiguration = new TraceConfiguration()
        };
    }
}

public record OpcuaSubscriptionConfiguration {
    public string DisplayName = "DEFAULT_SUBSCRIPTION_NAME";
    public bool PublishingEnabled = true;
    public bool SequentialPublishing = true;
    public int PublishingInterval = 100;
    public int MaxNotificationsPerPublish = 1000;
    public int ItemSamplingInterval = 100;
    public bool Durable = false;
    public uint QueueSize = 10;
    public uint KeepAliveCount = 5;
    public uint LifetimeCount = 0;
}

public record OpcuaSubscriptionEventArgs(
    uint? Sequence,
    string Node,
    DateTime? SourceTimestamp,
    DateTime LocalTimestamp,
    uint? Status,
    object? PreviousValue,
    object? Value
);

public class OpcuaClient : IOpcuaClient, IDisposable {
    private readonly Lock _lock = new();
    private readonly ApplicationConfiguration _appConfiguration;
    private readonly OpcuaConfiguration _configuration;
    private SessionReconnectHandler? _reconnectHandler;
    private ApplicationInstance _application;
    private ISession? _session;
    private Subscription? _subscription;
    private bool _disposed;
    private ILogger _logger;

    public OpcuaClient(
        IOptions<OpcuaConfiguration> configuration,
        ILogger<OpcuaClient> logger
    ) {
        _logger = logger;
        _configuration = configuration.Value;
        _appConfiguration = _configuration.ToApplicationConfiguration();
        _appConfiguration.CertificateValidator.CertificateValidation += CertificateValidation;
        _application = new ApplicationInstance {
            ApplicationName = _configuration.ApplicationName,
            ApplicationType = _configuration.ApplicationType,
            ApplicationConfiguration = _appConfiguration
        };
    }

    public void Dispose() {
        _disposed = true;
        Utils.SilentDispose(_session);
        GC.SuppressFinalize(this);
    }

    public bool IsConnected() {
        return _session?.Connected == true;
    }

    public async Task<bool> ConnectAsync(
        string url,
        IUserIdentity identity,
        bool useSecurity = true,
        CancellationToken cancellation = default
    ) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(OpcuaClient));
        }

        if (url == null) {
            throw new ArgumentNullException(nameof(url));
        }

        if (_session != null && _session.Connected) {
            _logger.LogWarning("Client is already connected");
            return false;
        }

        _logger.LogInformation("Validating opcua configuration");
        await _appConfiguration.ValidateAsync(_configuration.ApplicationType);
        if (_configuration.AutoAcceptUntrustedCertificates) {
            _appConfiguration.CertificateValidator.CertificateValidation += (s, e) => { e.Accept = (e.Error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted); };
        }

        var valid = await _application.CheckApplicationInstanceCertificatesAsync(false, 2048)
            .ConfigureAwait(false);

        _logger.LogInformation($"Valid Application Certificate: {valid}");
        _logger.LogInformation($"Connecting to {url}");
        _logger.LogInformation($"Application Certificate: {_appConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.ToString()}");

        ITransportWaitingConnection? connection = null;
        EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
            _appConfiguration,
            url,
            useSecurity,
            cancellation
        ).ConfigureAwait(false);

        EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(_appConfiguration);
        ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        TraceableSessionFactory sessionFactory = TraceableSessionFactory.Instance;
        ISession session = await sessionFactory.CreateAsync(
            _appConfiguration,
            connection,
            endpoint,
            connection == null,
            false,
            _appConfiguration.ApplicationName,
            _configuration.SessionLifetime,
            identity,
            null,
            cancellation
        ).ConfigureAwait(false);

        if (session == null || !session.Connected) {
            _logger.LogError("Failed to create opc client session");
            return false;
        }

        _session = session;
        _session.KeepAliveInterval = _configuration.KeepAliveInterval;
        _session.DeleteSubscriptionsOnClose = false;
        _session.TransferSubscriptionsOnReconnect = true;
        _session.KeepAlive += SessionKeepAlive;

        _reconnectHandler = new SessionReconnectHandler(true, _configuration.ReconnectPeriodBackoff);

        return true;
    }

    private void SessionKeepAlive(ISession session, KeepAliveEventArgs e) {
        try {
            if (_session == null || !_session.Equals(session) || !ServiceResult.IsBad(e.Status)) {
                return;
            }

            if (_configuration.ReconnectPeriod <= 0) {
                _logger.LogWarning($"KeepAlive status {e.Status}, but reconnect is disabled");
                return;
            }

            SessionReconnectHandler.ReconnectState? state = _reconnectHandler?.BeginReconnect(_session, _configuration.ReconnectPeriod, ReconnectComplete);
            if (state == SessionReconnectHandler.ReconnectState.Triggered) {
                _logger.LogInformation($"KeepAlive status {e.Status}, reconnect status {state}, reconnect period {_configuration.ReconnectPeriod}ms");
            } else {
                _logger.LogInformation($"KeepAlive status {e.Status}, reconnect status {state}");
            }

            e.CancelKeepAlive = true;
        } catch (Exception ex) {
            _logger.LogError($"Error in OnKeepAlive: {ex.Message}");
        }
    }

    public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellation = default) {
        try {
            _logger.LogInformation("Diconnecting opcua client");
            if (_session == null || !IsConnected()) {
                _logger.LogWarning("Could not disconnect opcua client, session is not connected");
                return;
            }

            lock(_lock) {
                _session.KeepAlive -= SessionKeepAlive;
                _reconnectHandler?.Dispose();
                _reconnectHandler = null;
            }

            await _session.CloseAsync(!leaveChannelOpen, cancellation).ConfigureAwait(false);
            if (leaveChannelOpen) {
                _session.DetachChannel();
            }

            _session.Dispose();
            _session = null;
            _logger.LogInformation("Opcua client session disconnected");
        } catch (Exception ex) {
            _logger.LogError("Error while trying to disconnect opcua client: {}", ex);
        }
    }

    public async Task<DataValueCollection?> ReadAsync(NodeId node, CancellationToken cancellation = default) {
        if (_session == null || !IsConnected()) {
            _logger.LogWarning("Cannot read opcua server, session is not connected");
            return null;
        }

        try {
            var nodes = new ReadValueIdCollection {
                new ReadValueId {NodeId=node, AttributeId=Attributes.Value},
                new ReadValueId {NodeId=node, AttributeId=Attributes.DataType},
                new ReadValueId {NodeId=node, AttributeId=Attributes.DataTypeDefinition},
                new ReadValueId {NodeId=node, AttributeId=Attributes.DisplayName},
            };

            var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both, nodes, cancellation).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodes);

            return response.Results;
        } catch (Exception ex) {
            _logger.LogError("Error while reading nodes: {}", ex);
            return null;
        }
    }

    public async Task SubscribeTo(
        NodeId node,
        uint nodeAttribute,
        Action<OpcuaSubscriptionEventArgs> eventHandler,
        OpcuaSubscriptionConfiguration? config = null,
        bool durable = false,
        CancellationToken cancellation = default
    ) {
        try {
            if (_session == null || !IsConnected()) {
                _logger.LogWarning("Cannot subscribe to opcua node, session is not connected");
                return;
            }
            if (config == null) {
                config = new OpcuaSubscriptionConfiguration();
            }
            if (_subscription == null) {
                _subscription = new Subscription(_session.DefaultSubscription) {
                    DisplayName = config.DisplayName,
                    PublishingEnabled = config.PublishingEnabled,
                    PublishingInterval = config.PublishingInterval,
                    LifetimeCount = config.LifetimeCount,
                    KeepAliveCount = config.KeepAliveCount,
                    DisableMonitoredItemCache = false,
                };

                _session.AddSubscription(_subscription);
                await _subscription.CreateAsync(cancellation).ConfigureAwait(false);

                _logger.LogInformation("Subscription created with id={0}, sampling interval={1}, publishing interval={2}",
                    _subscription.Id,
                    config.ItemSamplingInterval,
                    config.PublishingInterval
                );

                if (config.Durable) {
                    (bool success, uint revisedLifetime) = await _subscription.SetSubscriptionDurableAsync(1, cancellation).ConfigureAwait(false);
                    if (success) {
                        _logger.LogInformation("Subscription is {0} now durable, revised lifetime {1} in hours", _subscription.Id, revisedLifetime);
                    } else {
                        _logger.LogWarning("Subscription {0} failed durable call", _subscription.Id);
                    }
                }
            }

            var item = new MonitoredItem(_subscription.DefaultItem) {
                DisplayName = config.DisplayName,
                StartNodeId=node,
                AttributeId=nodeAttribute,
                SamplingInterval=config.ItemSamplingInterval,
                QueueSize=config.QueueSize,
                CacheQueueSize=(int)config.QueueSize,
                DiscardOldest=false,
                MonitoringMode=MonitoringMode.Reporting
            };

            item.Notification += (item, e) => {
                try {
                    var notification = e.NotificationValue as MonitoredItemNotification;
                    var args = new OpcuaSubscriptionEventArgs (
                      Sequence: notification?.Message.SequenceNumber,
                      Node: item.ResolvedNodeId.ToString(),
                      SourceTimestamp: notification?.Value.SourceTimestamp,
                      LocalTimestamp: DateTime.Now,
                      Status: notification?.Value.StatusCode.Code,
                      PreviousValue: null,
                      Value: notification?.Value.Value
                    );

                    eventHandler(args);
                } catch(Exception ex) {
                    _logger.LogError("MonitoredItem error: {}", ex);
                }
            };

            _subscription.AddItem(item);
            await _subscription.ApplyChangesAsync(cancellation).ConfigureAwait(false);

            _logger.LogInformation("Monitored item for node {0} added for subscription {1}", node, _subscription.Id);
        } catch (Exception ex) {
            _logger.LogError("Error while attempting to subscribe to items: {0}", ex);
        }
    }

    private void ReconnectComplete(object? sender, EventArgs e) {
        if (!ReferenceEquals(sender, _reconnectHandler)) {
            return;
        }

        lock (_lock) {
            if (_reconnectHandler?.Session == null) {
                _logger.LogInformation("Reconnect KeepAlive recovered");
                return;
            }

            if (!ReferenceEquals(_session, _reconnectHandler.Session)) {
                _logger.LogInformation($"Reconnected to new session: {_reconnectHandler.Session.SessionId}");

                var session = _session;
                _session = _reconnectHandler.Session;
                Utils.SilentDispose(session);
            } else {
                _logger.LogInformation($"Reactivated session: {_reconnectHandler.Session.SessionId}");
            }
        }
    }

    protected virtual void CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e) {
        bool accepted = false;
        ServiceResult error = e.Error;
        if (error.StatusCode == StatusCodes.BadCertificateUntrusted && _configuration.AutoAcceptClients) {
            accepted = true;
        }

        if (accepted) {
            _logger.LogInformation($"Untrusted certificate accepted. Subject={e.Certificate.Subject}");
            e.Accept = true;
        } else {
            _logger.LogWarning($"Untrusted certificate rejected. Subject={e.Certificate.Subject}");
        }
    }
}
