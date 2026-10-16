# Validação — Net Inspector 1.1 — 16/10/2026

## 1.4.0 — manutenção de disco, drivers, limpeza e menus (16/10/2026)

- Lixeira consulta o estado antes/depois: vazia não vira falha 0x8000FFFF; HRESULT de sucesso sem esvaziamento não vira sucesso confirmado. Quatro cenários controlados cobertos, sem esvaziar a lixeira real.
- Downloads redirecionada (mais de 30 dias), caches/logs específicos de ferramentas e dumps antigos, todos opcionais e desmarcados. Windows.old usa seleção na Limpeza de Disco elevada do sistema, sem exclusão recursiva de pastas Windows.
- Discos usam defrag /A, /O, /L e cipher /w, com validação da letra, NTFS para sobrescrita, consultas do dispositivo para TRIM/penalidade de busca e confirmação explícita para HDD. Mapa real limitado ao início do bitmap, agregado em 240 arcos; a animação indica atividade. Captura visual de teste usa valores sintéticos declarados no teste.
- Drivers usam Windows Update Agent, seleção de IDs/revisões, nova consulta, resultados individuais e reinício manual. Incluídos links oficiais Intel/Dell/Lenovo/HP/AMD/NVIDIA e identificação do computador; não é uma integração automática com os catálogos desses fabricantes.
- Programas consultam fontes/atualizações ao abrir; instalação silenciosa e aceite automático quando suportados. Menus Arquivo/Sair, seis ferramentas e Ajuda/Sobre implementados.
- Suíte do núcleo: 69 verificações passaram. Runner PowerShell exercitado sem elevação: saída UTF-8, exceção com código 1, saída nativa, cancelamento de processo de teste e compilação do interop do bitmap. Worktrees testadas em repositórios descartáveis: principal, sem referência remota, com alterações, arquivos não versionados, ignorados e bloqueadas são preservadas; secundária limpa com referência é elegível.
- UITests renderizaram as cinco janelas de ferramentas e 14 abas de cada janela principal/relatório. Revisão visual detectou descrições cortadas; alturas corrigidas. Os testes não iniciam consultas reais de atualização ao abrir os diálogos.
- Build Release e publicação sem avisos/erros. EXE único 1.4.0.0 em `artifacts/win-x64-1.4.0/NetInspector.exe`, 119.266.029 bytes; SHA-256 `200FCA181B3D973DD24A80A78A1A2036E55FA38436DDF0A94E015044F80F0196`. Binários anteriores preservados.
- Limites: não houve limpeza de dados reais, remoção de worktrees do usuário, instalação de programas/drivers, TRIM, sobrescrita, desfragmentação ou UAC durante os testes. Instalação COM, atualização externa por fabricante, interação com Limpeza de Disco e leitura real de bitmap elevado ainda exigem homologação. O cancelamento de etapas COM/instaladores pode aguardar a etapa atual. Worktrees gerenciadas devem preferencialmente ser arquivadas pela ferramenta para preservar o vínculo de conversa.

## 1.3.0 — status, energia, eventos e ferramentas (16/10/2026)

