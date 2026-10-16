using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NetInspector.Core;

public sealed record StatusProvider(string Name, string Page, string? Api);
public sealed record ProviderSample(DateTimeOffset At, string Provider, string Page, string State, string Summary, string[] Components, string[] Incidents, DateTimeOffset? UpdatedAt);
public sealed record GameTarget(string Name, string Host, int Port, string Protocol);
public sealed record GameSample(DateTimeOffset At, string Name, string Host, int Port, string Protocol, bool Success, double? Ms, string Details, string? Error);

public static class GameCatalog
{
    public static readonly StatusProvider[] Providers = [
        new("Epic / Fortnite / Rocket League", "https://status.epicgames.com", "https://status.epicgames.com/api/v2/summary.json"),
        new("Roblox", "https://status.roblox.com", "https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d"),
        new("Minecraft / Hypixel", "https://status.hypixel.net", "https://status.hypixel.net/api/v2/summary.json"),
        new("Riot / LoL / Valorant (Brasil)", "https://status.riotgames.com/?region=br&locale=pt_BR", null),
        new("Xbox / Minecraft", "https://support.xbox.com/pt-BR/xbox-live-status", null),
        new("PlayStation", "https://status.playstation.com/pt-br/", null),
        new("Activision / Call of Duty", "https://support.activision.com/onlineservices", null),
        new("Rockstar / GTA", "https://support.rockstargames.com/servicestatus", null),
        new("Ubisoft / Rainbow Six Siege", "https://www.ubisoft.com/en-us/game/rainbow-six/siege/status", null),
        new("EA / FC / Apex / Battlefield", "https://help.ea.com/en/server-status/", null),
        new("Steam / CS2 / Dota 2 — fonte independente Steamstat", "https://steamstat.us/", null)];
    public static List<GameTarget> Defaults => [new("Minecraft — Hypixel (servidor público internacional)", "mc.hypixel.net", 25565, "Minecraft")];
    public static void Validate(GameTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Name) || string.IsNullOrWhiteSpace(target.Host) || target.Host.Length > 253 || Uri.CheckHostName(target.Host) == UriHostNameType.Unknown || target.Port is < 1 or > 65535 || target.Protocol is not ("Minecraft" or "A2S" or "TCP"))
            throw new InvalidDataException("Informe nome, host/IP válido, porta de 1 a 65535 e protocolo Minecraft, A2S ou TCP.");
    }
}

