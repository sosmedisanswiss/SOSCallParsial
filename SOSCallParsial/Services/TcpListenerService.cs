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
using System.Text.RegularExpressions;

namespace SOSCallParsial.Services
{
    public class TcpListenerService : BackgroundService
    {
        private static readonly Regex AckSequenceRegex = new(
            @"""[^""]+""(?<Seq>\d{4})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex AckAccountRegex = new(
            @"L[0-9A-Za-z]{1,6}#(?<Acct>[^\[]+)\[",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ILogger<TcpListenerService> _logger;
        private readonly DomoMessageParser _parser;
        private readonly IServiceProvider _serviceProvider;
        private readonly TcpServerStatus _serverStatus;
        private readonly TcpSettings _tcpSettings;


        public TcpListenerService(
            ILogger<TcpListenerService> logger,
            DomoMessageParser parser,
            IServiceProvider serviceProvider,
            TcpServerStatus serverStatus,
            IOptions<TcpSettings> tcpOptions)
        {
            _logger = logger;
            _parser = parser;
            _serviceProvider = serviceProvider;
            _serverStatus = serverStatus;
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
                    _serverStatus.MarkListenerStarted(_tcpSettings.Host, _tcpSettings.Port, _tcpSettings.AllowedIp);
                    _logger.LogInformation("✅ TCP Listener started on port {Port}", _tcpSettings.Port);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            _logger.LogDebug("🟡 Waiting for new TCP client...");
                            var client = await listener.AcceptTcpClientAsync(stoppingToken);
                            _logger.LogInformation("🔌 New TCP client connected.");

                            _ = Task.Run(() => HandleClientAsync(client, stoppingToken));
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("🔴 AcceptTcpClientAsync canceled.");
                            break;
                        }
                        catch (SocketException sex)
                        {
                            _serverStatus.MarkError($"SocketException while accepting client: {sex.Message}");
                            _logger.LogError(sex, "❌ SocketException while accepting client.");
                            await Task.Delay(1000, stoppingToken); // дати трохи часу
                        }
                        catch (Exception ex)
                        {
                            _serverStatus.MarkError($"Unexpected exception in TCP listener: {ex.Message}");
                            _logger.LogError(ex, "❌ Unexpected exception in TCP listener.");
                            await Task.Delay(1000, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _serverStatus.MarkError($"Fatal error starting TCP listener: {ex.Message}");
                    _logger.LogCritical(ex, "🚨 Fatal error starting TCP listener. Restarting...");
                }
                finally
                {
                    listener.Stop();
                    _serverStatus.MarkListenerStopped();
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
            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (remoteEndPoint is null)
            {
                _logger.LogWarning("TCP client connected without a remote endpoint.");
                client.Close();
                return;
            }

            var remoteIp = remoteEndPoint.Address;
            var isInternalHealthProbe = IPAddress.IsLoopback(remoteIp);
            var remoteIpText = remoteIp.ToString();

            if (isInternalHealthProbe)
            {
                _logger.LogDebug("Internal health probe connected from IP: {IP}", remoteIp);
            }
            else
            {
                _logger.LogInformation("Incoming connection from IP: {IP}", remoteIp);
            }

            IPAddress? allowedIp = null;
            if (!string.IsNullOrWhiteSpace(_tcpSettings.AllowedIp) &&
                !IPAddress.TryParse(_tcpSettings.AllowedIp, out allowedIp))
            {
                _serverStatus.MarkError($"Configured AllowedIp '{_tcpSettings.AllowedIp}' is not a valid IP address.");
                _logger.LogError("Configured AllowedIp '{AllowedIp}' is not a valid IP address.", _tcpSettings.AllowedIp);
                client.Close();
                return;
            }

            if (allowedIp is not null && !remoteIp.Equals(allowedIp) && !isInternalHealthProbe)
            {
                _serverStatus.MarkUnauthorizedConnection(remoteIpText);
                _logger.LogWarning("Connection from unauthorized IP: {IP}", remoteIp);
                client.Close();
                return;
            }

            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            using var stream = client.GetStream();
            var isTrackedClient = false;

            try
            {
                if (!isInternalHealthProbe)
                {
                    _serverStatus.MarkClientConnected(remoteIpText);
                    isTrackedClient = true;
                }

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var raw = await ReadUntilCR(stream, cancellationToken);

                    if (raw is null)
                    {
                        if (isInternalHealthProbe)
                        {
                            _logger.LogDebug("Internal health probe disconnected.");
                        }
                        else
                        {
                            _logger.LogInformation("Client disconnected while waiting for data.");
                        }
                        break;
                    }

 
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    if (!isInternalHealthProbe)
                    {
                        _serverStatus.MarkMessageReceived(raw);
                    }

                    _logger.LogInformation("Received raw message: {Raw}", raw);

                    var parsed = _parser.Parse(raw);
                    if (parsed == null)
                    {
                         _logger.LogWarning("Message could not be parsed: {Raw}", raw);
                        continue;
                    }

                    if (!TryExtractAckContext(raw, parsed.Account, out var seq, out var account))
                    {
                        _logger.LogWarning("Unable to extract ACK context from message: {Raw}", raw);
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
                    var ackPayload = BuildAckDc09(seq, account);
                    await stream.WriteAsync(ackPayload, cancellationToken);
                    _logger.LogInformation("DC-09 ACK sent back to client for seq {Seq} and account {Account}.", seq, account);

                    await callService.EnqueueCallAsync(parsed);
                }
            }
            catch (Exception ex)
            {
                _serverStatus.MarkError($"Error handling TCP client {remoteIpText}: {ex.Message}");
                _logger.LogError(ex, "Error handling TCP client");
            }
            finally
            {
                if (isTrackedClient)
                {
                    _serverStatus.MarkClientDisconnected();
                }

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

        private static bool TryExtractAckContext(string rawMessage, string fallbackAccount, out string seq, out string account)
        {
            seq = string.Empty;
            account = string.Empty;

            var seqMatch = AckSequenceRegex.Match(rawMessage);
            if (!seqMatch.Success)
            {
                return false;
            }

            seq = seqMatch.Groups["Seq"].Value;

            var accountMatch = AckAccountRegex.Match(rawMessage);
            account = accountMatch.Success
                ? accountMatch.Groups["Acct"].Value
                : fallbackAccount;

            return !string.IsNullOrWhiteSpace(account);
        }

        private static byte[] BuildAckDc09(string seq, string account)
        {
            var payload = $"\"ACK\"{seq}L0#{account}[]";
            var payloadBytes = Encoding.ASCII.GetBytes(payload);
            var crc = CalculateDc07Crc(payloadBytes);
            var length = $"0{payloadBytes.Length:X3}";
            var frame = $"\n{crc:X4}{length}{payload}\r";

            return Encoding.ASCII.GetBytes(frame);
        }

        private static ushort CalculateDc07Crc(ReadOnlySpan<byte> payloadBytes)
        {
            ushort crc = 0;

            foreach (var currentByte in payloadBytes)
            {
                var temp = (int)currentByte;

                for (var i = 0; i < 8; i++)
                {
                    temp ^= crc & 0x0001;
                    crc >>= 1;

                    if ((temp & 0x0001) != 0)
                    {
                        crc ^= 0xA001;
                    }

                    temp >>= 1;
                }
            }

            return crc;
        }
    }
}