- Status “Durante teste de velocidade” e coluna “Teste de velocidade simultâneo” substituem “? Sob carga”, com explicação fixa e detalhes da linha. Mantém separação das amostras em repouso no diagnóstico.
- Terceiro gráfico ao lado da memória: bateria/carga/carregamento ou cabo para computador sem bateria. GetSystemPowerStatus amostrado junto com CPU/memória; informação persistida e opcional em relatórios antigos. Casos de bateria carregando, desconectada, desktop e estados desconhecidos testados com entradas controladas. Nesta máquina, coleta real retornou bateria e cabo conectado.
- Corrigido BOM UTF-16 retido ao decodificar wevtutil e antes de ler XML em relatórios anteriores. XML vazio vira canal sem eventos; DTD/entidades externas bloqueados. Smoke de 35s: 68 pings, nove serviços e cinco canais válidos, sem canais indisponíveis. Revisada captura da interface com eventos reais do System.
- Menu Ferramentas disponível fora da coleta. Atualizador executa winget upgrade --all, instala Microsoft.WinGet.Client/cliente WinGet no escopo do usuário se ausente, captura saída e códigos de erro. Os dois caminhos foram exercitados com funções PowerShell simuladas, sem instalar programas.
- Limpeza oferece RAM, temporários do usuário com mais de 24h, cache de quatro navegadores, cookies, lixeira e Prefetch. Cookies/lixeira/Prefetch desmarcados. Seleção confirmada antes da exclusão; cancelamento mantém alterações anteriores; logs limitados e roláveis. Navegadores abertos ignorados. Arquivos bloqueados, links/junctions e raízes protegidos; testes usam apenas diretórios descartáveis.
- Sem exigência global de administrador. Prefetch usa auxiliar elevado limitado à pasta Windows/Prefetch e arquivos .pf; não recebe caminhos de exclusão externos. Instaladores do WinGet podem solicitar UAC individualmente.
- Build Release sem avisos/erros; suíte final com 65 verificações. O smoke anterior às três verificações adicionais do updater/raiz passou em 82 verificações. UITests passaram em 14 abas da janela inicial e do relatório e nos dois diálogos de ferramentas. Conferidas imagens de bateria, progresso, eventos e opções de limpeza; corrigido corte da última opção.
- EXE único 1.3.0 em artifacts/win-x64-1.3.0/NetInspector.exe. O EXE anterior em artifacts/win-x64 está em execução e não foi encerrado/substituído.
- Limites: nenhuma atualização de programa, limpeza de dados reais/lixeira/Prefetch ou solicitação UAC foi executada nesta validação. Instalação real em Windows sem WinGet, elevação com usuário padrão e transições físicas de bateria/cabo continuam requerendo homologação. A redução de RAM é temporária; não promete ganho de desempenho.

## 1.2.3 — Wi-Fi e organização visual (16/10/2026)

- Captura de comandos preserva os bytes, reconhece UTF-8 estrito e usa a página de código do console/OEM como alternativa. Captura real de netsh e casos UTF-8/OEM passaram; eventos mantêm Unicode explícito. Relatórios antigos com caracteres já corrompidos não são reescritos.
- Wi-Fi, proxy e TCP usam fonte monoespaçada aplicada ao trecho completo e colunas recalculadas, incluindo continuações e preservação de MAC/IPv6. Rolagem horizontal evita quebra das linhas técnicas.
- Orientações fixas e faixa da assistente substituídas por Como usar; conexão/consumo em uma linha, detalhes no tooltip e na descrição acessível. Progresso/ETA em linha própria acima do resumo, separado dos recursos.
- Memória física em coluna vertical segmentada, preenchida de baixo para cima, com percentual e GiB ao lado; ausência de amostra mantém o indicador sem valor.
- 47 verificações passaram, incluindo captura real do netsh. Testes da interface passaram nas 14 abas da janela inicial e do relatório; capturas de progresso em 50%, memória em 62% e Wi-Fi corrigido revisadas. Esses estados visuais são simulados; não representam nova sessão de coleta completa.
- EXE portátil win-x64 1.2.3 gerado. O aplicativo já aberto continua usando a versão anterior até ser fechado e aberto novamente.

Copyright © 2026 Fernando J. Cordeiro @ CONNECT10 INTERNET

## Implementação e verificações

