# Parametros, desempenho e diagnostico

Este guia explica onde configurar o projeto, como interpretar seus custos e como investigar falhas comuns.

## Tabela de parametros

Os valores efetivos estao em `Physics/SimulationSettings.cs`.

| Constante | Valor | Efeito |
|---|---:|---|
| `DiskCount` | 1.000.000 | Numero padrao de corpos; `--count` altera a quantidade para a execucao atual |
| `Seed` | 2026 | Seed inicial; pode ser substituida por `--seed` |
| `WorkGroupSize` | 128 | Tamanho local usado nos shaders de grade e simulacao |
| `GridBucketCount` | 4.194.304 | Numero de buckets hash; deve ser potencia de dois |
| `GridCellSize` | 0,2 | Escala em que posicoes sao convertidas para coordenadas de celula |
| `FixedStep` | 1/120 s | Intervalo fixo de integracao |
| `Gravity` | 9,81 m/s² | Aceleracao gravitacional |
| `InclinationDegrees` | 45° | Inclinacao nominal usada pela superficie e pelo contato |
| `MinimumRadius`, `MaximumRadius` | 0,05–0,074 m | Faixa de raio sorteada por esfera |
| `MinimumMass`, `MaximumMass` | 0,2–2,0 kg | Faixa de massa sorteada por esfera |
| `MinimumStaticFriction`, `MaximumStaticFriction` | 0,35–0,58 | Faixa de atrito estatico |
| `MinimumDynamicFriction`, `MaximumDynamicFriction` | 0,18–0,32 | Faixa de atrito dinamico |
| `MinimumRestitution`, `MaximumRestitution` | 0,1–0,75 | Faixa de restituicao |
| `LaneCount` | 110 | Numero de posicoes laterais na distribuicao inicial |
| `VerticalLayerCount` | 6 | Camadas verticais na formacao inicial |
| `SpawnSpacing` | 0,16 m | Espacamento entre colunas e faixas |
| `VerticalLayerSpacing` | 0,15 m | Separacao entre camadas de spawn |
| `SolidSphereInertiaFactor` | 0,4 | Fator de inercia de esfera solida uniforme (`I = fator * m * r²`) |
| `AirDensity` | 1,225 kg/m³ | Densidade usada no arrasto aerodinamico |
| `AirDragCoefficient` | 0,47 | Coeficiente de arrasto usado no modelo quadratico |
| `ObstacleMeshHalfThicknessRatio` | 0,12 | Espessura visual relativa ao raio para malhas circulares de obstaculos |
| `RampHalfWidth` | 9 m | Metade da largura da rampa |

## Ajustar valores com seguranca

### Alterar quantidade de discos

`DiskCount` tem padrao de 1.000.000 e pode ser alterado no inicio do processo:

```powershell
dotnet run -- --count 4096 --seed 2026
```

Altera buffers, dispatches, reset e culling. O custo cresce aproximadamente de forma linear no trabalho por disco, mas contatos podem ficar mais caros de forma nao linear quando a densidade local aumenta. O valor precisa ser positivo; reiniciar a aplicacao e necessario para alterar a quantidade.

Os controles interativos aparecem como botoes no canto superior esquerdo da janela: pausar/continuar, resetar, alternar diagnosticos, salvar PNG, alternar coleta de dataset e abrir o relatorio HTML mais recente. Os atalhos `Espaco`, `R` e `F3`-`F5` continuam como alternativas. O clique executa uma unica acao por pressao, inclusive se o botao do mouse ficar mantido. A captura de dataset e o trace JSONL de diagnosticos iniciam automaticamente; `DATA OFF`/`F5` e `TRACE ON/OFF`/`F3` permitem controla-los de forma independente. `--capture false` desativa a captura automatica para uma execucao. Cada SSBO da captura e limitado a 256 MiB e respeita o limite do driver. O readback mantem dois slots; para simulacoes grandes, a frequencia e reduzida automaticamente para limitar transferencias a aproximadamente 64 MiB/s. Em 100.000 esferas, um quadro ocupa 153.600.064 bytes e e amostrado a cada 275 passos. A captura encerra ao atingir um milhao de amostras (aproximadamente 1,5 GB para 100 mil esferas), gera um relatorio HTML automaticamente e deixa a simulacao/diagnosticos ativos. Ao fechar antes da meta, o relatorio e gerado para os dados validos coletados. Configuracoes acima da capacidade sao recusadas sem encerrar a simulacao e informadas no console. Os arquivos `.nphysdata` e relatorios ficam no diretorio LocalAppData padrao; o trace JSONL e gravado em `%LOCALAPPDATA%/NPhysics/traces`. Validacao e ferramentas de modelo sao comandos one-shot:

