# NPhysics

Simulacao interativa de um milhao de esferas rigidas em uma rampa procedural 3D, implementada em C# com OpenTK e OpenGL. Integracao, busca de vizinhos, contatos translacionais/rotacionais e selecao de objetos visiveis sao processados na GPU por compute shaders.

## Sumario

- [Escopo e parametros](#escopo-e-parametros)
- [Requisitos e execucao](#requisitos-e-execucao)
- [Controles](#controles)
- [Arquitetura](#arquitetura)
- [Fluxo de inicializacao e simulacao](#fluxo-de-inicializacao-e-simulacao)
- [Modelo fisico](#modelo-fisico)
- [Dados e buffers da GPU](#dados-e-buffers-da-gpu)
- [Shaders](#shaders)
- [Rampa e obstaculos](#rampa-e-obstaculos)
- [Renderizacao e camera](#renderizacao-e-camera)
- [Desempenho e limitacoes](#desempenho-e-limitacoes)
- [Configuracao](#configuracao)
- [Guias tecnicos](#guias-tecnicos)

## Escopo e parametros

Os valores abaixo descrevem a configuracao implementada atualmente. A solicitacao inicial mencionava 16 objetos variaveis; a configuracao final adotada e fixa em **1.000.000 de esferas**, conforme a instrucao posterior. O total nao aumenta durante a simulacao e esferas que saem da rampa nao sao recriadas.

| Parametro | Valor ou comportamento |
|---|---|
| Dimensao | Fisica de esferas rigidas 3D, com orientacao e velocidade angular |
| Quantidade | Fixa em 1.000.000 |
| Gravidade | 9,81 m/s², na direcao vertical negativa |
| Inclinacao nominal da rampa | 45 graus; pequenas ondulacoes alteram localmente a superficie |
| Passo fixo de fisica | 1/120 s |
| Massa | Independente por esfera, sorteada entre 0,2 e 2,0 kg |
| Raio | Independente por esfera, sorteado entre 0,05 e 0,074 m |
| Atrito estatico | Coeficiente independente entre 0,35 e 0,58 |
| Atrito dinamico | Coeficiente independente entre 0,18 e 0,32 |
| Restituicao | Independente entre 0,1 e 0,75 |
| Colisoes | Esfera-rampa, esfera-esfera e esfera-obstaculo |
| Largura da rampa | 18 m (meia largura de 9 m) |
| Inercia de rotacao | Esfera solida uniforme, `I = 2/5 m r²` |
| Passo e integrador | Passo fixo 1/120 s; integracao semi-implicita |
| Arrasto do ar | Densidade 1,225 kg/m³ e coeficiente de esfera 0,47 |
| Borda da rampa | Limite lateral com reflexao baseada na restituicao da esfera |

Os valores de massa, raio, atrito e restituicao sao sorteados na inicializacao e novamente ao pressionar `R`. Um reinicio substitui o estado/material das mesmas 1.000.000 de esferas; nao altera a quantidade.

## Requisitos e execucao

- Windows, Linux ou macOS com driver OpenGL adequado.
- .NET 10 SDK.
- GPU e driver compativeis com **OpenGL 4.3**, compute shaders, shader storage buffer objects (SSBO) e desenho indireto.
- Memoria de GPU suficiente para manter os buffers de simulacao e renderizacao.

Na raiz do projeto:

```powershell
dotnet run
```

Para compilar sem iniciar a janela:

```powershell
dotnet build
```

O projeto usa OpenTK 4.9.4 e incorpora os arquivos GLSL no assembly durante a compilacao; os shaders nao precisam ser copiados para uma pasta externa na execucao.

## Controles

| Tecla | Acao |
|---|---|
| `Espaco` | Pausar ou continuar a fisica e o movimento da camera |
| `R` | Reinicializar as mesmas 1.000.000 de esferas e seus materiais |
| `F3` | Pausar ou retomar a coleta de diagnosticos |
| `F4` | Salvar uma captura PNG do quadro atual para avaliacao |
| `F5` | Ativar ou pausar a coleta do dataset |

A velocidade inicial da camera e 5,5 unidades por segundo. O numero de discos nao e um controle de runtime: esta fixado em `SimulationSettings.DiskCount`.
O rastreamento inicia ativado e grava uma linha JSONL por amostra em `%LOCALAPPDATA%/NPhysics/traces`. Salva tambem cinco quadros PNG automaticamente no inicio e aceita capturas adicionais por `F4` (limite de 20 por sessao), junto de metadados de camera/tempo no JSONL. As imagens ficam em uma pasta com o mesmo nome-base da sessao e sufixo `-frames`. O caminho da sessao aparece no console. A coleta usa readback GPU assincrono para contadores; capturas de imagem sao esparsas e fazem leitura sincronica do framebuffer. `F3` pausa/retoma a telemetria automatica.

## Fase 1 de coleta neural

A coleta binaria fica desativada por padrao. Ative-a com `F5` ou execute:

```powershell
dotnet run -- --count 4096 --seed 2026 --capture true
```

Os arquivos `.nphysdata` sao gravados em `%LOCALAPPDATA%/NPhysics/datasets/`. A captura usa staging buffers em anel e fences OpenGL; o solver convencional nao usa o caminho neural. Os formatos incrementais podem ser validados pelo `DatasetReader`.

Valide um arquivo capturado sem abrir a janela:

```powershell
dotnet run -- --validate-dataset "C:\Users\Anna Beatriz\AppData\Local\NPhysics\datasets\dataset.nphysdata"
```

## Treinamento local da fase 3

Treine um modelo a partir de um arquivo `.nphysdata`:

```powershell
dotnet run -- --train "C:\Users\Anna Beatriz\AppData\Local\NPhysics\datasets\dataset.nphysdata"
```

O modelo correspondente sera gravado com extensao `.nphysmodel`. A implementacao atual usa uma GNN Encode-Process-Decode em C#, Huber Loss separavel por componente, Adam e serializacao propria. O backprop do decoder esta implementado; o backprop completo do encoder e das duas passagens de mensagens continua pendente antes do treinamento de producao.

Verifique carregamento/upload dos pesos e a equivalencia de uma camada linear na GPU:

```powershell
dotnet run -- --gpu-model-check "%LOCALAPPDATA%\NPhysics\datasets\dataset.nphysmodel"
```

O formato v2 e a organizacao dos pesos no SSBO estao documentados em [docs/model-gpu-layout.md](docs/model-gpu-layout.md). Este probe nao ativa a inferencia GNN nem altera o backend fisico.

## Arquitetura

O codigo e separado por responsabilidade:

| Caminho | Responsabilidade |
|---|---|
| `Program.cs` | Ponto de entrada; cria, executa e descarta a janela |
| `PhysicsSimulation.cs` | Ciclo da janela OpenTK, entrada, acumulador de passo fixo, camera e coordenacao dos componentes |
| `Physics/SimulationSettings.cs` | Parametros centralizados usados pela simulacao e pela geracao de shaders |
| `Physics/GpuPhysics.cs` | Buffers SSBO, inicializacao das esferas, grade espacial, dispatch e estado fisico na GPU |
| `Diagnostics/GpuDiagnostics.cs` | Contadores GPU, queries de tempo e leitura assíncrona de amostras |
| `Diagnostics/SimulationTrace.cs` | Cabecalho de sessao, eventos de coleta e gravacao JSONL |
| `Diagnostics/TelemetryModels.cs` | Estruturas tipadas das janelas CPU/GPU registradas |
| `World/ProceduralTrack.cs` | Funcao de altura da rampa e criacao deterministica de obstaculos por segmento |
| `World/Obstacle.cs` | Dados imutaveis de um obstaculo: posicao, raio, meia extensao, tipo e cor |
| `Rendering/SceneRenderer.cs` | Malhas, buffers de vertices, camera/projecao, desenho da rampa, obstaculos e discos |
| `Rendering/GlProgram.cs` | Compilacao e link de programas GLSL; expoe erros de shader como excecoes |
| `Rendering/ShaderSource.cs` | Le os shaders embutidos e substitui constantes parametrizadas |
| `Shaders/ClearGrid.comp.glsl` | Limpa os cabecalhos da grade espacial |
| `Shaders/BuildGrid.comp.glsl` | Insere cada disco em uma celula hash e constroi as listas ligadas |
| `Shaders/Simulate.comp.glsl` | Integracao translacional/rotacional, contatos com rampa/obstaculos e colisoes esfera-esfera |
| `Shaders/CullDisks.comp.glsl` | Descarta discos fora do frustum e prepara o comando de desenho indireto |
| `Shaders/Surface.vert.glsl` | Posiciona vertices da rampa, obstaculos e discos instanciados |
| `Shaders/Surface.frag.glsl` | Iluminacao direcional simples e cor dos materiais |
| `NPhysics.csproj` | Target .NET, dependencia OpenTK e incorporacao dos shaders como recursos |

O namespace das classes e `NPhysics`. A aplicacao exige contexto OpenGL 4.3 Core.

## Fluxo de inicializacao e simulacao

1. `Program.Main` cria `PhysicsSimulation` e inicia o loop do OpenTK.
2. `PhysicsSimulation.OnLoad` cria o estado da fisica, os programas GPU e as malhas de renderizacao.
3. `GpuPhysics.Reset` gera posicao, velocidade linear, orientacao, velocidade angular e material para cada indice de `0` a `DiskCount - 1`; os arrays iniciais sao enviados aos SSBOs.
4. A cada quadro, `PhysicsSimulation` acumula o tempo real. Para cada intervalo completo de 1/120 s, a fisica avanca um passo; o tempo do quadro e limitado a 1/20 s para evitar grandes saltos.
5. Cada passo limpa a grade, a preenche com as posicoes atuais e executa o shader de simulacao em buffers alternados de entrada/saida (ping-pong).
6. A camera avanca ao longo do eixo X e os obstaculos procedurais sao atualizados somente quando muda o intervalo de segmentos visivel.
7. Durante a renderizacao, um compute shader seleciona esferas visiveis. A cena desenha a rampa, obstaculos e as esferas visiveis; a GPU executa o desenho instanciado indireto.
8. Ao fechar, `SceneRenderer` e `GpuPhysics` liberam programas, VAOs e buffers OpenGL.

A fisica atualiza as 1.000.000 de esferas em cada passo, inclusive as que estao fora da camera. O culling reduz o trabalho de rasterizacao, nao o trabalho de simulacao.

## Modelo fisico

### Integracao e gravidade

O shader aplica aceleracao gravitacional constante `g = 9,81 m/s²` no eixo vertical e integra velocidade e posicao com Euler semi-implicito:

1. Atualiza a velocidade com a gravidade.
2. Atualiza a posicao usando a velocidade atualizada.
3. Resolve o contato com rampa, obstaculos e esferas vizinhas, incluindo a velocidade angular nos pontos de contato.

O passo e fixo em `1/120 s`; o loop do jogo pode executar zero ou mais passos por quadro. Nao ha interpolacao de estado para renderizacao.

### Contato com a rampa

O shader calcula a normal local da rampa por diferencas finitas. O impulso normal e o atrito tangencial sao aplicados no ponto de contato; o atrito altera a velocidade angular e produz rolamento. O contato tambem corrige penetracao e pode aplicar restituicao para impactos normais suficientemente rapidos.

### Colisao entre esferas

Cada corpo e uma esfera rigida de raio e massa variaveis. A resposta de contato usa velocidade no ponto de contato, impulsos normais e tangenciais, inercia de esfera solida (`I = 2/5 m r²`) e torque angular. A orientacao e integrada como quaternion, e o arrasto aerodinamico depende da area projetada e da velocidade. A renderizacao usa esferas billboard em estilo cartoon, sem sombreamento direcional; o giro continua na simulacao fisica, mas nao aparece como marcas visuais. As aproximacoes e limites do solver estao detalhados no [modelo fisico](docs/physics-model.md).

Cada esfera possui massa, raio, orientacao e velocidade angular proprios armazenados nos buffers. A massa afeta a resposta de colisao; a aceleracao de gravidade nao depende diretamente da massa.

### Obstaculos e bordas

Obstaculos podem ser blocos ou cilindros; os contatos estaticos usam os coeficientes de atrito e restituicao da esfera. As bordas laterais da rampa limitam a coordenada Z e refletem a velocidade lateral com a restituicao do material.

## Dados e buffers da GPU

Posicao, velocidade linear e material usam um `vec4` por esfera:

| Buffer SSBO | Binding | Conteudo por elemento |
|---|---:|---|
| Posicao de entrada | 0 | `(x, y, z, raio)` |
| Velocidade de entrada | 1 | `(vx, vy, vz, 0)` |
| Posicao de saida | 2 | `(x, y, z, raio)` |
| Velocidade de saida | 3 | `(vx, vy, vz, 0)` |
| Cabecalhos da grade | 4 | Indice da primeira esfera na lista da celula ou `-1` |
| Links da grade | 5 | Indice da proxima esfera na mesma lista hash |
| Obstaculos | 6 | Dois `vec4` por obstaculo: posicao/raio e meia extensao/tipo |
| Material das esferas | 7 | `(massa, atrito estatico, atrito dinamico, restituicao)` |
| Metricas GPU | 8 | Contadores atomicos usados pelo shader de simulacao (binding 6 no build da grade e 2 no culling) |
| Spin de entrada / orientacao | 9 | Em sim: vetor de estado duplo por corpo; em render: quaternion por corpo |
| Spin de saida | 10 | Orientacao e velocidade angular gravadas pela simulacao |

Posicao, velocidade linear e estado angular sao alternados a cada passo: um conjunto e lido e o outro recebe o resultado. O estado angular usa dois `vec4` por esfera: quaternion `(x,y,z,w)` e velocidade angular `(wx,wy,wz,0)`. O desenho indireto tambem usa um SSBO com o comando OpenGL de cinco inteiros seguido pela lista de indices dos corpos visiveis.

Estimativa nominal dos principais SSBOs para 1.000.000 de esferas:

- Posicoes ping-pong: 32 MB.
- Velocidades ping-pong: 32 MB.
- Orientacao/velocidade angular ping-pong: 64 MB.
- Materiais: 16 MB.
- Links da grade: 4 MB.
- Grade hash de 4.194.304 cabecalhos: 16 MiB.
- Comando indireto e lista de visiveis: aproximadamente 4 MB.

Isso corresponde a aproximadamente 169 MB decimais (cerca de 161 MiB) nos principais buffers. A estimativa nao inclui metricas, malhas, alocacoes internas do driver, framebuffer ou arrays temporarios em CPU. A inicializacao usa arrays temporarios de posicao, velocidade, spin e material, aproximadamente 80 MB no total.

## Shaders

Os shaders sao arquivos GLSL separados e incorporados ao assembly como recursos. `ShaderSource` substitui marcadores por valores de `SimulationSettings` antes da compilacao. A localizacao de um shader ou um erro de compilacao/link produz uma excecao com informacao de diagnostico.

| Compute shader | Grupo local | Trabalho |
|---|---:|---|
| `ClearGrid.comp.glsl` | 256 threads | Define os 4.194.304 cabecalhos hash como `-1` |
| `BuildGrid.comp.glsl` | 128 threads | Calcula uma celula por esfera e insere seu indice com `atomicExchange` |
| `Simulate.comp.glsl` | 128 threads | Avanca uma esfera, resolve contatos e grava estado translacional/rotacional de saida |
| `CullDisks.comp.glsl` | 256 threads | Testa o frustum e acrescenta indices visiveis por contador atomico |

Os shaders graficos desenham um quad por particula em instancias, orientado para a camera, com cor chapada, contorno escuro e brilho grafico. A lista de instancias e criada em GPU e consumida por `glDrawArraysIndirect`; nao ha leitura de volta do estado para CPU a cada quadro. A geometria visual nao altera o estado ou a colisao.

## Rampa e obstaculos

A altura nominal da superficie e:

```text
y(x,z) = -tan(45 graus) * x
         + 0,48 * sin(0,055 * x)
         + 0,16 * sin(0,19 * x + 1,4)
         + 0,065 * sin(0,47 * x)
         + 0,14 * sin(0,55 * z + 0,035 * x)
         + 0,045 * sin(1,25 * z - 0,11 * x)
         + 0,006 * z²
```

A rampa tem meia largura de 9 m. Ondulacoes longitudinais e transversais acentuam cristas e vales; a mesma funcao e usada na inicializacao dos corpos, nos obstaculos, na malha e no contato fisico. A malha visivel cobre uma janela deslizante de 240 por 37 vertices e e atualizada conforme a camera percorre o eixo X. A pista e procedural e estende-se ao longo do percurso.

Os obstaculos sao determinados por hash do numero do segmento, portanto o tipo, posicao, dimensoes e paleta de cada segmento nao dependem de sorteio novo a cada quadro. Ha aproximadamente um obstaculo por segmento de 6 m; caixas e cilindros contrastantes recebem faixas visuais. A lista mantida cobre a area da camera e margens ao redor dela; a capacidade do buffer GPU e de 256 obstaculos por atualizacao.

As esferas iniciais sao arranjadas em colunas ao longo de X, 110 faixas em Z e ate seis camadas verticais. Seus centros e camadas sao posicionados pela normal local da rampa, evitando iniciar enterradas numa superficie inclinada. O espacamento nominal entre faixas/colunas e 0,16 m. A distribuicao ocupa centenas de metros de pista; a camera mostra apenas uma parte do conjunto por vez.

## Renderizacao e camera

- Projecao perspectiva OpenGL, com viewport redimensionavel.
- Rampa como malha triangular com normais calculadas numericamente.
- Particulas como sprites circulares billboard, desenhadas com instancing indireto, cores chapadas vivas, contorno escuro e pequeno brilho cartoon, sem sombreamento de luz.
- A cor de cada particula representa `m * g * (y - yReferencia)`, normalizada pela energia potencial maxima estimada do percurso: vermelho/laranja para baixa energia e ciano/azul para alta.
- A camera segue a rampa por uma vista proxima, elevada e obliqua, enquadrada em um trecho curto para destacar a largura, relevo, obstaculos e esferas em movimento.
- Obstaculos circulares usam malha cilindrica; blocos usam malha cubica.
- O relevo usa bandas e linhas de contorno estilizadas; obstaculos usam paletas contrastantes e padroes de faixa, sem escurecimento por normais ou luz direcional.
- A camera segue o eixo X, olhando para uma posicao adiante sobre a altura media da rampa.
- `CullDisks.comp.glsl` testa os centros contra o frustum com uma margem proporcional ao raio.

## Desempenho e limitacoes

Um milhao e uma quantidade fixa muito alta para uma simulacao interativa. A GPU precisa processar um milhao de invocacoes por passo de fisica e por quadro de culling. O processamento foi movido para compute shaders, e a renderizacao evita desenhar esferas fora da camera, mas o desempenho final depende da GPU, do driver e da densidade local de corpos.

O modelo e uma simulacao customizada, nao um motor rigido completo:

- Cada esfera armazena quaternion e velocidade angular; o corpo e aproximado como esfera solida uniforme com `I = 2/5 m r²`.
- Esferas sao desenhadas como billboards com profundidade reconstruida por pixel; a apresentacao cartoon nao exibe o spin, que permanece ativo no estado fisico.
- O atrito tangencial age no ponto de contato e gera torque, permitindo rolamento e spin.
- A inclinacao de 45 graus refere-se a componente principal; ondulacoes introduzem variacoes locais.
- Obstaculos tratam-se como corpos estaticos; o contato com cilindros verticais e caixas usa geometria analitica aproximada.
- O solver e paralelo e de uma passagem: cada invocacao atualiza apenas uma esfera com base no estado antigo dos vizinhos. Nao ha CCD nem solver iterativo global, portanto agrupamentos densos podem apresentar jitter, penetracao residual e variacao numerica.
- A grade hash pode produzir candidatos extras quando celulas distintas colidem no hash. Os candidatos passam por um teste de contato, mas a implementacao nao armazena a coordenada original da celula para eliminar candidatos duplicados; isso pode aplicar mais de uma correcao ao mesmo par em casos de colisao de hash.
- A telemetria registra metricas agregadas e tempos de GPU, mas nao captura o estado individual dos discos nem implementa um profiler detalhado por invocacao.
- Ao pausar com espaco, o update inteiro para, incluindo fisica e camera. O acumulador limita o delta aceito por quadro, mas nao limita o numero de passos de recuperacao se a GPU ficar muito atrasada.

## Configuracao

Os parametros publicos da simulacao ficam em `Physics/SimulationSettings.cs`. Para manter a fisica e os shaders sincronizados, constantes usadas em GLSL sao substituidas por `Rendering/ShaderSource.cs`.

Alterar `DiskCount`, `GridBucketCount`, `GridCellSize` ou `WorkGroupSize` afeta memoria, desempenho e/ou a busca espacial. `GridBucketCount` deve permanecer potencia de dois porque o shader usa mascara de bits. Alterar a geometria, o atrito, o estado angular ou a resposta a colisao requer atualizar `Shaders/Simulate.comp.glsl`; alterar a malha ou o desenho requer `Rendering/SceneRenderer.cs` e os shaders graficos.

Para alterar a rampa, atualizar em conjunto `SimulationSettings.RampSlope`, `World/ProceduralTrack.cs` e a funcao `groundHeight` do compute shader. A geometria billboard das particulas e independente da inclinacao visual e nao muda a fisica.

Antes de enviar uma alteracao fisica, conferir que os parametros em C# e GLSL continuam iguais, que o passo do shader de grade corresponde ao passo de simulacao e que todos os bindings SSBO usados no shader estao associados antes do dispatch.

## Guias tecnicos

Os documentos complementares detalham o projeto sem transformar este arquivo em uma listagem repetida de codigo:

- [Arquitetura e ciclo de vida](docs/architecture.md): responsabilidades, dependencias, ownership de recursos e sequencia de inicializacao.
- [Modelo fisico e contatos](docs/physics-model.md): coordenadas, rampa, integracao, atrito, restitucao e limites das aproximacoes.
- [Pipeline GPU e memoria](docs/gpu-pipeline.md): SSBOs, hash espacial, dispatches, sincronizacao e culling.
- [Parametros, desempenho e diagnostico](docs/operations.md): faixas configuraveis, estimativas, sintoma/causa e roteiro de verificacao.
- [Rastreamento e diagnosticos](docs/diagnostics.md): campos do trace, custos, controles e como usar os dados para orientar otimizacoes.