- Um único aplicativo portátil: coleta, abre relatório/checkpoint na mesma janela e exporta diagnóstico em TXT. Publicação em `artifacts/win-x64/NetInspector.exe`, sem DLLs ao lado.
- O EXE publicado 1.1.0 foi iniciado com um relatório real como argumento e permaneceu respondendo. Os executáveis 1.0 foram preservados em `artifacts/legacy/1.0`, fora da distribuição atual.
- Relatórios interpretados em linguagem legível: resumo de velocidade, latência, jitter e perda; alertas coloridos, evidências, hipóteses e ações. Sem aba JSON, sem inventário serializado e sem XML exposto.
- Botões Iniciar, Parar e salvar e Abrir relatório com tamanho/cor/destaque. Logo original renderizado em branco com alpha preservado; não utiliza as variantes geradas que apresentaram ruído.
- Compilação Release sem avisos/erros. Testes de UI renderizam diagnóstico, gráficos, serviços, status, servidores, conexão e eventos; verificam copyright, logo, botões e ausência de JSON na interface.
- Testes determinísticos: orçamento atômico concorrente; download/upload com quatro fluxos e aquecimento em transporte simulado com atraso; parser Statuspage e Status.io; componentes degradados prevalecendo sobre resumo normal; página sem integração nunca classificada como saudável.
- Suíte final sem rede: 26 verificações passaram. Smoke real e status: 44 verificações passaram antes da inclusão adicional da verificação explícita de compatibilidade 1.0. Captura de eventos usa Unicode e RenderedXml para preservar acentos e obter mensagens completas.
- Testes de protocolo em loopback: A2S UDP com challenge e INFO, Minecraft com status e ping/pong/nonce. São testes do protocolo completo, sem partidas.
- APIs reais de Epic, Roblox e Hypixel consultadas e interpretadas como operacionais no horário do teste. A primeira URL Roblox usada era incompatível (404); a integração final foi corrigida para a API pública Status.io e verificada ao vivo.
- Coleta real de 35s no Windows: ICMP, nove serviços de sites/streaming, inventário, status de jogos e servidor público Hypixel. Hypixel retornou resposta de status e ping/pong. Eventos dos cinco canais foram exportados e validados como XML.
- O smoke de velocidade externo é deliberadamente limitado a 12 MB: a transferência terminou antes da janela útil nesta rede e foi corretamente registrada como inconclusiva. O teste de janela/concorrência foi validado no transporte controlado. Não se apresenta esse smoke como aferição de 1 Gbps.
- Regras, compatibilidade do relatório 1.0, reabertura do pacote, corrupção de hash e path traversal também são cobertos.

Evidências de renderização e relatórios reais ficam em `test-results/`, ignorado pelo Git por conter dados locais.

## Homologação ainda necessária

- Medição real sustentada em enlaces de até 1 Gbps; recursos do PC, overhead e destino influenciam a aferição.
- Sessão real de duas horas e múltiplos ciclos de velocidade.
- Windows limpo sem .NET previamente instalado, usuário padrão, Wi-Fi/cabo/VPN/IPv6 e eventos protegidos/desativados.
- Perdas e latência controladas, suspensão e retomada.
- Servidores A2S reais escolhidos pelo suporte, portas de consulta específicas e limitações de fragmentação/compressão.
- ARM64, se essa distribuição for gerada, e assinatura digital para distribuição pública.

Fontes de status podem atrasar ou abranger outras regiões. Os destinos configurados não representam todos os servidores de um jogo; não há teste de login, matchmaking ou partida. Eventos Windows não provam causalidade.


## Redesign CONNECT10 1.2 — 16/10/2026

Esta seção substitui a descrição visual da versão 1.1 acima.

- Tema azul profundo, detalhes azul/laranja CONNECT10, logo colorido original e Victória. Navegação lateral, título/subtítulo com espaçamento calculado pela métrica real da fonte.
- Botões uniformes, arredondados, com hover/foco e desenho independente do tema nativo, inclusive quando desabilitados. Campo de duração com −/+, dropdowns com desenho da lista e checkboxes com contraste em todos os estados.
- Modo escuro nativo .NET 10 para barras de rolagem; moldura clara do controle de abas removida. Capturas de diagnóstico, gráficos, qualidade, serviços, jogos, histórico, PC/rede, Wi-Fi e Windows inspecionadas.
- Estados de pronto, execução e finalização verificados por teste de UI, assim como dimensões e alinhamento das cinco ações e presença dos indicadores por linha. Esse teste de estados não inicia uma coleta pela interface.
- Indicadores com texto: normal/respondeu, atenção/falha e inconclusivo/restrito/consulta manual. ICMP sem respostas ou poucas amostras não recebe aprovação nem diagnóstico automático de queda.
- Histórico filtra a interface da rota IPv4 das sondagens (1.1.1.1), registrada a cada 10s por GetBestInterface. Inventário inicial mostra só a conexão principal. Ambos permitem abrir os adaptadores secundários. Relatórios antigos continuam identificando apenas a rota inicial; VPN e rotas de outros destinos permanecem limitações explícitas.
- Saída netsh decodificada com OEM do Windows nas novas capturas; leitura técnica dividida em seções. Não se tenta reescrever evidências de relatórios antigos.
- 46 verificações passaram na coleta real de 35s, incluindo identificação da rota por amostra, netsh sem caracteres perdidos, 68 pings, nove serviços e cinco canais de eventos. Testes de protocolo e velocidade controlada também passaram. Não houve medição externa de velocidade nessa coleta.
- Testes de UI passaram para janela inicial e relatório. Distribuição portátil atualizada em artifacts/win-x64/NetInspector.exe.

