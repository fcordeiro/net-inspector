namespace NetInspector.Core;

public sealed record Finding(string Severity, string Title, string Evidence, string Cause, string Action);
public sealed record PingStats(string Target, int Samples, int Received, double LossPercent, double? AverageMs, double? P95Ms, double? JitterMs);
public static class Diagnosis
{
    public static PingStats Stats(string target, IEnumerable<PingSample> source)
    {
        var samples = source.OrderBy(p => p.At).ToArray();
        var values = samples.Where(p => p.Success && p.Ms.HasValue).Select(p => p.Ms!.Value).Order().ToArray();
        var deltas = samples.Zip(samples.Skip(1)).Where(p => p.First.Success && p.Second.Success && p.First.Ms.HasValue && p.Second.Ms.HasValue && p.Second.At - p.First.At < TimeSpan.FromSeconds(10)).Select(p => Math.Abs(p.Second.Ms!.Value - p.First.Ms!.Value)).ToArray();
        return new(target, samples.Length, values.Length, samples.Length == 0 ? 0 : (samples.Length - values.Length) * 100.0 / samples.Length,
            values.Length == 0 ? null : values.Average(), values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * .95) - 1], deltas.Length == 0 ? null : deltas.Average());
    }
    public static List<Finding> Analyze(Session s)
    {
        var findings = new List<Finding>();
        findings.Add(new(s.Resources.Count == 0 ? "Inconclusivo" : "Informação", "Uso de CPU e memória do computador", ResourceSummary.Describe(s), "Consumo total do computador, incluindo outros programas. Média aritmética e pico das amostras de 2s; picos entre amostras podem não aparecer.", s.Resources.Count == 0 ? "Este relatório não contém medições de recursos. Execute uma nova coleta para registrá-las." : "Se o consumo estiver elevado durante a falha, confira os processos no Gerenciador de Tarefas e compare os horários. Estes dados não isolam a causa da lentidão."));
        foreach (var adapter in s.Network.GroupBy(n => n.Adapter))
        {
            var ordered = adapter.OrderBy(n => n.At).ToArray();
            var changes = ordered.Zip(ordered.Skip(1)).Count(p => p.First.Status != p.Second.Status);
            if (changes > 0) findings.Add(new("Atenção", adapter.Key + ": estado do enlace mudou", $"{changes} mudanças observadas em amostras de 10s.", "Cabo, Wi-Fi, suspensão, troca de rede ou adaptador virtual podem alterar o estado.", "Correlacionar com o horário da falha e eventos Windows; mudanças entre amostras podem não aparecer."));
            var rx = ordered.Zip(ordered.Skip(1)).Sum(p => Math.Max(0, p.Second.ReceiveErrors - p.First.ReceiveErrors));
            var tx = ordered.Zip(ordered.Skip(1)).Sum(p => Math.Max(0, p.Second.SendErrors - p.First.SendErrors));
            if (rx + tx > 0) findings.Add(new("Atenção", adapter.Key + ": erros de interface", $"Incrementos observados: RX {rx}, TX {tx}.", "Possível problema de enlace ou driver; depende dos contadores fornecidos pelo adaptador.", "Conferir cabo, sinal Wi-Fi e driver; não atribuir ao provedor sem outros testes."));
        }
        var groups = s.Pings.Where(p => !p.UnderLoad).GroupBy(p => p.Target).Select(g => Stats(g.Key, g)).ToArray();
        foreach (var p in groups.Where(p => p.Samples >= 10))
        {
            if (p.Received == 0) findings.Add(new("Inconclusivo", $"{p.Target}: sem resposta ICMP", $"{p.Samples} tentativas sem resposta, fora dos testes de velocidade.", "Bloqueio ou despriorização de ICMP, alvo inacessível ou falha de rede. Não confirma perda de tráfego de aplicações.", "Conferir DNS/TCP/HTTPS e comparar outros alvos no mesmo horário."));
            else if (p.LossPercent >= 2) findings.Add(new("Atenção", $"{p.Target}: perda ICMP observada", $"{p.LossPercent:F1}% em {p.Samples} amostras sem carga de velocidade.", p.Target == "Gateway" ? "Possível instabilidade local (Wi-Fi, cabo, roteador); ICMP também pode ser despriorizado." : "Possível congestionamento, rota externa ou despriorização ICMP; não identifica o responsável.", "Comparar gateway e alvos externos no mesmo período; repetir por cabo."));
            if (p.JitterMs > 20) findings.Add(new("Atenção", $"{p.Target}: variação de latência", $"Jitter ICMP médio {p.JitterMs:F1} ms; RTT médio {p.AverageMs:F1} ms.", "Possível disputa pelo enlace, sinal Wi-Fi ou congestionamento; métricas não isolam a causa.", "Repetir com rede ociosa e cabo; comparar outro equipamento."));
            if (p.AverageMs > 100) findings.Add(new("Atenção", $"{p.Target}: latência elevada", $"RTT médio {p.AverageMs:F1} ms; P95 {p.P95Ms:F1} ms.", "Distância ao destino ou fila/congestionamento; não indica automaticamente defeito.", "Comparar com o histórico e outros destinos próximos."));
        }
        foreach (var group in s.Services.GroupBy(p => p.Name))
        {
            var failures = group.Count(p => p.Outcome.EndsWith("_failed", StringComparison.Ordinal));
            if (failures > 0) findings.Add(new("Atenção", group.Key + ": falha de acesso observada", $"{failures}/{group.Count()} sondagens falharam. Última falha: {group.Last(p => p.Outcome.EndsWith("_failed", StringComparison.Ordinal)).Outcome}.", "Falha na etapa indicada pode envolver DNS, rede, TLS, proxy, firewall ou o destino. Não comprova indisponibilidade geral do serviço.", "Consultar a linha do tempo e a mensagem técnica; comparar outros serviços."));
            if (group.Any(p => p.Outcome == "restricted")) findings.Add(new("Informação", group.Key + ": resposta restrita", "Resposta HTTP 401, 403 ou 429 recebida.", "HTTPS respondeu; pode haver autenticação, proteção contra robôs ou limite de requisições.", "Verificar acesso no navegador. Não classificar como queda da internet."));
            if (group.Any(p => p.Outcome == "http_error")) findings.Add(new("Atenção", group.Key + ": erro HTTP", "O servidor retornou erro HTTP; consultar código nos dados.", "Resposta do destino ou intermediário; transporte HTTPS funcionou.", "Comparar acesso real e status do serviço."));
        }
        var successfulSpeeds = s.Speeds.Where(p => p.Mbps.HasValue).ToArray();
        if (successfulSpeeds.Length == 0) findings.Add(new("Inconclusivo", "Velocidade não medida", "Nenhuma transferência concluída.", "Teste desativado, cancelado ou endpoint indisponível.", "Consultar erros e repetir se necessário."));
        else findings.Add(new("Informação", "Velocidade medida", string.Join("; ", successfulSpeeds.GroupBy(p => p.Direction).Select(g => $"{(g.Key == "download" ? "Download" : "Upload")}: média {g.Average(p => p.Mbps):F2} Mbps ({g.Count()} testes)")), successfulSpeeds.All(p => p.Streams == 1) ? "Relatório de versão anterior: transferências curtas, que podem subestimar enlaces rápidos." : "Medição HTTP com 4 fluxos e aquecimento. Rota, servidor de teste, Wi-Fi e capacidade do PC podem limitar o resultado.", "Comparar com o plano contratado, o link negociado e outro teste por cabo. 1 Gbps equivale a 1.000 Mbps; GB indicam consumo, não velocidade."));
        foreach (var provider in s.ProviderStatus.GroupBy(p => p.Provider))
        {
            var affected = provider.Where(p => p.State == "degraded").ToArray();
            if (affected.Length > 0) findings.Add(new("Atenção", provider.Key + ": incidente publicado", $"{affected.Length} consulta(s) com incidente/degradação. {affected[^1].Summary} {string.Join("; ", affected[^1].Incidents)} {string.Join("; ", affected[^1].Components)}", "O próprio operador publicou um problema ou manutenção. Isso não prova que o incidente afetou este usuário ou sua região.", "Consultar os componentes afetados e horários na aba Status dos jogos e na fonte oficial."));
            else if (provider.Any(p => p.State == "operational")) findings.Add(new("Informação", provider.Key + ": sem incidentes publicados", $"A fonte oficial não reportou incidentes na consulta mais recente de {provider.Last(p => p.State == "operational").At.ToLocalTime():HH:mm:ss}.", "O status publicado pode ter atraso ou não abranger todos os servidores/regiões.", "Comparar com os testes do servidor específico e a experiência dentro do jogo."));
            if (provider.Any(p => p.State == "unavailable")) findings.Add(new("Inconclusivo", provider.Key + ": status não consultado", provider.Last(p => p.State == "unavailable").Summary, "Falha de consulta, proteção da API ou indisponibilidade da fonte não prova queda do jogo.", "Abrir a página oficial na aba Status dos jogos."));
        }
        foreach (var target in s.GameServers.GroupBy(p => new { p.Name, p.Host, p.Port, p.Protocol }))
        {
            var successes = target.Where(p => p.Success && p.Ms.HasValue).ToArray();
            var failed = target.Count(p => !p.Success);
            var average = successes.Length > 0 ? successes.Average(p => p.Ms!.Value) : (double?)null;
            findings.Add(new(failed > 0 ? "Atenção" : "Informação", target.Key.Name + (failed > 0 ? ": teste do servidor falhou" : ": servidor respondeu"), $"{successes.Length}/{target.Count()} respostas válidas via {target.Key.Protocol}; latência média {(average.HasValue ? average.Value.ToString("F1") + " ms" : "não medida")}. Destino {target.Key.Host}:{target.Key.Port}.", failed > 0 ? "Destino indisponível, porta/protocolo incorretos, filtro, rota ou rede. Não é uma medida de perda de pacotes IP nem prova queda global do jogo." : "O teste confirma resposta apenas do destino e protocolo indicados; não testa login, matchmaking ou uma partida.", "Correlacionar o horário com a página de status. Para jogos diferentes, configurar o servidor e a porta corretos."));
        }
        if (s.Events.Any(e => e.Message.Contains("Limite de dados", StringComparison.Ordinal))) findings.Add(new("Informação", "Limite de consumo dos testes atingido", "As medições de velocidade foram suspensas para respeitar o limite de dados escolhido.", "O orçamento em GB limita consumo, não a velocidade em Mbps.", "Aumentar o limite na próxima coleta para manter testes de velocidade por mais tempo."));
        if (s.State == "running" || !s.EndedAt.HasValue) findings.Add(new("Inconclusivo", "Coleta parcial", "Sessão sem finalização.", "Checkpoint de uma coleta ainda ativa ou interrompida.", "Usar relatório final quando disponível."));
        if (groups.All(g => g.Samples < 10)) findings.Add(new("Inconclusivo", "Amostragem insuficiente", "Menos de 10 amostras em cada alvo sem carga.", "Coleta muito curta ou testes sob carga.", "Monitorar por mais tempo."));
        if (s.Events.Any(e => e.Level == "error") || s.State == "error") findings.Add(new("Atenção", "Erro interno de coleta", "Consultar eventos do programa.", "Parte da coleta pode estar ausente.", "Repetir após verificar a mensagem de erro e permissões de gravação."));
        if (findings.All(f => f.Severity is "Informação")) findings.Insert(0, new("Informação", "Sem anomalias nos testes disponíveis", "Nenhum limiar de alerta foi excedido.", "Não exclui falhas intermitentes fora da amostragem ou específicas do navegador/aplicativo.", "Correlacionar horário da reclamação com as medições."));
        return findings;
    }
}
