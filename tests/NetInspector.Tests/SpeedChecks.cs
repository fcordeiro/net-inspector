using System.Net;
using NetInspector.Core;

internal static class SpeedChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(NetInspector.Core.Monitor.SpeedInterval(5) == TimeSpan.FromMinutes(1), "teste de 5 minutos mede velocidade a cada minuto");
        check(NetInspector.Core.Monitor.SpeedInterval(120) == TimeSpan.FromMinutes(15) && NetInspector.Core.Monitor.SpeedInterval(1) == TimeSpan.FromSeconds(30), "intervalo de velocidade respeita limites em coletas curtas e longas");
        using var http = new HttpClient(new PacedHandler());
        foreach (var direction in new[] { "download", "upload" })
        {
            var result = await new SpeedTester(http).Measure(direction, 128_000_000, CancellationToken.None, seconds: 2.2, warmup: .2);
            check(result.Mbps > 0 && result.Error == null && result.Streams == 4 && result.MeasurementSeconds >= 1.8, "velocidade " + direction + " usa 4 fluxos e janela após aquecimento");
            check(result.Bytes > 0 && result.Bytes <= result.BudgetBytes && result.BudgetBytes <= 128_000_000, "contagem e orçamento no " + direction);
        }
        using var denied = new HttpClient(new DeniedHandler());
        var refused = await new SpeedTester(denied).Measure("download", 32_000_000, CancellationToken.None);
        check(refused.Mbps == null && refused.Bytes == 0 && refused.Error?.Contains("HTTP 403") == true && refused.Error.Contains("não comprova falha"), "recusa HTTP não vira velocidade nem diagnóstico de queda");
    }
    private sealed class DeniedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }
    private sealed class PacedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                using var sink = new PacedStream();
                await request.Content!.CopyToAsync(sink, cancellationToken);
                return new(HttpStatusCode.OK);
            }
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            if (int.Parse(query["bytes"]) > 4_000_000) return new(HttpStatusCode.Forbidden);
            return new(HttpStatusCode.OK) { Content = new StreamContent(new PacedStream()) };
        }
    }
    private sealed class PacedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(20, cancellationToken); buffer.Span.Clear(); return Math.Min(buffer.Length, 65536);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => await Task.Delay(20, cancellationToken);
    }
}