Ainda requer homologação de troca física Wi-Fi/cabo durante a coleta, DPI variados e outros ambientes Windows. As capturas de UI são produzidas por DrawToBitmap; não substituem a interação manual com menus suspensos, diálogos de arquivo e tecnologias assistivas.


## Correções de velocidade 1.2.1 — 16/10/2026

- Reproduzida recusa HTTP 403 no download Cloudflare com requisições de 16 MB; requisições menores aceitas nesta rede. Tamanho máximo reduzido para 4 MB, preservando quatro fluxos, orçamento conservador e buffers de 64 KB. Teste controlado recusa blocos maiores para impedir regressão. Recusas HTTP têm explicação em português e resultado inconclusivo, sem atribuir a falha à conexão do cliente.
- Download real via SpeedTester: 39.558.785 bytes em 4,01s, resultado 81,84 Mbps, sem erro. Validação pontual da transferência, não homologação de velocidade contratada/1 Gbps. Limite de 128 MB respeitado.
- Velocidade passa a repetir em duração/5, limitada a 30s–15min. Em teste de 5min, intervalo de 1min; em 120min, permanece 15min. Os gráficos informam o intervalo; relatórios anteriores mantêm a indicação de 15min. O orçamento e a duração efetiva podem limitar a quantidade de pontos.
- 31 verificações determinísticas passaram, incluindo os intervalos e HTTP 403. Não foi realizada nova coleta real de cinco minutos para verificar cinco pontos de upload.


## 1.2.2 — CPU, memória e ETA (16/10/2026)

- Compilação Release sem avisos ou erros; publicação `win-x64` concluída em EXE único.
- 41 verificações automatizadas passaram, incluindo cálculo de CPU por deltas, primeira leitura sem valor, média/pico, preservação no pacote, leitura de relatórios sem recursos e validação de faixa.
- Smoke real de 35s passou com 61 verificações: 18 amostras de memória e 17 de CPU, média/pico no evento final, checkpoint e pacote com eventos Windows. Sem teste externo de velocidade nesta execução.
- UITests passaram nas 14 abas, com capturas de tela revisadas: dois gráficos fixos, CPU/memória no resumo, barra/ETA aos 50%, parada sem forçar 100%, finalização animada, conclusão em 100% e checkpoint sem ETA ativo.
- Diagnóstico TXT usa a mesma lista de achados que contém o resumo de recursos. Relatórios antigos continuam legíveis com CPU/memória não medidas.
- Monitoramento é total do computador, por amostras a cada 2s. A média é aritmética e o pico é o máximo observado; picos entre intervalos podem escapar. O ETA cobre a coleta; a etapa de eventos Windows e gravação informa duração indeterminada.
- Não foi repetida uma sessão de duas horas nem homologado Windows limpo ou ARM64.
# 1.5.2 - organização do cabeçalho do PDF (16/10/2026)

