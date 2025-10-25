using System.Diagnostics;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Moss.Services {
    public class OpcSubscriptionService: BackgroundService {
        private readonly ILogger<OpcSubscriptionService> _logger;
        private OpcUaClient? _client;

        public OpcSubscriptionService(ILogger<OpcSubscriptionService> logger) {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellation) {
            _logger.LogInformation("Starting opc subscription service");

            await ConnectAsync();
            while (!cancellation.IsCancellationRequested) {
                await Task.Delay(10);
            }

            _logger.LogInformation("Stopping opc subcription service");
        }

        private async Task ConnectAsync() {
            var config = new ApplicationConfiguration {
                ApplicationName = "MyClient",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = Utils.Format(@"urn:{0}:MyClient", System.Net.Dns.GetHostName()),
                SecurityConfiguration = new SecurityConfiguration {
                    ApplicationCertificate = new CertificateIdentifier {
                        StoreType="Directory",
                        StorePath=@"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault",
                        SubjectName="MyClientSubjectName",
                    },
                    TrustedIssuerCertificates = new CertificateTrustList {
                        StoreType="Directory",
                        StorePath=@"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities",
                    },
                    TrustedPeerCertificates = new CertificateTrustList {
                        StoreType="Directory",
                        StorePath=@"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications",
                    },
                    RejectedCertificateStore = new CertificateTrustList {
                        StoreType="Directory",
                        StorePath=@"%CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates",
                    },
                    AddAppCertToTrustedStore = true,
                    AutoAcceptUntrustedCertificates = true,

                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas {
                    OperationTimeout = 15000
                },
                ClientConfiguration = new ClientConfiguration {
                    DefaultSessionTimeout = 60000,
                },
                TraceConfiguration = new TraceConfiguration()
            };

            await config.ValidateAsync(ApplicationType.Client);
            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates) {
                config.CertificateValidator.CertificateValidation += (s,e) => {e.Accept = (e.Error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted);};
            }
            var application = new ApplicationInstance {
              ApplicationName = config.ApplicationName,
              ApplicationType = config.ApplicationType,
              ApplicationConfiguration = config,
            };

            var valid = await application.CheckApplicationInstanceCertificatesAsync(false, 2048)
                .ConfigureAwait(false);
            _logger.LogInformation($"Valid Application Certificate: {valid}");


            _client = new OpcUaClient(config, _logger);
            await _client.ConnectAsync("opc.tcp://localhost:62541/discovery");

        }
    }

    public class OpcUaClient: IDisposable {
        private readonly Lock _lock = new();
        private readonly ApplicationConfiguration _configuration;
        private SessionReconnectHandler? _reconnectHandler;
        private bool _disposed;
        private ILogger _logger;

        public Opc.Ua.Client.ISession? Session { get; private set; }
        public int KeepAliveInterval { get;set; } = 5000;
        public int ReconnectPeriod { get;set; } = 1000;
        public int ReconnectPeriodBackoff { get;set; } = 15000;
        public uint SessionLifetime { get;set; } = 60*1000;
        public IUserIdentity UserIdentity { get;set; } = new UserIdentity(username: "opcuauser", password: "makaze34!");
        public bool AutoAccept { get;set; }


        public OpcUaClient(
            ApplicationConfiguration configuration,
            ILogger logger
        ) {
            _configuration = configuration;
            _configuration.CertificateValidator.CertificateValidation += CertificateValidation;
            _logger = logger;
        }

        public void Dispose() {
            _disposed = true;
            Utils.SilentDispose(Session);

        }

        public async Task<bool> ConnectAsync(string url, bool useSecurity = true, CancellationToken cancellation = default) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(OpcUaClient));
            }

            if (url == null) {
                throw new ArgumentNullException(nameof(url));
            }

                if (Session != null && Session.Connected) {
                    _logger.LogWarning("Client is already connected");
                    return false;
                }

                _logger.LogInformation($"Connecting to {url}");
                _logger.LogInformation($"Application Certificate: {_configuration.SecurityConfiguration.ApplicationCertificate.Certificate.ToString()}");
                ITransportWaitingConnection? connection = null;
                EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    _configuration,
                    url,
                    useSecurity,
                    cancellation
                ).ConfigureAwait(false);

                EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(_configuration);
                ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
                TraceableSessionFactory sessionFactory = TraceableSessionFactory.Instance;
                Opc.Ua.Client.ISession session = await sessionFactory.CreateAsync(
                    _configuration,
                    connection,
                    endpoint,
                    connection == null,
                    false,
                    _configuration.ApplicationName,
                    SessionLifetime,
                    UserIdentity,
                    null,
                    cancellation
                ).ConfigureAwait(false);

                if (session == null || !session.Connected) {
                    _logger.LogError("Failed to create opc client session");
                    return false;
                }

                Session = session;
                Session.KeepAliveInterval = KeepAliveInterval;
                Session.DeleteSubscriptionsOnClose = false;
                Session.TransferSubscriptionsOnReconnect = true;
                Session.KeepAlive += SessionKeepAlive;

                _reconnectHandler = new SessionReconnectHandler(true, ReconnectPeriodBackoff);

                return true;
        }

        private void SessionKeepAlive(Opc.Ua.Client.ISession session, KeepAliveEventArgs e) {
            try {
                if (Session == null || !Session.Equals(session) || !ServiceResult.IsBad(e.Status)) {
                    return;
                }

                if (ReconnectPeriod <= 0) {
                    _logger.LogWarning($"KeepAlive status {e.Status}, but reconnect is disabled");
                    return;
                }

                SessionReconnectHandler.ReconnectState state = _reconnectHandler.BeginReconnect(Session, ReconnectPeriod, ReconnectComplete);
                if (state == SessionReconnectHandler.ReconnectState.Triggered) {
                    _logger.LogInformation($"KeepAlive status {e.Status}, reconnect status {state}, reconnect period {ReconnectPeriod}ms");
                } else {
                    _logger.LogInformation($"KeepAlive status {e.Status}, reconnect status {state}");
                }

                e.CancelKeepAlive = true;
            } catch (Exception ex) {
                _logger.LogError($"Error in OnKeepAlive: {ex.Message}");
            }
        }

        private void ReconnectComplete(object sender, EventArgs e) {
            if (!ReferenceEquals(sender, _reconnectHandler)) {
                return;
            }

            lock (_lock) {
                if (_reconnectHandler.Session == null) {
                    _logger.LogInformation("Reconnect KeepAlive recovered");
                    return;
                }

                if (!ReferenceEquals(Session, _reconnectHandler.Session)) {
                    _logger.LogInformation($"Reconnected to new session: {_reconnectHandler.Session.SessionId}");

                    var session = Session;
                    Session = _reconnectHandler.Session;
                    Utils.SilentDispose(session);
                } else {
                    _logger.LogInformation($"Reactivated session: {_reconnectHandler.Session.SessionId}");
                }
            }
        }

        protected virtual void CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e) {
            bool accepted = false;
            ServiceResult error = e.Error;
            if (error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted && AutoAccept) {
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
}