```powershell
dotnet run -- --self-test
dotnet run -- --validate-dataset "C:\path\to\capture.nphysdata"
dotnet run -- --dataset-report "C:\path\to\capture.nphysdata"
dotnet run -- --train "C:\path\to\capture.nphysdata"
dotnet run -- --gpu-model-check "C:\path\to\capture.nphysmodel"
dotnet run --project TrainerApp\NPhysics.Trainer.csproj -c Release -- --samples 25000 --batch-size 256
dotnet run --project ModelSimulator\NPhysics.ModelSimulator.csproj -c Release -- "C:\path\to\model.nphysmodel" --count 2048
```

`--dataset-report` gera um painel HTML local com configuracao e qualidade do dataset. O treinador separado seleciona o dataset mais recente por padrao, permite escolher arquivo/saida/limites, ignora amostras com overflow e executa minibatches limitados em memoria. Seu painel `.training.html` mostra a loss de treino; ainda nao ha split de validacao nem garantia de generalizacao. `--gpu-model-check` compara o forward GNN com a referencia C# sem iniciar a simulacao.

`ModelSimulator` carrega um `.nphysmodel` e substitui o solver Teacher por inferencia GNN para os deltas de estado. A preparacao de nos/vizinhos e feita na CPU, a inferencia na GPU e o estado previsto e lido sincronicamente para aplicar limites de seguranca; por isso use inicialmente poucas esferas. Contatos com obstaculos ainda nao sao tratados, e o caminho nao tem estabilidade acumulada certificada; permanece separado do executavel principal. `--steps N` fecha automaticamente apos N passos para smoke tests. O trace marca o backend e o caminho do modelo usado.

### Alterar grade hash

`GridBucketCount` deve continuar sendo potencia de dois porque os shaders aplicam mascara de bits. Aumentar o numero de buckets usa mais memoria, mas tende a reduzir colisao de hash; nao elimina a necessidade do teste de contato. Diminuir buckets economiza memoria, mas pode alongar listas e aumentar candidatos.

`GridCellSize` precisa ser compatível com o raio maximo e a extensao da consulta. O shader consulta apenas uma celula em cada direcao; se as celulas ficarem menores que a distancia de contato que precisa ser detectada, pode deixar de encontrar vizinhos. Mudancas devem ser feitas em `SimulationSettings` e injetadas em todos os shaders que dependem do valor.

### Alterar passo fisico

Diminuir `FixedStep` aumenta a frequencia de atualizacao e o custo por segundo de simulacao. Aumenta a estabilidade para contatos rapidos, mas nao substitui CCD. Aumentar o passo pode causar penetracao, atravesamento ou energia numerica incorreta.

### Alterar inclinacao

A inclinacao e usada para calcular a altura nominal da rampa e a funcao de contato. O desenho das particulas usa billboards orientados pela camera, sem depender da inclinacao. A altura procedural completa precisa continuar identica entre `ProceduralTrack.Height` e `groundHeight` no compute shader. Os marcadores GLSL sao substituidos por `ShaderSource`, enquanto `ProceduralTrack` usa diretamente `SimulationSettings`.

### Alterar faixa de materiais

Massa, raio e coeficientes sao sorteados em `GpuPhysics.Reset`. A massa afeta inversa de massa e os impulsos; o raio afeta posicoes, contatos e aparencia; atrito altera o movimento tangencial; restituicao altera o ressalto. Manter coeficientes positivos e faixas ordenadas.

## Memoria e inicializacao

Os principais SSBOs ocupam aproximadamente 169 MB decimais para um milhao de esferas, incluindo posicoes, velocidades lineares e os dois buffers ping-pong de orientacao e velocidade angular. Adicionalmente:

- a inicializacao cria temporariamente quatro arrays C#: posicao, velocidade e material ocupam cerca de 16 MB cada; spin ocupa 32 MB, totalizando aproximadamente 80 MB;
- o driver pode alocar espaco auxiliar;
- malhas e framebuffer consomem memoria adicional;
- a lista de indices visiveis tem capacidade para um milhao de indices mesmo que poucos objetos aparecam na camera.

O reset recria arrays e reenvia os dados completos. Para testar com menos corpos, use `--count` no startup.

## Desempenho: o que e e nao e otimizado

### Otimizado

- Integracao e contatos executados na GPU.
- Posicoes nao sao copiadas da GPU para a CPU a cada quadro.
- Busca de vizinhos via grade hash, em vez de testar todos os pares.
- Estado ping-pong evita leitura e escrita concorrentes no mesmo buffer de estado.
- Desenho indireto com lista compacta de discos visiveis.
- Billboard esferico de seis vertices compartilhado por todas as instancias.
- Lista de obstaculos enviada apenas quando muda o intervalo de segmentos.