public sealed class ProviderProbe(HttpClient http)
{
    public async Task<ProviderSample> Check(StatusProvider provider, CancellationToken ct)
    {
        var at = DateTimeOffset.UtcNow;
        if (provider.Api == null) return new(at, provider.Name, provider.Page, "manual", "Consulta manual na fonte indicada: integração automática indisponível nesta versão.", [], [], null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var response = await http.GetAsync(provider.Api, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (memory.Length + count > 2_000_000) throw new InvalidDataException("Resposta de status excedeu 2 MB");
                memory.Write(buffer, 0, count);
            }
            return Parse(provider, memory.ToArray(), at);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(at, provider.Name, provider.Page, "unavailable", "Não foi possível consultar o status: " + (ex is OperationCanceledException ? "tempo limite" : ex.Message), [], [], null); }
    }
    public static ProviderSample Parse(StatusProvider provider, byte[] json, DateTimeOffset at)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("result", out var result)) return ParseStatusIo(provider, result, at);
        var status = root.GetProperty("status");
        var indicator = status.GetProperty("indicator").GetString();
        if (indicator is not ("none" or "minor" or "major" or "critical")) throw new InvalidDataException("Indicador de status não reconhecido");
        var components = root.GetProperty("components").EnumerateArray().Where(c => c.GetProperty("status").GetString() != "operational").Select(c => c.GetProperty("name").GetString() + ": " + c.GetProperty("status").GetString()).ToArray();
        var incidents = root.GetProperty("incidents").EnumerateArray().Where(i => i.GetProperty("status").GetString() is not ("resolved" or "postmortem")).Select(i => i.GetProperty("name").GetString() + " — " + i.GetProperty("status").GetString()).ToList();
        if (root.TryGetProperty("scheduled_maintenances", out var maintenance))
            incidents.AddRange(maintenance.EnumerateArray().Where(i => i.GetProperty("status").GetString() is "in_progress" or "verifying").Select(i => "Manutenção: " + i.GetProperty("name").GetString()));
        DateTimeOffset? updated = root.GetProperty("page").TryGetProperty("updated_at", out var date) && date.TryGetDateTimeOffset(out var parsed) ? parsed : null;
        return new(at, provider.Name, provider.Page, indicator == "none" && components.Length == 0 && incidents.Count == 0 ? "operational" : "degraded", status.GetProperty("description").GetString() ?? "Sem descrição", components, incidents.ToArray(), updated);
    }
    private static ProviderSample ParseStatusIo(StatusProvider provider, JsonElement result, DateTimeOffset at)
    {
        var overall = result.GetProperty("status_overall");
        var code = overall.GetProperty("status_code").GetInt32();
        if (code is < 100 or > 600) throw new InvalidDataException("Código Status.io desconhecido");
        var components = new List<string>();
        foreach (var component in result.GetProperty("status").EnumerateArray())
        {
            if (component.GetProperty("status_code").GetInt32() != 100) components.Add(component.GetProperty("name").GetString() + ": " + component.GetProperty("status").GetString());
            if (component.TryGetProperty("containers", out var containers))
                components.AddRange(containers.EnumerateArray().Where(c => c.GetProperty("status_code").GetInt32() != 100).Select(c => component.GetProperty("name").GetString() + " / " + c.GetProperty("name").GetString() + ": " + c.GetProperty("status").GetString()));
        }
        string Label(JsonElement incident) => incident.TryGetProperty("name", out var name) ? name.GetString() ?? "Incidente publicado" : "Incidente publicado";
        var incidents = result.GetProperty("incidents").EnumerateArray().Select(Label).ToList();
        if (result.TryGetProperty("maintenance", out var maintenance) && maintenance.TryGetProperty("active", out var active)) incidents.AddRange(active.EnumerateArray().Select(i => "Manutenção: " + Label(i)));
        DateTimeOffset? updated = overall.TryGetProperty("updated", out var date) && date.TryGetDateTimeOffset(out var parsed) ? parsed : null;
        return new(at, provider.Name, provider.Page, code == 100 && components.Count == 0 && incidents.Count == 0 ? "operational" : "degraded", overall.GetProperty("status").GetString() ?? "Sem descrição", components.ToArray(), incidents.ToArray(), updated);
    }
}

