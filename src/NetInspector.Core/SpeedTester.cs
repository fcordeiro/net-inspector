using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace NetInspector.Core;

// Allocation is conservative: failed or interrupted HTTP requests retain their reservation.
public sealed class TransferBudget(long limit)
{
    private long reserved;
    public long Reserved => Interlocked.Read(ref reserved);
    public int Reserve(int requested)
    {
        while (true)
        {
            var current = Reserved;
            var allocation = (int)Math.Min(requested, Math.Max(0, limit - current));
            if (allocation == 0) return 0;
            if (Interlocked.CompareExchange(ref reserved, current + allocation, current) == current) return allocation;
        }
    }
}

public sealed class SpeedTester(HttpClient http)
{
    public async Task<SpeedSample> Measure(string direction, long allowance, CancellationToken ct, double seconds = 8, double warmup = 1)
    {
        var at = DateTimeOffset.UtcNow;
        var budget = new TransferBudget(allowance);
        var clock = Stopwatch.StartNew();
        long bytes = 0, measuredBytes = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(seconds));
        void Progress(int count)
        {
            Interlocked.Add(ref bytes, count);
            if (clock.Elapsed.TotalSeconds >= warmup) Interlocked.Add(ref measuredBytes, count);
        }
        async Task Worker()
        {
            try
            {
                while (!window.IsCancellationRequested)
                {
                    var size = budget.Reserve(4_000_000);
                    if (size == 0) return;
                    if (direction == "download")
                    {
                        using var response = await http.GetAsync($"https://speed.cloudflare.com/__down?bytes={size}&nonce={Guid.NewGuid():N}", HttpCompletionOption.ResponseHeadersRead, window.Token);
                        response.EnsureSuccessStatusCode();
                        using var stream = await response.Content.ReadAsStreamAsync(window.Token);
                        var buffer = new byte[65536];
                        var total = 0;
                        while (total < size)
                        {
                            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, size - total)), window.Token);
                            if (count == 0) throw new IOException("Download incompleto");
                            total += count; Progress(count);
                        }
                    }
                    else
                    {
                        using var content = new StreamingPayload(size, Progress);
                        using var request = new HttpRequestMessage(HttpMethod.Post, "https://speed.cloudflare.com/__up") { Content = content };
                        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, window.Token);
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested) { }
            catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
            {
                errors.Add($"O servidor de velocidade recusou o {(direction == "download" ? "download" : "upload")} (HTTP {(int)ex.StatusCode.Value}). Tente novamente mais tarde; essa recusa não comprova falha na sua conexão.");
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Worker()));
        clock.Stop();
        var measurementSeconds = Math.Max(0, clock.Elapsed.TotalSeconds - warmup);
        var error = ct.IsCancellationRequested ? "Interrompido" : !errors.IsEmpty ? string.Join("; ", errors.Distinct()) : bytes == 0 ? "Sem transferência" : measurementSeconds < 1 ? "Dados insuficientes: orçamento esgotado antes de uma janela útil" : null;
        return new(at, direction, error == null ? measuredBytes * 8 / measurementSeconds / 1_000_000 : null, bytes, clock.Elapsed.TotalSeconds, error, budget.Reserved, 4, measurementSeconds);
    }
    private sealed class StreamingPayload : HttpContent
    {
        private readonly int size;
        private readonly Action<int> progress;
        public StreamingPayload(int size, Action<int> progress)
        {
            this.size = size; this.progress = progress;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        protected override bool TryComputeLength(out long length) { length = size; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Write(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => Write(stream, cancellationToken);
        private async Task Write(Stream stream, CancellationToken ct)
        {
            var buffer = new byte[65536]; Random.Shared.NextBytes(buffer);
            for (var remaining = size; remaining > 0;)
            {
                var count = Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count), ct);
                progress(count); remaining -= count;
            }
        }
    }
}
