using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SOSCallParsial.DAL;
using SOSCallParsial.DAL.Entities;
using SOSCallParsial.Models;
using SOSCallParsial.Models.Configs;

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

                        _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
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
                _logger.LogCritical(ex, "🚨 Fatal error starting TCP listener.");
            }
            finally
            {
                listener.Stop();
                _logger.LogInformation("🛑 TCP Listener stopped.");
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

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);

            try
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    //Console.WriteLine("📥 Waiting for message...");

                    if (stream.DataAvailable && stream.ReadByte() != 0x0A)
                    {
                        _logger.LogWarning("Message did not start with LF (0x0A)");
                    }

                    var raw = await ReadUntilCR(reader, cancellationToken);

                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    Console.WriteLine($"📨 Raw message received: {raw}");
                    _logger.LogInformation("Received raw message: {Raw}", raw);

                    var parsed = _parser.Parse(raw);
                    if (parsed == null)
                    {
                        Console.WriteLine("❗ Message could not be parsed.");
                        _logger.LogWarning("Message could not be parsed: {Raw}", raw);
                        continue;
                    }

                    Console.WriteLine("✅ Message parsed successfully.");
                    Console.WriteLine($"📞 Phone: {parsed.PhoneNumber} | Code: {parsed.EventCode}");

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
                    Console.WriteLine("💾 Saved message to database.");

                    await callService.EnqueueCallAsync(parsed);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("ACK\r"), cancellationToken);
                    _logger.LogInformation("ACK sent back to client.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Error while processing TCP client:");
                Console.WriteLine(ex.ToString());
                _logger.LogError(ex, "Error handling TCP client");
            }
            finally
            {
                client.Close();
                Console.WriteLine("🔒 TCP connection closed.");
            }
        }

        private async Task<string> ReadUntilCR(StreamReader reader, CancellationToken token)
        {
            var sb = new StringBuilder();
            char[] buffer = new char[1];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await reader.ReadAsync(buffer, 0, 1);
                    if (read == 0) break;

                    if (buffer[0] == '\r') break;
                    sb.Append(buffer[0]);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "⚠️ Connection closed unexpectedly during read.");
            }

            return sb.ToString();
        }
    }
}
