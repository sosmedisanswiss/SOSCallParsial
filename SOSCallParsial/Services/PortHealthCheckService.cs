using Microsoft.Extensions.Options;
using SOSCallParsial.Models.Configs;
using System.Net;
using System.Net.Sockets;

namespace SOSCallParsial.Services
{
    public sealed class PortHealthCheckService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

        private readonly ILogger<PortHealthCheckService> _logger;
        private readonly TcpServerStatus _serverStatus;
        private readonly TcpSettings _tcpSettings;

        public PortHealthCheckService(
            ILogger<PortHealthCheckService> logger,
            TcpServerStatus serverStatus,
            IOptions<TcpSettings> tcpOptions)
        {
            _logger = logger;
            _serverStatus = serverStatus;
            _tcpSettings = tcpOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunProbeAsync(stoppingToken);

            using var timer = new PeriodicTimer(CheckInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunProbeAsync(stoppingToken);
            }
        }

        private async Task RunProbeAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _tcpSettings.Port)
                    .WaitAsync(CheckTimeout, stoppingToken);

                const string successMessage = "TCP-порт доступен и принимает подключения.";
                _serverStatus.MarkPortCheck(true, successMessage);
                _logger.LogInformation("Port health check succeeded for TCP port {Port}.", _tcpSettings.Port);
            }
            catch (TimeoutException)
            {
                var message = $"TCP-порт {_tcpSettings.Port} не ответил за {CheckTimeout.TotalSeconds:0} сек.";
                _serverStatus.MarkPortCheck(false, message);
                _logger.LogWarning("Port health check timed out for TCP port {Port}.", _tcpSettings.Port);
            }
            catch (SocketException ex)
            {
                var message = $"TCP-порт {_tcpSettings.Port} недоступен: {ex.SocketErrorCode}.";
                _serverStatus.MarkPortCheck(false, message);
                _logger.LogWarning(ex, "Port health check failed for TCP port {Port}.", _tcpSettings.Port);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                var message = $"Ошибка проверки TCP-порта {_tcpSettings.Port}: {ex.Message}";
                _serverStatus.MarkPortCheck(false, message);
                _logger.LogError(ex, "Unexpected error during port health check for TCP port {Port}.", _tcpSettings.Port);
            }
        }
    }
}