### Ainda custa mesmo fora da camera

- A fisica avanca cada esfera a cada passo.
- A grade e limpa em todos os seus buckets a cada passo.
- A grade e construida para todos os discos.
- O culling testa todos os discos por quadro.
- Colisoes de hash podem aumentar o numero de candidatos verificados.
- Se a aplicacao ficar atrasada, o acumulador pode solicitar varios passos consecutivos.

## Problemas comuns

### Erro GLSL `undefined variable`

Uma variavel usada pelo shader foi removida ou renomeada sem atualizar todas as referencias. O numero de linha no erro refere-se ao texto GLSL enviado ao driver, nao necessariamente a uma linha C#.

Procedimento:

1. Abrir o shader indicado.
2. Ir a linha indicada pelo log (considerar que o driver pode contar a partir de zero).
3. Procurar declaracao, parametros da funcao e usos da variavel.
4. Compilar de novo e testar a inicializacao OpenGL, nao apenas `dotnet build`.

### Shader/resource nao encontrado

Confirmar o nome da chamada `ShaderSource.Load`, o nome do arquivo dentro de `Shaders/` e o recurso embutido `NPhysics.Shaders.<nome>.glsl`. Confirmar tambem que o `EmbeddedResource` do csproj inclui os GLSL.

### Build passa, mas janela falha ao iniciar

O build C# nao compila shaders GLSL. O erro pode ocorrer somente em `OnLoad`. Verificar:

- suporte OpenGL 4.3 do driver;
- log de compilacao/link do shader;
- recurso GLSL embutido;
- placeholders substituidos;
- bindings e tamanho dos SSBOs;
- suporte a compute e desenho indireto.

### Tela vazia ou objetos ausentes

Verificar viewport, matriz view-projection, matriz de modelo, frustum do culling, contador indireto e bindings dos buffers de posicao/indices visiveis. Confirmar que `instanceCount` e zerado antes do dispatch do culling e que as barreiras incluem o consumidor.

Para separar renderizacao de fisica, pode-se temporariamente desenhar uma malha fixa sem culling. Nao remover permanentemente diagnosticos de erros OpenGL.

### Fisica instavel ou discos penetrando

Verificar passo fixo, raio/espacamento, normal de contato, eixo de colisao, cell size, consulta de vizinhos e impulso apenas para corpos em aproximacao. O modelo nao usa CCD nem solver iterativo; impactos muito rapidos e pilhas densas podem continuar instaveis.

### Lentidao extrema ou travamento

O trabalho de um milhao de corpos por passo e exigente. Verificar uso de GPU, temperatura, versao do driver e os tempos por estagio e contadores agregados no [trace de diagnosticos](./diagnostics.md). O projeto nao registra amostras individuais por disco nem mantem contador de FPS separado.

## Checklist antes de alterar

- [ ] A quantidade continua consistente com os tamanhos de todos os buffers?
- [ ] Os workgroups nao ultrapassam os limites do OpenGL do dispositivo?
- [ ] O hash mask corresponde a uma quantidade de buckets potencia de dois?
- [ ] A busca visita celulas suficientes para o raio maximo?
- [ ] O layout `vec4` corresponde ao stride esperado no C#?
- [ ] Todos os bindings estao associados antes do dispatch/desenho?
- [ ] Ha barreira entre produtor e consumidor?
- [ ] O mesmo formato de rampa e usado por CPU, compute shader e malha?
- [ ] A malha renderizada corresponde ao collider declarado na documentacao?
- [ ] O build passa e os shaders compilam em uma sessao OpenGL?
- [ ] O caminho de descarte libera cada novo handle GPU?

## Reproducibilidade

`PhysicsSimulation` usa uma semente base deterministica para criar novos objetos `Random`, mas a geracao inicial de materiais depende do estado consumido pela sequencia. A ordem de insercao no grid usa atomics e pode variar entre execucoes. Portanto, mesmo com a mesma semente de inicializacao, nao se deve assumir reproducibilidade bit a bit da trajetoria em diferentes GPUs, drivers ou execucoes.

## O que medir para otimizar

Uma avaliacao de desempenho confiavel deve medir separadamente:

1. tempo de CPU para atualizar janela, camera e uniforms;
2. tempo GPU por estagio: limpar/construir grade, simular, culling e desenho;
3. tempo do dispatch de culling;
4. tempo de rasterizacao das instancias visiveis;
5. contagem de discos visiveis e distribuicao de candidatos por bucket;
6. memoria GPU dedicada/compartilhada e alocacao durante reset.

Os tempos dos estagios GPU, contadores de contato, velocidade linear/angular maxima e quantidade de corpos visiveis ja sao coletados pelo rastreador. Consulte o [guia de diagnosticos](./diagnostics.md) para os campos disponiveis e suas limitacoes; a distribuicao espacial completa e o estado individual de cada esfera nao sao gravados.

