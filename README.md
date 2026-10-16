# Net Inspector 1.5.3

Copyright © 2026 Fernando J. Cordeiro @ CONNECT10 INTERNET

Um único aplicativo nativo para Windows 10/11 x64: monitora a conexão por até duas horas e abre os próprios relatórios para análise offline. O EXE 1.5.3 está em `artifacts/win-x64-1.5.3/NetInspector.exe` e nas [releases oficiais](https://github.com/fcordeiro/net-inspector/releases). Versões anteriores locais foram preservadas. Feche a janela anterior e abra o novo EXE.

## Novidades da versão 1.5

- **Ícone:** identidade própria com lupa e nós de rede em azul e laranja CONNECT10, aplicada ao EXE e às janelas. PNG original, ICO com dez resoluções e prompt de criação estão em `src/NetInspector.Desktop/Assets/`.
- **Duração:** padrão de 15 minutos, mínimo de 1 e máximo de 120. Quinze minutos são um ponto inicial prático; duas horas permitem observar falhas intermitentes. O máximo é de monitoramento, com testes de velocidade periódicos. O orçamento de dados continua limitando as transferências. Mantenha o computador ligado e sem suspensão.
- **Interface:** cabeçalhos tecnológicos, painéis azul profundo, hierarquia de títulos, seletores temáticos, ações uniformes e alinhamento vertical da linha Duração. Limpeza oferece rolagem das opções em janelas menores.
- **Discos:** identificação automática por consultas somente de leitura ao dispositivo. SSD, HDD e mídia não identificada são apresentados com a evidência. Sobrescrita fica bloqueada em SSD, RAID, Storage Spaces, mídias virtuais, volumes compostos ou identificação inconclusiva. O processo elevado verifica novamente a mídia antes da operação; permanece a confirmação da ação destrutiva, sem pedir ao usuário para identificar SSD/HDD.
- **PDF:** Arquivo > Salvar relatório completo em PDF (Ctrl+Shift+S), após finalizar ou abrir uma coleta. Capa com marca, diagnóstico, gráficos de CPU/memória/velocidade/latência, inventário, configurações, extensões, eventos Windows e tabelas de todas as medições. Inclui anexos internos com os dados originais. Coletas extensas podem produzir centenas de páginas por preservar todas as amostras. Campos ausentes aparecem como não medidos; o PDF não coleta retroativamente dados de relatórios antigos.
- **Hardware:** novas coletas incluem modelo de processador, núcleos, memória física, fabricante/modelo do PC, versão/build do Windows e armazenamento, quando o Windows permite a consulta.
- **Atualizações:** Ajuda > Verificar atualizações consulta a última release estável e valida a existência do EXE da arquitetura correspondente. A busca ao iniciar vem habilitada e pode ser desativada no menu Ajuda; a preferência é salva em LocalAppData/NetInspector. Somente a consulta é automática: o usuário abre as notas e baixa o EXE no navegador, fecha o aplicativo e substitui o arquivo. Nenhum executável é baixado ou instalado em segundo plano. A tela informa se há uma versão nova ou se o aplicativo está atualizado; falhas de consulta recebem uma mensagem curta.

O teste sintético equivalente a 120 minutos gerou 14.400 pings, 3.600 leituras de recursos, 720 leituras de rede e 1.080 sondagens de serviços: checkpoint de cerca de 4,5 MB e PDF integral de cerca de 8,8 MB em aproximadamente 10 segundos neste computador. A cópia do estado para a interface foi otimizada de aproximadamente 113 ms para 0,3 ms nesse cenário. Essa validação de volume e uma coleta real curta não substituem a homologação contínua de duas horas.

Não requer instalação, runtime .NET previamente instalado ou DLLs ao lado do EXE. O runtime vem incorporado e pode extrair componentes para o cache interno do Windows. Binário ainda sem assinatura digital. O logo colorido original CONNECT10 e a imagem da Victória são embutidos. O tema escuro usa azul CONNECT10, laranja, navegação lateral, controles consistentes e barras de rolagem escuras. O rodapé e novos relatórios usam o ano vigente.

## Usar

1. Abra `NetInspector.exe`. Para investigar, escolha a duração e o limite de consumo de dados dos testes de velocidade e clique em **Iniciar**.
2. Escolha a pasta para salvar e mantenha o PC ligado, sem suspensão. **Parar e salvar** encerra antecipadamente e prepara o relatório.
3. Para analisar uma coleta, clique em **Abrir relatório**, arraste um `.netinspect`/checkpoint `.json` para a janela ou passe o caminho como argumento ao EXE.
4. O arquivo é interpretado em um resumo com métricas, alertas por gravidade, evidências, possíveis causas e ações recomendadas. As outras abas mostram tabelas e gráficos legíveis. JSON e XML não são exibidos ao usuário.
5. Use **Exportar diagnóstico** para obter um TXT. Use **Servidores de jogos** para configurar até 10 destinos antes da coleta.

Os dados são salvos em JSON atomicamente a cada 15s. Esse checkpoint permite recuperação de uma coleta parcial; até 15s podem se perder em uma interrupção abrupta. O `.netinspect` final é criado ao encerrar, incluindo os eventos Windows. Essa finalização pode levar até aproximadamente 100s adicionais. O app precisa de pasta gravável e não impede a suspensão do PC.

## Interface e leitura dos resultados

- **CPU, memória e energia:** três gráficos fixos na interface principal mostram CPU total em gauge e memória física em barra (percentual e GiB). A coleta começa junto com o teste e amostra a cada 2s, incluindo o inventário inicial. Ao lado da memória, a bateria mostra carga e se está carregando; sem bateria, aparece um desenho do cabo e a fonte de alimentação informada pelo Windows. Esses dados ficam no relatório; arquivos antigos mostram energia não medida. A primeira leitura de CPU aguarda o próximo intervalo. Contadores indisponíveis aparecem sem valor.
- **Médias e picos:** o Resumo e diagnóstico, o TXT exportado, os eventos do programa e os arquivos `.json`/`.netinspect` preservam CPU e memória. A média é aritmética das amostras válidas e o pico é o maior valor observado. São recursos do computador inteiro; picos entre amostras podem escapar. Relatórios antigos indicam que esses dados não foram medidos.
- **Progresso e ETA:** a barra mostra o percentual do tempo de coleta e a contagem regressiva. Interromper o teste preserva o percentual alcançado. Durante a preparação do relatório, a barra fica animada e informa que o ETA dessa etapa é indisponível (eventos Windows podem levar cerca de 100s). Um checkpoint aberto não apresenta uma coleta ativa.

- **Iniciar teste** passa a **Teste em execução** durante a coleta; **Parar e salvar** fica disponível apenas durante o teste e passa a **Salvando** na finalização. Abrir, exportar e configurar destinos respeitam o estado da sessão.
- Indicadores por linha: **✓** medição normal/resposta observada, **✕** atenção/falha observada, **?** inconclusivo, acesso restrito ou consulta manual. Esses símbolos têm texto e cores próprias; não dependem só da cor.
- **Conexão no tempo** mostra por padrão a interface da rota IPv4 para 1.1.1.1 a cada 10s. VPN pode ser essa interface. Relatórios anteriores mostram apenas o adaptador da rota inicial, com essa limitação identificada. Outros destinos e IPv6 podem seguir rotas distintas.
- **Meu PC e rede** mostra o inventário inicial e a conexão principal. Adaptadores secundários ficam disponíveis pela opção avançada, assim como o histórico completo.
- **Wi-Fi e configurações** separa Wi-Fi, proxy e TCP por títulos, com texto monoespaçado. Novas capturas netsh usam a codificação OEM do Windows; arquivos antigos preservam o texto originalmente capturado.
- O diagnóstico explica observações, possíveis causas e ações recomendadas. O tema nativo escuro requer suporte do Windows 10/11; os botões e seletores têm desenho próprio para manter contraste.

## Velocidade: Mbps e GB

**Mbps é velocidade. GB é consumo de dados.** O antigo texto de 600 MB era um orçamento de tráfego, não um teto de velocidade.

Agora são usados quatro fluxos HTTP simultâneos para Cloudflare, por até 8s em cada direção, com o primeiro segundo reservado ao aquecimento. O teste inicia com a coleta e se repete em intervalo proporcional à duração (30s a 15min). Cada requisição transfere até 4 MB, evitando a recusa HTTP 403 observada com blocos de 16 MB. As transferências usam buffers pequenos e upload em streaming, sem alocar gigabytes na memória. Não há corte de resultado em 1.000 Mbps.

O perfil foi dimensionado para investigar enlaces de até 1 Gbps. Isso não garante que uma medição alcance o plano contratado: rota, servidor, Wi-Fi, enlace negociado, recursos do PC e protocolo também influenciam. A homologação real em um enlace de 1 Gbps continua necessária.

O limite de dados é configurável em **2, 5, 20 ou 50 GB** por sessão, com 20 GB selecionados inicialmente. Em duas horas de uma conexão simétrica de 1 Gbps, oito ciclos de 8s por direção podem consumir aproximadamente **16 GB de payload**. Cabeçalhos, DNS e demais sondagens ficam fora desse orçamento. O download usa até metade do saldo de cada ciclo, preservando saldo para upload.

As reservas de bytes são atômicas entre fluxos; requisições interrompidas ou com erro mantêm a reserva, portanto o limite é conservador. Ao esgotar o orçamento, só os testes de velocidade param. Uma reserva pequena pode terminar antes de uma janela útil: nesse caso, a velocidade aparece como não medida e o motivo é registrado, em vez de inventar um número.

## Jogos: status e servidores

- **Consulta automática a cada 5min:** Epic/Fortnite/Rocket League (Statuspage), Roblox (Status.io) e Minecraft/Hypixel (Statuspage). São interpretados o status publicado, componentes afetados, incidentes e manutenções ativas, com horário da consulta e atualização da fonte.
- **Consulta manual identificada:** Riot/LoL/Valorant (Brasil), Xbox/Minecraft, PlayStation, Activision/CoD, Rockstar/GTA, Ubisoft/Rainbow Six, EA/FC/Apex/Battlefield e Steam/CS2/Dota 2. Steamstat é identificado como fonte independente. O botão abre a fonte selecionada. Não se presume funcionamento apenas porque uma página responde.
- **Minecraft Java:** handshake de status, resposta estruturada e ping/pong TCP com verificação de nonce. Mostra versão, jogadores e RTT do ping. O destino padrão é o servidor público internacional `mc.hypixel.net:25565`; não representa todos os servidores Minecraft. Não há descoberta DNS SRV: configure o host e a porta finais.
- **A2S / Source:** consulta UDP A2S_INFO, incluindo challenge quando solicitado. Mostra resposta válida, RTT, nome, mapa e jogadores. Configure um servidor compatível e a porta de consulta real. Pacotes A2S fragmentados/comprimidos não são suportados e aparecem como inconclusivos.
- **TCP:** apenas conexão a host/porta explícitos. Não confirma o protocolo de um jogo UDP.

Amostras dos servidores são coletadas a cada minuto, com timeout de 6s por destino. Sem login, credenciais, matchmaking ou entrada em partidas. A falta de resposta pode significar filtro, destino/porta incorretos, rota ou indisponibilidade. Taxa de respostas de protocolo não é perda de pacotes IP. Status publicado pode ter atraso e abrangência regional diferente.

As antigas sondagens dos portais comerciais dos jogos foram removidas das novas coletas. Relatórios antigos continuam abrindo.

## Demais medições e interpretação

| Medição | Intervalo e dados |
|---|---|
| ICMP | A cada 2s para gateway IPv4 inicial, Cloudflare, Google e Quad9; timeout de 1,5s; latência média, P95, jitter e perda observada por destino |
| Sites e streaming | A cada 60s: DNS, TCP 443 e HTTPS para Facebook, Instagram, TikTok, YouTube, X, Globoplay, Netflix, HBO/Max e Prime Video; até quatro sondagens concorrentes, timeout total de 12s |
| Adaptadores | Inventário inicial; estado, velocidade negociada, bytes e erros a cada 10s |
| PC e navegador | Sistema, rota padrão IPv4 por métrica combinada, IPs, DNS, gateway, navegador padrão para HTTPS, Wi-Fi, proxy WinHTTP e configurações TCP |
| Extensões | Perfis padrão do usuário atual em Chrome, Edge, Brave e Firefox. Estado ativo apenas quando disponível; versões antigas e nomes localizados podem não ser identificados |
| Windows | Na finalização, até 400 avisos/erros das últimas 24h em cada um dos cinco canais: System, Application, WLAN, NetworkProfile e DNS Client. Canais inacessíveis geram motivo explícito. A interface interpreta XML em linhas de eventos |

O status **Durante teste de velocidade** significa ping medido junto com download/upload. Essa condição não indica falha por si só. A coluna **Teste de velocidade simultâneo** e os detalhes da linha explicam a comparação com os períodos sem transferência.

Amostras ICMP durante transferências de velocidade são identificadas e separadas no diagnóstico. Regras ICMP exigem no mínimo 10 amostras sem carga: perda parcial ≥2%, jitter >20ms e RTT médio >100ms. Jitter é a média das diferenças absolutas entre RTTs consecutivos, excluindo perdas e intervalos ≥10s. P95 usa nearest rank. Não é jitter RTP.

HTTP 401/403/429 significa acesso restrito; ausência total de ICMP é inconclusiva. DNS pode vir do cache. HTTPS mede resposta até os cabeçalhos, sem renderização de página ou reprodução de vídeo. Link negociado é diferente de velocidade contratada. Eventos Windows são apresentados para correlação de horário; não há atribuição automática de causa ou culpa ao provedor.

O gateway é o identificado no começo: troca de rede/VPN pode desatualizá-lo. HTTPS segue IPv4/IPv6 do sistema, mas alvos ICMP são IPv4. Eventos de suspensão e falhas entre intervalos podem escapar. O monitoramento não altera configurações nem exige administrador. As ferramentas de manutenção atuam somente após confirmação; discos, drivers e limpeza de sistema podem solicitar elevação específica.

## Ferramentas

O menu **Ferramentas** fica disponível com a coleta parada:

- **Atualizar programas:** ao abrir, atualiza as fontes e executa a consulta `winget upgrade` antes de permitir instalação. Depois da revisão e confirmação, usa `winget upgrade --all --silent --accept-source-agreements --accept-package-agreements --disable-interactivity`. Se o cliente não estiver disponível, solicita instalar o Instalador de Aplicativos e repetir a consulta. Instaladores podem pedir UAC. Cancelar não desfaz instalações concluídas.
- **Liberar memória:** RAM, temporários com mais de 24h, caches de navegadores, cookies, lixeira, Prefetch, Downloads com mais de 30 dias (inclusive subpastas), caches/logs de ChatGPT/Codex, Claude, Visual Studio/Code e Antigravity, além de dumps de falhas com mais de sete dias. Todos os novos itens, cookies, lixeira e Prefetch começam desmarcados. Downloads usa a pasta conhecida do Windows, respeitando redirecionamentos. Caches ficam limitados a diretórios conhecidos e ferramentas abertas são ignoradas; configurações, sessões, credenciais, projetos e extensões não são alvos. RAM liberada pode ser reutilizada imediatamente, sem promessa de ganho de desempenho.
- **Windows.old/atualizações antigas:** opção desmarcada que abre a Limpeza de Disco do próprio Windows, com elevação. Selecione as categorias e confirme na janela do sistema; a disponibilidade depende do Windows. Remover a instalação anterior elimina a possibilidade de voltar à versão anterior. O aplicativo não toma posse nem exclui pastas do sistema diretamente.
- **Limpar worktrees:** descoberta limitada em pastas conhecidas ou escolhidas. Só oferece worktrees secundárias registradas, sem alterações, arquivos ignorados/não versionados, submódulos ou bloqueios, com HEAD alcançável por referência remota local. Ferramentas abertas preservam suas pastas gerenciadas conhecidas. Revalida cada seleção e usa `git worktree remove` sem `--force`; mantém branches. Referências remotas locais não são uma nova verificação do servidor. Para preservar o vínculo e o estado da conversa, prefira arquivar na própria ferramenta.
- **Discos:** análise (`defrag /A`), otimização apropriada à mídia (`/O`), TRIM (`/L`, somente quando o dispositivo confirma suporte) e sobrescrita de espaço livre (`cipher /w`, somente NTFS com HDD confirmado pelo dispositivo e pelo usuário). A sobrescrita pode levar horas e ocupar o espaço livre temporariamente. Não é uma garantia de apagamento de mídia física; snapshots, backups e blocos remapeados podem reter dados. Dispositivos desconhecidos bloqueiam as operações específicas.
- **Mapa orbital:** 240 arcos agregam o bitmap real dos primeiros até 8.388.608 clusters. Verde/azul mostram proporção livre/ocupada; o mapa é uma amostra de alocação, não um mapa de fragmentação nem uma animação dos movimentos físicos. Atualiza antes/depois da operação quando a consulta é suportada. A faixa laranja indica atividade e o log mostra a saída real do Windows.
- **Atualizar drivers:** busca drivers pelo Windows Update Agent e instala somente IDs/revisões selecionados e confirmados, com nova consulta antes da instalação, aceite de termos e resultado por driver. Sem reinício automático. Busca/download/instalação COM podem precisar terminar antes do cancelamento. Inclui identificação de fabricante/modelo e acesso aos serviços oficiais Intel, Dell, Lenovo, HP, AMD e NVIDIA; esses serviços abrem no navegador e fazem sua própria detecção/instalação. Não há catálogo externo universal integrado.
- **Arquivo > Sair** preserva a finalização de coleta em andamento; **Ajuda > Sobre** mostra versão e autoria.
- Navegadores abertos são ignorados, sem encerrá-los; feche-os e tente novamente. Cookies removidos encerram sessões. Senhas, favoritos e histórico não são alvos da limpeza. Arquivos bloqueados/protegidos são ignorados e informados; junctions/links não são percorridos. Prefetch limita a exclusão a `.pf` e executa somente essa etapa em um processo elevado, se necessário. As próximas aberturas podem ficar mais lentas.
- O log informa arquivos removidos, espaço em disco e itens ignorados. Cancelamento mantém o que já foi feito. As exclusões são permanentes; nenhuma rotina de limpeza é executada automaticamente.

Eventos Windows com BOM Unicode, inclusive em relatórios antigos, são normalizados antes da interpretação. Canal vazio aparece como ausência de avisos/erros; indisponibilidade preserva o motivo do Windows. Não são ativados canais desativados nem exigido administrador para todos os eventos.

## Formato e privacidade

`.netinspect` é ZIP contendo `manifest.json`, `session.json` e `windows/*.xml` ou `windows/*.error.txt`. O leitor valida SHA-256, versão, duplicatas, nomes e limites (64MB compactado, 128MB descompactado, 32 entradas), sem extrair arquivos para disco. SHA-256 verifica corrupção, não autoria. O esquema 1 foi mantido com campos opcionais adicionais para compatibilidade com relatórios 1.0.

O pacote não é criptografado e pode incluir IPs locais, SSID/BSSID, nomes de perfis, extensões e mensagens de outros programas. Não coleta cookies, senhas, histórico de navegação ou conteúdo de páginas. Não envia o relatório automaticamente. Os testes se conectam aos destinos configurados, Cloudflare e fontes de status.

## Desenvolvimento e testes

Requer SDK .NET 10 apenas no computador de desenvolvimento.

```powershell
dotnet build NetInspector.slnx -c Release
dotnet run --project tests/NetInspector.Tests -c Release
dotnet run --project tests/NetInspector.Tests -c Release -- --status-smoke
dotnet run --project tests/NetInspector.Tests -c Release -- --smoke --speed-smoke
dotnet run --project tests/NetInspector.UITests -c Release -- test-results/smoke.netinspect
dotnet run --project tests/NetInspector.UITests -c Release -- --long-session
./scripts/publish.ps1 -OutputDirectory artifacts/win-x64-1.5.3
```

Os testes incluem orçamento concorrente, transferências com aquecimento em transporte controlado, status com incidentes, Minecraft e A2S completos em servidores locais, formato de relatório e diagnóstico. `--status-smoke` consulta as três fontes reais. `--smoke` monitora o Windows real por 35s; `--speed-smoke` limita o tráfego externo a 12 MB e pode corretamente produzir velocidade inconclusiva por falta de duração. `UITests` renderiza as abas e verifica botões, logo, relatório e ausência de JSON na interface. Evidências ficam em `test-results` e não devem ser publicadas, pois podem conter dados reais.

Para ARM64, publique com `./scripts/publish.ps1 -Runtime win-arm64` e homologue nessa arquitetura. Os testes não substituem uma sessão de duas horas, um Windows limpo e enlaces controlados de até 1 Gbps.

## Referências

- [Otimização/defrag](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/defrag), [cipher](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cipher), [bitmap de alocação](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_get_volume_bitmap) e [Windows Update Agent](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/searching--downloading--and-installing-updates).

- [Publicação de EXE único no .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)
- [Identificação de mídia pelo Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_device_seek_penalty_descriptor)
- [API de releases do GitHub](https://docs.github.com/en/rest/releases/releases)
- [PDFsharp e MigraDoc](https://docs.pdfsharp.net/) (MIT)
- [Endpoints de velocidade Cloudflare](https://github.com/cloudflare/speedtest)
- [Status Epic](https://status.epicgames.com/api), [Status Hypixel](https://status.hypixel.net/api), [Status Roblox](https://status.roblox.com/), [API pública Status.io](https://kb.status.io/developers/public-status-api/)
- [Consulta A2S da Valve](https://developer.valvesoftware.com/wiki/Server_queries)
- [Servidor Hypixel](https://hypixel.net/play?addr=mc.hypixel.net)
- [Steamstat: endpoint não destinado a terceiros](https://github.com/SteamDatabase/steamstat.us/blob/main/README.md) — apenas link para consulta manual; não usa sua API interna.
