# Rastreamento da simulacao

Este guia descreve o trace implementado para observar estabilidade, custo de simulacao e comportamento da cena ao longo do tempo.

## Objetivos e limites

O trace foi desenhado para ajudar a responder:

- Quanto trabalho a simulacao faz por segundo?
- O custo cresce por busca de candidatos, contatos ou rasterizacao?
- A grade hash esta concentrando insercoes?
- A simulacao produz estados nao finitos ou velocidades extremas?
- Quanto do trabalho e submetido pela CPU e quanto tempo a GPU leva?
- Quantas esferas estao visiveis por quadro e quantos passos fisicos sao executados?

O objetivo nao e gravar o estado de cada esfera. 100.000 posicoes, velocidades lineares e angulares a cada segundo provocariam transferencia, armazenamento e custo muito grandes. O sistema agrega contadores na GPU e registra metadados de cada janela.

## Ativacao e arquivos

Ao iniciar, a coleta fica ativada. Um arquivo de sessao novo e criado em:

```text
%LOCALAPPDATA%/NPhysics/traces/simulation-<UTC timestamp>.jsonl
```

Capturas de imagem ficam em uma pasta irma `simulation-<UTC timestamp>-frames`, com arquivos PNG numerados. Cinco quadros sao salvos automaticamente durante os primeiros segundos; `F4` salva um quadro adicional sob demanda. O limite e de 20 imagens por sessao. O caminho completo do trace aparece no console da aplicacao. Cada linha e um objeto JSON independente; arquivos nao sao reescritos em execucoes futuras. `F3` pausa ou retoma a telemetria automatica. `R` reinicia as esferas e grava evento `reset`, incluindo a posicao da camera e estado de pausa quando o trace esta ativo.

Fechar a aplicacao descarrega o escritor para que o arquivo possa ser analisado imediatamente. Se o diretorio nao puder ser criado ou o arquivo nao puder ser aberto, a inicializacao falha explicitamente em vez de simular uma coleta bem-sucedida.

## Tipos de linha

### Sessao

A primeira linha tem `type: "session"` e `schemaVersion`. Registra data UTC, fabricante/modelo/versao OpenGL e parametros que contextualizam as amostras, incluindo quantidade de esferas, passo, gravidade, inclinacao, grade e faixas de materiais. Comparacoes devem considerar essas configuracoes e a mesma GPU/driver.

### Amostra

Uma linha `type: "sample"` e capturada aproximadamente a cada segundo. O conteudo inclui:

- `captureId`, instante UTC de captura e duracao da janela;
- quantidade de quadros, passos fisicos, obstaculos ativos, posicao da camera, foco e estado pausado;
- médias de tempo CPU por quadro para submissao dos passos de fisica e renderizacao;
- candidatos testados, contatos esfera-esfera/rampa/obstaculo e insercoes em buckets ocupados;
- contagem de estados invalidos, maior velocidade linear/angular e soma de esferas visiveis em cada culling;
- duracao das ultimas queries GPU concluidas para limpeza da grade, construcao da grade, simulacao, culling e desenho.

Os contadores GPU sao limpos depois que uma janela e copiada para um buffer de readback. A leitura CPU ocorre apenas quando uma fence indica que a copia ja terminou. A consulta a fence usa timeout zero. Quatro slots evitam bloquear o frame; se estiverem todos pendentes, a captura e adiada, e a janela seguinte acumula mais tempo.

Os contadores detalhados usam atomics globais nos shaders e podem serializar uma carga de muitas esferas. Para mais de 4.096 esferas, o projeto os desativa para preservar a responsividade; os tempos GPU por query e o registro JSONL continuam ativos. Nesse modo, campos de contagem ficam zerados e o cabecalho `session` registra `detailedGpuCountersAvailable: false`. A ativacao/desativacao por mouse e processada uma vez por clique, nao repetidamente enquanto o botao fica pressionado.

### Eventos

Linhas `type: "event"` documentam reinicializacoes. Linhas `type: "capture"` registram ativacao e pausa da instrumentacao. Esses registros permitem separar mudancas de estado intencionais de instabilidade numerica.

### Quadros de imagem

Cada linha `type: "frame"` aponta para um PNG relativo ao diretorio do trace e registra numero sequencial, resolucao, horario UTC, tempo de simulacao, `cameraX`, estado de foco e pausa. A imagem e lida do framebuffer depois do desenho e antes da troca de buffers; os arquivos podem ser abertos diretamente para comparar a cena correspondente com as amostras de telemetria ao redor. A leitura de pixels e sincrona, por isso as capturas sao limitadas aos primeiros cinco quadros espaçados por segundo e a pedidos explicitos com `F4`, ate 20 por sessao.

## Definicao e interpretacao dos campos