## Protocolo de validacao de uma alteracao

Use esta sequencia para limitar o escopo quando uma mudanca afetar fisica, shader ou desenho:

1. Identifique quais buffers, bindings e shaders a mudanca toca.
2. Confira se os tamanhos e offsets permanecem iguais nos lados C# e GLSL.
3. Compile a solucao para detectar erros C# e de recursos incorporados.
4. Execute a aplicacao em um dispositivo com suporte OpenGL 4.3.
5. Observe o log completo de compilacao e link dos shaders na inicializacao.
6. Confirme que a janela abre e que a rampa e a populacao aparecem.
7. Deixe a simulacao avancar por alguns segundos e procure NaN visual, discos explodindo ou congelamento.
8. Pause e retome para confirmar que o estado se mantem estavel.
9. Reinicialize e confirme que a quantidade configurada continua igual e que novos materiais sao aceitos.
10. Redimensione a janela e confira se a projecao e o culling continuam coerentes.
11. Feche normalmente e confirme que o descarte dos recursos nao gera erros.

O build verifica o codigo .NET e os nomes dos recursos, mas nao substitui a etapa de execucao: os shaders GLSL sao compilados pelo driver durante a inicializacao do contexto.

## Verificacoes por tipo de mudanca

### Parametros fisicos

- Compare o valor C# com todos os marcadores GLSL que o consomem.
- Confira as unidades: metros, segundos, quilogramas e coeficientes adimensionais.
- Verifique que o raio maximo e compativel com a largura da celula da grade.
- Verifique que o maior impulso nao causa overflow ou perda relevante de precisao.
- Teste um estado inicial com poucas interacoes e outro com agrupamento denso.
- Reconfira as faixas da tabela de parametros no README.

### Busca espacial

- Confirme que limpar a tabela precede a insercao de todos os discos.
- Confira a barreira entre limpeza, construcao e leitura da grade.
- Calcule a vizinhanca necessaria para o raio maximo e o tamanho das celulas.
- Lembre-se de que um bucket pode receber itens de celulas diferentes.
- Se alterar o hash, valide colisao de hash separadamente de contato geometrico.
- Evite assumir que uma lista de candidatos visitada uma vez representa um par unico.

### Renderizacao indireta

- Confirme o layout do comando com a assinatura da chamada OpenGL usada.
- Verifique se o contador de instancias e reinicializado antes de cada culling.
- Confirme uma barreira de comando entre a escrita pelo compute shader e o desenho.
- Verifique os bindings do shader grafico depois da alternancia dos buffers de fisica.
- Teste com nenhum disco visivel, alguns visiveis e muitos visiveis.
- Garanta que o indice gravado pelo culling referencia o mesmo estado de posicao usado pelo desenho.

## Leitura de sintomas

| Sintoma | Primeiras verificacoes |
|---|---|
| Erro de shader na inicializacao | Nome do recurso, marcador nao substituido, simbolo GLSL nao declarado e versao do contexto |
| Rampa visivel, discos ausentes | Buffer de indices, contador indireto, bindings do vertex shader e teste de frustum |
| Discos aparecem, mas nao se movem | Loop fixo, acumulador, dispatch de simulacao e alternancia ping-pong |
| Discos atravessam a rampa | Convencao da normal, avaliacao da altura e consistencia entre collider e malha |
| Contatos instaveis em grupos densos | Raio maximo, delta temporal, numero de candidatos e duplicatas do hash |
| Desempenho cai em uma area | Atomics concentrados, densidade local, culling e custo de instancias |
| Reinicializacao falha | Capacidade dos buffers, tamanho dos arrays de upload e estado do programa compute |

Esses sintomas orientam a investigacao, mas nao identificam automaticamente a causa. Registre a mensagem completa do driver e altere uma variavel por vez ao reproduzir o problema.

## Registro de uma medicao manual

Ao comparar duas versoes, anote pelo menos:

- modelo da GPU e versao do driver;
- sistema operacional e versao do runtime .NET;
- resolucao da janela;
- quantidade de discos e configuracao de fisica;
- estado da simulacao (inicializacao, movimento, pausa ou reset);
- duracao observada e metodo usado para medir;
- se a medicao inclui compilacao inicial dos shaders;
- variacao entre repeticoes e quaisquer mensagens do driver.

Compare execucoes no mesmo dispositivo e na mesma resolucao. A primeira inicializacao pode incluir compilacao e alocacao que nao se repetem da mesma forma em quadros posteriores. Sem timer GPU embutido, tempos estimados pelo usuario devem ser marcados como observacao manual, nao como benchmark preciso.
