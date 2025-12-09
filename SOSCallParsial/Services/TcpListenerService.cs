using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SOSCallParsial.DAL;
using SOSCallParsial.DAL.Entities;
using SOSCallParsial.Models;
using SOSCallParsial.Models.Configs;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SOSCallParsial.Services
{
    public class TcpListenerService : BackgroundService
    {
        private readonly ILogger<TcpListenerService> _logger;
        private readonly DomoMessageParser _parser;
        private readonly IServiceProvider _serviceProvider;
        private readonly TcpSettings _tcpSettings;


        public TcpListenerService(
            ILogger<TcpListenerService> logger,
            DomoMessageParser parser,
            IServiceProvider serviceProvider,
            IOptions<TcpSettings> tcpOptions)
        {
            _logger = logger;
            _parser = parser;
            _serviceProvider = serviceProvider;
            _tcpSettings = tcpOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var listener = new TcpListener(IPAddress.Any, _tcpSettings.Port);

                try
                {
                    listener.Start();
                    _logger.LogInformation("✅ TCP Listener started on port {Port}", _tcpSettings.Port);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            _logger.LogDebug("🟡 Waiting for new TCP client...");
                            var client = await listener.AcceptTcpClientAsync(stoppingToken);
                            _logger.LogInformation("🔌 New TCP client connected.");

                            _ = HandleClientAsync(client, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("🔴 AcceptTcpClientAsync canceled.");
                            break;
                        }
                        catch (SocketException sex)
                        {
                            _logger.LogError(sex, "❌ SocketException while accepting client.");
                            await Task.Delay(1000, stoppingToken); // дати трохи часу
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Unexpected exception in TCP listener.");
                            await Task.Delay(1000, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "🚨 Fatal error starting TCP listener. Restarting...");
                }
                finally
                {
                    listener.Stop();
                    _logger.LogInformation("🛑 TCP Listener stopped.");
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("🔄 Restarting TCP listener after failure or shutdown.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
            _logger.LogInformation("Incoming connection from IP: {IP}", remoteIp);

            if (!string.IsNullOrWhiteSpace(_tcpSettings.AllowedIp) &&
                !remoteIp.Equals(IPAddress.Parse(_tcpSettings.AllowedIp)))
            {
                _logger.LogWarning("Connection from unauthorized IP: {IP}", remoteIp);
                client.Close();
                return;
            }

            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            using var stream = client.GetStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var raw = await ReadUntilCR(stream, cancellationToken);

                    if (raw is null)
                    {
                        _logger.LogInformation("Client disconnected while waiting for data.");
                        break;
                    }

 
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                     _logger.LogInformation("Received raw message: {Raw}", raw);

                    var parsed = _parser.Parse(raw);
                    if (parsed == null)
                    {
                         _logger.LogWarning("Message could not be parsed: {Raw}", raw);
                        continue;
                    }

 
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var callService = scope.ServiceProvider.GetRequiredService<CallQueueService>();

                    db.AlarmLogs.Add(new AlarmLog
                    {
                        Account = parsed.Account,
                        EventCode = parsed.EventCode,
                        GroupCode = parsed.GroupCode,
                        ZoneCode = parsed.ZoneCode,
                        PhoneNumber = parsed.PhoneNumber,
                        RawMessage = parsed.RawMessage,
                        Timestamp = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(cancellationToken);
 
                    await callService.EnqueueCallAsync(parsed);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("ACK\r"), cancellationToken);
                    _logger.LogInformation("ACK sent back to client.");
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error handling TCP client");
            }
            finally
            {
                client.Close();
             }
        }

        private async Task<string?> ReadUntilCR(NetworkStream stream, CancellationToken token)
        {
            var messageBytes = new List<byte>(256);
            var buffer = new byte[1];
            var expectingLeadLf = true;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(0, 1), token);
                    if (read == 0)
                    {
                        return null; // connection closed
                    }

                    byte current = buffer[0];

                    if (expectingLeadLf)
                    {
                        if (current != 0x0A)
                        {
                            _logger.LogWarning("Message did not start with LF (0x0A)");
                        }
                        else
                        {
                            expectingLeadLf = false;
                            continue; // skip the expected LF
                        }

                        expectingLeadLf = false;
                    }

                    if (current == 0x0D) break;
                    messageBytes.Add(current);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "⚠️ Connection closed unexpectedly during read.");
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (token.IsCancellationRequested)
            {
                return null;
            }

            return Encoding.ASCII.GetString(messageBytes.ToArray());
        }
    }
}
