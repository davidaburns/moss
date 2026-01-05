namespace Moss.Clients;

using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

public interface IOpcUaClient {
    public Task<bool> ConnectAsync(
        string url,
        IUserIdentity identity,
        bool useSecurity = true,
        CancellationToken cancellation = default
    );
}

public record OpcUaConfiguration {
    public string ApplicationName = "MyClient";
    public ApplicationType ApplicationType = ApplicationType.Client;
    public bool AddAppCertToTrustedStore = true;
    public bool AutoAcceptUntrustedCertificates = false;
    public bool AutoAcceptClients = true;
    public int TransportOperationTimeout = 15000;
    public int DefaultSessionTimeout = 60000;
    public int KeepAliveInterval = 5000;
    public int ReconnectPeriod = 1000;
    public int ReconnectPeriodBackoff = 15000;
    public uint SessionLifetime = 60 * 1000;

    public ApplicationConfiguration ToApplicationConfiguration() {
        return new ApplicationConfiguration {
            ApplicationName = ApplicationName,
            ApplicationType = ApplicationType,
            ApplicationUri = Utils.Format(@"urn:{0}:{1}", System.Net.Dns.GetHostName(), ApplicationName),
            SecurityConfiguration = new SecurityConfiguration {
                ApplicationCertificate = new CertificateIdentifier {
                    StoreType = "Directory",
                    StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault",
                    SubjectName = "MyClientSubjectName",
                },
                TrustedIssuerCertificates = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities",
                },
                TrustedPeerCertificates = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications",
                },
                RejectedCertificateStore = new CertificateTrustList {
                    StoreType = "Directory",
                    StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates",
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

public class OpcUaClient : IOpcUaClient, IDisposable {
    private readonly Lock _lock = new();
    private readonly ApplicationConfiguration _appConfiguration;
    private readonly OpcUaConfiguration _configuration;
    private SessionReconnectHandler? _reconnectHandler;
    private ApplicationInstance _application;
    private Opc.Ua.Client.ISession? _session;
    private bool _disposed;
    private ILogger _logger;

    public OpcUaClient(
        IOptions<OpcUaConfiguration> configuration,
        ILogger<OpcUaClient> logger
    ) {
        _configuration = configuration.Value;
        _appConfiguration = _configuration.ToApplicationConfiguration();
        _appConfiguration.CertificateValidator.CertificateValidation += CertificateValidation;
        _logger = logger;
    }

    public void Dispose() {
        _disposed = true;
        Utils.SilentDispose(_session);

    }

    public async Task<bool> ConnectAsync(
        string url,
        IUserIdentity identity,
        bool useSecurity = true,
        CancellationToken cancellation = default
    ) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(OpcUaClient));
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

        _application = new ApplicationInstance {
            ApplicationName = _configuration.ApplicationName,
            ApplicationType = _configuration.ApplicationType,
            ApplicationConfiguration = _appConfiguration
        };

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
        Opc.Ua.Client.ISession session = await sessionFactory.CreateAsync(
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

    private void SessionKeepAlive(Opc.Ua.Client.ISession session, KeepAliveEventArgs e) {
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
        if (error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted && _configuration.AutoAcceptClients) {
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