public static class GameProbe
{
    public static async Task<GameSample> Check(GameTarget target, CancellationToken ct)
    {
        GameCatalog.Validate(target);
        var at = DateTimeOffset.UtcNow;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var (ms, details) = target.Protocol == "A2S" ? await A2S(target, deadline.Token) : await Tcp(target, deadline.Token);
            return new(at, target.Name, target.Host, target.Port, target.Protocol, true, ms, details, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(at, target.Name, target.Host, target.Port, target.Protocol, false, null, "Sem resposta válida ao teste específico", ex is OperationCanceledException ? "Tempo limite de 6s" : ex.Message); }
    }
    private static async Task<(double, string)> Tcp(GameTarget target, CancellationToken ct)
    {
        using var client = new TcpClient { NoDelay = true };
        var clock = Stopwatch.StartNew();
        await client.ConnectAsync(target.Host, target.Port, ct);
        if (target.Protocol == "TCP") return (clock.Elapsed.TotalMilliseconds, "Conexão TCP aceita; não confirma protocolo ou partida.");
        using var stream = client.GetStream();
        using var hello = new MemoryStream();
        hello.WriteByte(0); WriteVarInt(hello, 47);
        var host = Encoding.UTF8.GetBytes(target.Host); WriteVarInt(hello, host.Length); hello.Write(host);
        hello.WriteByte((byte)(target.Port >> 8)); hello.WriteByte((byte)target.Port); hello.WriteByte(1);
        await SendPacket(stream, hello.ToArray(), ct);
        await stream.WriteAsync(new byte[] { 1, 0 }, ct);
        var response = await ReadPacket(stream, ct);
        using var body = new MemoryStream(response);
        if (await ReadVarInt(body, ct) != 0) throw new InvalidDataException("Resposta de status Minecraft inválida");
        var length = await ReadVarInt(body, ct);
        if (length < 0 || length > body.Length - body.Position) throw new InvalidDataException("JSON Minecraft inválido");
        var json = new byte[length]; await body.ReadExactlyAsync(json, ct);
        using var status = JsonDocument.Parse(json);
        var root = status.RootElement;
        var version = root.GetProperty("version").GetProperty("name").GetString();
        var players = root.GetProperty("players");
        var ping = new byte[9]; ping[0] = 1;
        var nonce = Random.Shared.NextInt64(); BinaryPrimitives.WriteInt64BigEndian(ping.AsSpan(1), nonce);
        clock.Restart(); await SendPacket(stream, ping, ct);
        var pong = await ReadPacket(stream, ct);
        if (pong.Length != 9 || pong[0] != 1 || BinaryPrimitives.ReadInt64BigEndian(pong.AsSpan(1)) != nonce) throw new InvalidDataException("Pong Minecraft não corresponde à solicitação");
        return (clock.Elapsed.TotalMilliseconds, $"Status e ping/pong válidos. Versão {version}; {players.GetProperty("online").GetInt32()} / {players.GetProperty("max").GetInt32()} jogadores. Não testa login/partida. Host/porta explícitos, sem descoberta SRV.");
    }
    private static async Task<(double, string)> A2S(GameTarget target, CancellationToken ct)
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(target.Host, ct);
        var address = addresses.FirstOrDefault() ?? throw new IOException("DNS sem endereços");
        using var udp = new UdpClient(address.AddressFamily);
        udp.Connect(address, target.Port);
        var query = new byte[] { 255, 255, 255, 255, 0x54 }.Concat(Encoding.ASCII.GetBytes("Source Engine Query\0")).ToArray();
        var clock = Stopwatch.StartNew();
        await udp.SendAsync(query, ct);
        var response = (await udp.ReceiveAsync(ct)).Buffer;
        if (IsHeader(response, 0x41) && response.Length == 9)
        {
            query = query.Concat(response.Skip(5)).ToArray();
            clock.Restart(); await udp.SendAsync(query, ct);
            response = (await udp.ReceiveAsync(ct)).Buffer;
        }
        var elapsed = clock.Elapsed.TotalMilliseconds;
        if (!IsHeader(response, 0x49) || response.Length < 12) throw new InvalidDataException("A2S sem resposta INFO válida; pacotes fragmentados/comprimidos não suportados");
        var index = 6;
        string ReadString()
        {
            var end = Array.IndexOf(response, (byte)0, index);
            if (end < 0) throw new InvalidDataException("String A2S truncada");
            var result = Encoding.UTF8.GetString(response, index, end - index); index = end + 1; return result;
        }
        var server = ReadString(); var map = ReadString(); _ = ReadString(); var game = ReadString();
        if (index + 9 > response.Length) throw new InvalidDataException("INFO A2S truncado");
        index += 2;
        return (elapsed, $"UDP A2S_INFO válido: {server}; {game}; mapa {map}; {response[index]}/{response[index + 1]} jogadores. Não entra em partida.");
    }
    private static bool IsHeader(byte[] response, byte type) => response.Length >= 5 && response.Take(4).All(b => b == 255) && response[4] == type;
    public static void WriteVarInt(Stream stream, int value)
    {
        var number = (uint)value;
        do { var part = (byte)(number & 0x7F); number >>= 7; stream.WriteByte((byte)(part | (number != 0 ? 0x80 : 0))); } while (number != 0);
    }
    public static async Task<int> ReadVarInt(Stream stream, CancellationToken ct)
    {
        var result = 0; var buffer = new byte[1];
        for (var i = 0; i < 5; i++) { await stream.ReadExactlyAsync(buffer, ct); var part = buffer[0]; result |= (part & 0x7F) << (7 * i); if ((part & 0x80) == 0) return result; }
        throw new InvalidDataException("VarInt inválido");
    }
    private static async Task SendPacket(Stream stream, byte[] packet, CancellationToken ct)
    {
        using var prefix = new MemoryStream(); WriteVarInt(prefix, packet.Length);
        await stream.WriteAsync(prefix.ToArray(), ct); await stream.WriteAsync(packet, ct);
    }
    private static async Task<byte[]> ReadPacket(Stream stream, CancellationToken ct)
    {
        var length = await ReadVarInt(stream, ct);
        if (length is < 1 or > 262144) throw new InvalidDataException("Pacote Minecraft excedeu o limite");
        var packet = new byte[length]; await stream.ReadExactlyAsync(packet, ct); return packet;
    }
}