| Campo | Como e medido | O que pode indicar |
|---|---|---|
| `candidateChecks` | Cada item candidato visitado durante a busca hash em todas as invocacoes | Alto valor sem aumento equivalente de contato sugere trabalho de busca improdutivo |
| `diskContacts` | Uma contagem por invocacao de esfera que encontra sobreposicao com outra esfera | Mede resolucoes locais, nao pares unicos; um par pode contribuir mais de uma vez |
| `rampContacts` | Invocacoes cuja esfera entra no ramo de resposta da rampa | Crescimento mostra maior quantidade de corpos em contato com a superficie |
| `obstacleContacts` | Sobreposicoes esfera-obstaculo encontradas | Indica atividade perto de obstaculos |
| `gridBucketConflicts` | Insercao que encontra bucket hash nao vazio | Combina encadeamento normal e colisao de coordenadas no hash |
| `invalidStates` | Posicao, velocidade linear/angular ou orientacao de saida contem NaN ou infinito | Qualquer valor positivo e um problema; o trace detecta, mas nao corrige |
| `maximumSpeed` | Maximo de `length(velocity)` entre invocacoes em cada passo da janela | Ajuda a encontrar explosoes numericas e impactos extremos |
| `maximumAngularSpeedRadiansPerSecond` | Maximo de `length(angularVelocity)` entre invocacoes, em rad/s | Ajuda a encontrar spin extremo e instabilidade de contato |
| `visibleDiskFrames` | Soma das esferas visiveis produzidas por cada dispatch de culling | Dividir por `frameCount` para estimar media de esferas visiveis por quadro |
| `lastGridClearGpuMilliseconds` | Ultima query `TIME_ELAPSED` da limpeza da grade concluida | Custo do clear de buckets; util para avaliar quantidade de buckets |
| `lastGridBuildGpuMilliseconds` | Ultima query `TIME_ELAPSED` da construcao da grade concluida | Custo para inserir os corpos e competir por atomicos |
| `lastSimulationGpuMilliseconds` | Ultima query `TIME_ELAPSED` de um passo de simulacao concluida | Custo de integracao e resolucao de contato |
| `lastCullingGpuMilliseconds` | Ultima query `TIME_ELAPSED` do dispatch de culling concluida | Custo de teste de frustum das esferas |
| `lastDrawingGpuMilliseconds` | Ultima query `TIME_ELAPSED` do lote de desenho concluida | Custo GPU do desenho da cena |
| `averagePhysicsSubmissionMilliseconds` | Soma do tempo CPU ao submeter fisica dividida por todos os quadros da janela | Custo de driver/submissao; quadros sem passo tambem entram no divisor |
| `averageFrameMilliseconds` | Media de `FrameEventArgs.Time` entre quadros renderizados | Complementa `frameCount`; nao e um FPS suavizado |
| `averageRenderSubmissionMilliseconds` | Tempo CPU em `Render` e `SwapBuffers` dividido por quadros da janela | Pode incluir espera de VSync; nao substitui timer GPU |
| `physicsStepCount` | Numero de passos fixos executados na janela | Comparado ao `frameCount`, revela quadros que executam multiplos passos |
| `cameraX`, `focused`, `paused`, `obstacleCount` | Snapshot de contexto na captura | Contextualiza a regiao de pista e o estado de execucao |

`candidateChecks` pode incluir a mesma esfera varias vezes devido a buckets hash compartilhados e percurso de vizinhanca. `diskContacts` pode contar um contato por ambas as esferas, e duplicatas de hash tambem podem repetir resolucoes. Nao some esses valores como se fossem pares unicos.

## Metodologia para comparar alteracoes

1. Use o mesmo hardware, driver, resolucao e cena. Registre informacoes do cabeçalho de sessao.
2. Deixe a simulacao aquecer e capture janelas equivalentes.
3. Use `R` para reproduzir o layout inicial; materiais aleatorios podem mudar, portanto resultados nao sao perfeitamente deterministas.
4. Compare varias amostras consecutivas, nao apenas um pico ou uma unica janela.
5. Para estudar custo da busca, compare `candidateChecks` e `gridBucketConflicts` com contatos e `lastGridBuildGpuMilliseconds`/`lastSimulationGpuMilliseconds`.
6. Para estudar instabilidade, observe `invalidStates`, `maximumSpeed` e `maximumAngularSpeedRadiansPerSecond` antes e depois da mudanca.
7. Para distinguir gargalo CPU/GPU, compare submissao CPU com os cinco tempos GPU por estagio.
8. Pressione `F3` e repita a captura sem atomicos de telemetria para estimar o overhead da instrumentacao.
9. Preserve os traces antes/depois junto da alteracao avaliada.

A coleta detalhada usa atomicos em candidatos e contatos; ela nao e um benchmark sem overhead. O modo desligado elimina esses atomicos e timer queries, permitindo uma comparacao mais proxima do custo original. Diferencas pequenas precisam de repeticoes. O trace nao implementa correlacao entre esferas individuais nem snapshots completos.

## Sugestoes de proxima instrumentacao

Adicione novas metricas apenas para testar uma hipotese concreta, e mantenha os campos agregados:

- Distribuicao de comprimento das listas por bucket para identificar hotspots.
- Histograma de velocidade ou profundidade de penetracao para avaliar qualidade fisica.
- Marcadores de obstaculo/segmento, se uma zona da pista concentrar custo.
- Captura esparsa de indices selecionados e apenas sob demanda para investigar um corpo individual.
- Exportacao CSV ou scripts de relatorio separados, deixando o trace original como fonte.

Antes de adicionar uma coleta por disco, estime bytes por amostra e custo de leitura. Nao introduza `GL.Finish` nem `GetBufferSubData` sem fence pronta no caminho de cada frame.
