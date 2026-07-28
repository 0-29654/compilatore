using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CVPlus.Mac.Models;

namespace CVPlus.Mac.Services;

public sealed class TeacherDiscoveryService : IAsyncDisposable
{
    private UdpClient? _udp;
    public event Action<ServerState>? ServerDiscovered;

    public Task StartAsync(CancellationToken token)
    {
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5051));
        _ = ListenAsync(token);
        return Task.CompletedTask;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udp is not null)
        {
            try
            {
                UdpReceiveResult packet = await _udp.ReceiveAsync(token);
                string json = Encoding.UTF8.GetString(packet.Buffer);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement r = doc.RootElement;
                string ip = Get(r, "ip", packet.RemoteEndPoint.Address.ToString());
                int port = GetInt(r, "port", 5050);
                string session = Get(r, "sessionCode", Get(r, "code", Get(r, "session", "")));
                string mode = Get(r, "mode", Get(r, "sessionMode", "esercitazione"));
                bool allowed = GetBool(r, "headerManagementAllowed", GetBool(r, "allowHeaderManagement", false));
                ServerDiscovered?.Invoke(new ServerState(ip, port, session, mode, allowed));
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(500, token); }
        }
    }

    private static string Get(JsonElement e, string n, string d) => e.TryGetProperty(n, out var v) ? v.ToString() : d;
    private static int GetInt(JsonElement e, string n, int d) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out int x) ? x : d;
    private static bool GetBool(JsonElement e, string n, bool d) => e.TryGetProperty(n, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? v.GetBoolean() : d;
    public ValueTask DisposeAsync() { _udp?.Dispose(); return ValueTask.CompletedTask; }
}
