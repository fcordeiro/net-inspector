using System.Net;
using System.Net.Sockets;
using System.Text;
using NetInspector.Core;

internal static class ProtocolChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var budget = new TransferBudget(1_000_000);
        var reservations = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => budget.Reserve(32000))));
        check(reservations.Sum() == 1_000_000 && budget.Reserved == 1_000_000 && budget.Reserve(1) == 0, "orçamento é atômico com fluxos concorrentes");
        var provider = new StatusProvider("Teste local", "https://example.com", "https://example.com/status");
        var json = """{"page":{"updated_at":"2026-10-16T12:00:00Z"},"status":{"indicator":"none","description":"All Systems Operational"},"components":[{"name":"Login","status":"operational"}],"incidents":[],"scheduled_maintenances":[]}""";
        var parsed = ProviderProbe.Parse(provider, Encoding.UTF8.GetBytes(json), DateTimeOffset.UtcNow);
        check(parsed.State == "operational", "status oficial sem incidentes interpretado");
        var degraded = ProviderProbe.Parse(provider, Encoding.UTF8.GetBytes(json.Replace("\"status\":\"operational\"", "\"status\":\"partial_outage\"")), DateTimeOffset.UtcNow);
        check(degraded.State == "degraded" && degraded.Components.Length == 1, "componente degradado prevalece sobre resumo sem incidente");
        var roblox = ProviderProbe.Parse(provider, Encoding.UTF8.GetBytes("""{"result":{"status_overall":{"status":"Operational","status_code":100,"updated":"2026-10-16T12:00:00Z"},"status":[{"name":"Players","status":"Operational","status_code":100,"containers":[{"name":"Game server","status":"Degraded","status_code":300}]}],"incidents":[],"maintenance":{"active":[]}}}"""), DateTimeOffset.UtcNow);
        check(roblox.State == "degraded" && roblox.Components.Length == 1, "Status.io interpreta componentes internos do Roblox");
        var manual = await new ProviderProbe(new HttpClient()).Check(provider with { Api = null }, CancellationToken.None);
        check(manual.State == "manual", "página sem integração não vira status saudável");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var a2sServer = Task.Run(async () =>
        {
            var first = await udp.ReceiveAsync(deadline.Token);
            if (first.Buffer.Length < 5 || first.Buffer[4] != 0x54) throw new Exception("Consulta A2S inválida");
            await udp.SendAsync(new byte[] { 255, 255, 255, 255, 0x41, 1, 2, 3, 4 }, first.RemoteEndPoint, deadline.Token);
            var second = await udp.ReceiveAsync(deadline.Token);
            if (!second.Buffer.TakeLast(4).SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new Exception("Challenge A2S não devolvido");
            var response = new byte[] { 255, 255, 255, 255, 0x49, 17 }.Concat(Encoding.UTF8.GetBytes("Servidor de teste\0de_dust2\0csgo\0Counter-Strike\0")).Concat(new byte[] { 218, 2, 5, 20, 0, 100, 108, 0, 1, 0 }).ToArray();
            await udp.SendAsync(response, second.RemoteEndPoint, deadline.Token);
        }, deadline.Token);
        var a2s = await GameProbe.Check(new("A2S local", "127.0.0.1", port, "A2S"), deadline.Token);
        await a2sServer;
        check(a2s.Success && a2s.Details.Contains("de_dust2") && a2s.Ms.HasValue, "A2S UDP com challenge e resposta de servidor");
        var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start();
        try
        {
            var minecraftServer = Task.Run(async () =>
            {
                using var client = await tcp.AcceptTcpClientAsync(deadline.Token);
                using var stream = client.GetStream();
                _ = await Packet(stream, deadline.Token); // handshake
                var request = await Packet(stream, deadline.Token);
                if (request.Length != 1 || request[0] != 0) throw new Exception("Minecraft status request inválido");
                using var status = new MemoryStream(); status.WriteByte(0);
                var body = Encoding.UTF8.GetBytes("""{"version":{"name":"Teste Java","protocol":47},"players":{"online":12,"max":30}}""");
                GameProbe.WriteVarInt(status, body.Length); status.Write(body);
                await Send(stream, status.ToArray(), deadline.Token);
                var ping = await Packet(stream, deadline.Token);
                await Send(stream, ping, deadline.Token);
            }, deadline.Token);
            var minecraft = await GameProbe.Check(new("Minecraft local", "127.0.0.1", ((IPEndPoint)tcp.LocalEndpoint).Port, "Minecraft"), deadline.Token);
            await minecraftServer;
            check(minecraft.Success && minecraft.Details.Contains("12 / 30") && minecraft.Ms.HasValue, "Minecraft status e ping/pong completos");
        }
        finally { tcp.Stop(); }
        var s = new Session { EndedAt = DateTimeOffset.UtcNow, State = "completed", ProviderStatus = [degraded], GameServers = [a2s] };
        check(Diagnosis.Analyze(s).Any(f => f.Title.Contains("incidente publicado")), "incidente oficial entra no diagnóstico");
        check(Diagnosis.Analyze(s).Any(f => f.Title.Contains("servidor respondeu")), "resposta do servidor entra no diagnóstico");
    }
    private static async Task<byte[]> Packet(Stream stream, CancellationToken ct)
    {
        var length = await GameProbe.ReadVarInt(stream, ct); var data = new byte[length]; await stream.ReadExactlyAsync(data, ct); return data;
    }
    private static async Task Send(Stream stream, byte[] data, CancellationToken ct)
    {
        using var prefix = new MemoryStream(); GameProbe.WriteVarInt(prefix, data.Length);
        await stream.WriteAsync(prefix.ToArray(), ct); await stream.WriteAsync(data, ct);
    }
}
