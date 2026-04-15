namespace SOSCallParsial.Services
{
    public sealed class TcpServerStatus
    {
        private readonly object _gate = new();
        private readonly DateTime _appStartedAtUtc = DateTime.UtcNow;

        private string? _host;
        private string? _allowedIp;
        private int _port;
        private bool _listenerRunning;
        private DateTime? _listenerStartedAtUtc;
        private DateTime? _listenerStoppedAtUtc;
        private DateTime? _lastClientConnectedAtUtc;
        private string? _lastClientIp;
        private DateTime? _lastMessageAtUtc;
        private string? _lastMessagePreview;
        private long _activeClientCount;
        private long _totalConnections;
        private DateTime? _lastPortCheckAtUtc;
        private bool? _lastPortCheckSucceeded;
        private string? _lastPortCheckMessage;
        private DateTime? _lastUnauthorizedAttemptAtUtc;
        private string? _lastUnauthorizedIp;
        private DateTime? _lastErrorAtUtc;
        private string? _lastErrorMessage;

        public void MarkListenerStarted(string? host, int port, string? allowedIp)
        {
            lock (_gate)
            {
                _host = host;
                _port = port;
                _allowedIp = allowedIp;
                _listenerRunning = true;
                _listenerStartedAtUtc = DateTime.UtcNow;
                _listenerStoppedAtUtc = null;
            }
        }

        public void MarkListenerStopped()
        {
            lock (_gate)
            {
                _listenerRunning = false;
                _listenerStoppedAtUtc = DateTime.UtcNow;
                _activeClientCount = 0;
            }
        }

        public void MarkClientConnected(string remoteIp)
        {
            lock (_gate)
            {
                _lastClientConnectedAtUtc = DateTime.UtcNow;
                _lastClientIp = remoteIp;
                _activeClientCount++;
                _totalConnections++;
            }
        }

        public void MarkClientDisconnected()
        {
            lock (_gate)
            {
                if (_activeClientCount > 0)
                {
                    _activeClientCount--;
                }
            }
        }

        public void MarkMessageReceived(string rawMessage)
        {
            lock (_gate)
            {
                _lastMessageAtUtc = DateTime.UtcNow;
                _lastMessagePreview = rawMessage.Length <= 140
                    ? rawMessage
                    : $"{rawMessage[..137]}...";
            }
        }

        public void MarkPortCheck(bool succeeded, string message)
        {
            lock (_gate)
            {
                _lastPortCheckAtUtc = DateTime.UtcNow;
                _lastPortCheckSucceeded = succeeded;
                _lastPortCheckMessage = message;
            }
        }

        public void MarkUnauthorizedConnection(string remoteIp)
        {
            lock (_gate)
            {
                _lastUnauthorizedAttemptAtUtc = DateTime.UtcNow;
                _lastUnauthorizedIp = remoteIp;
            }
        }

        public void MarkError(string message)
        {
            lock (_gate)
            {
                _lastErrorAtUtc = DateTime.UtcNow;
                _lastErrorMessage = message;
            }
        }

        public TcpServerStatusSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new TcpServerStatusSnapshot(
                    _appStartedAtUtc,
                    _host,
                    _allowedIp,
                    _port,
                    _listenerRunning,
                    _listenerStartedAtUtc,
                    _listenerStoppedAtUtc,
                    _lastClientConnectedAtUtc,
                    _lastClientIp,
                    _lastMessageAtUtc,
                    _lastMessagePreview,
                    _activeClientCount,
                    _totalConnections,
                    _lastPortCheckAtUtc,
                    _lastPortCheckSucceeded,
                    _lastPortCheckMessage,
                    _lastUnauthorizedAttemptAtUtc,
                    _lastUnauthorizedIp,
                    _lastErrorAtUtc,
                    _lastErrorMessage);
            }
        }
    }

    public sealed record TcpServerStatusSnapshot(
        DateTime AppStartedAtUtc,
        string? Host,
        string? AllowedIp,
        int Port,
        bool ListenerRunning,
        DateTime? ListenerStartedAtUtc,
        DateTime? ListenerStoppedAtUtc,
        DateTime? LastClientConnectedAtUtc,
        string? LastClientIp,
        DateTime? LastMessageAtUtc,
        string? LastMessagePreview,
        long ActiveClientCount,
        long TotalConnections,
        DateTime? LastPortCheckAtUtc,
        bool? LastPortCheckSucceeded,
        string? LastPortCheckMessage,
        DateTime? LastUnauthorizedAttemptAtUtc,
        string? LastUnauthorizedIp,
        DateTime? LastErrorAtUtc,
        string? LastErrorMessage);
}