- Capa reorganizada com logo proporcional à esquerda, identificação à direita, linha divisória, título em uma linha e subtítulo separado.
- Identificação repetida da marca removida da capa; cabeçalhos das páginas internas preservados.
- PDFs com dados e vazio/legado gerados pelo aplicativo e renderizados para revisão visual. Sem mudanças nos dados e anexos do relatório.
- Testes de interface e geração de PDF passaram.

# 1.5.1 - acentuação do WinGet e mensagens de atualização (16/10/2026)

- Configurada a codificação UTF-8 na passagem de comandos nativos pelo PowerShell, antes da gravação do log. Não há substituição posterior de caracteres corrompidos.
- Testes emitindo bytes UTF-8 reais, tanto por comando nativo direto quanto pelo runner de processos, preservaram Concluído, Versão, Disponível e atualizações.
- Consulta real `winget upgrade --disable-interactivity` preservou Versão, Disponível e a mensagem de duas atualizações disponíveis. Não instalou nem atualizou programas.
- Tela do atualizador validada nos estados de versão atual, versão nova e falha, sem explicações sobre releases, navegador ou troca do EXE. Download aparece somente no estado de versão nova. Capturas locais revisadas.
- Testes de interface e runner passaram, incluindo cancelamento e ferramentas existentes.

# 1.5.0 - duração, armazenamento, interface, PDF e atualizações (16/10/2026)

- Padrão de coleta alterado para 15min; máximo mantido em 120min e compartilhado entre controle e coletor. A duração longa é de monitoramento com velocidade periódica, limitada pelo orçamento de dados.
- 87 verificações determinísticas de núcleo passaram, incluindo compatibilidade, protocolos, velocidade controlada, mídias desconhecidas/virtuais, versões, URLs de releases e isolamento das cópias de estado. Coleta real de 35s passou com 106 verificações, 64 pings, nove serviços e cinco canais Windows, antes da otimização adicional do snapshot. Não houve nova medição externa de velocidade.
- Cenário sintético equivalente a 120min: 14.400 pings, 3.600 amostras de recursos, 720 amostras de rede e 1.080 sondagens de serviços. Checkpoint 4.523.480 bytes; PDF integral 8.788.662 bytes em 9,66s (771 páginas porque inclui cada amostra). Snapshot médio reduziu de 113,2ms para 0,3ms neste computador. Não representa uma coleta real contínua de duas horas.
- SSD deste computador identificado por consulta de leitura. HDD, SSD/NVMe, identificação ausente/inconsistente e controladores virtuais/RAID/Spaces exercitados por casos controlados. Volume composto, thin provisioning ou mapeamento físico inacessível impede sobrescrita. A ferramenta elevada relê o tipo, barramento e sistema de arquivos antes de executar.
- Interface: ferramentas renderizadas e revisadas, cabeçalhos tecnológicos, painéis, seletores temáticos, opções de limpeza com rolagem e alinhamento vertical verificado na linha Duração e nas barras de ferramentas. As 14 abas da janela inicial e de relatório passaram; capturas por DrawToBitmap não substituem homologação de DPI e interação manual.
- PDF gerado para sessão com dados e relatório legado/vazio; capa, gráficos vetoriais, todas as categorias, cabeçalhos repetidos, rodapés e paginação renderizados e revisados. Mensagens longas ganham linhas de continuação; session.json e canais originais ficam anexados ao PDF. Evidências locais em test-results são ignoradas no Git.
- Atualizador consulta somente a última release pública estável do repositório oficial, exige versão válida e ativo compatível, ignora prereleases e bloqueia URL de outra origem. Consulta ao iniciar é configurável e não interrompe testes. Verificação manual exibe falhas de rede/limite; download abre a release no navegador. Não há substituição automática do EXE.
- GitHub Actions valida build/testes e gera EXE único e SHA-256; tags com versão correspondente publicam GitHub Releases. Licenças de bibliotecas ficam no repositório, incorporadas no EXE e acessíveis em Ajuda.
- Não foram executadas limpezas de dados reais, instalações, TRIM, sobrescrita ou elevação UAC. Execução real de duas horas, Windows limpo, HDD físico, DPI variados e Windows ARM64 permanecem para homologação.
