# Modelo fisico e contatos

Este documento descreve o modelo implementado no compute shader `Shaders/Simulate.comp.glsl`. Cada corpo e uma esfera rigida com estado rotacional e resposta a impulso; o solver e paralelo e aproximado, nao um motor geral de rigid bodies.

## Unidades e eixos

O mundo usa coordenadas cartesianas:

- `X`: direcao longitudinal da rampa e movimento da camera;
- `Y`: altura; gravidade aponta para `-Y`;
- `Z`: largura da rampa.

Os valores sao tratados como unidades semelhantes a metros, segundos e quilogramas. A aplicacao nao impõe uma conversao de unidade em runtime; a interpretacao SI depende de usar os coeficientes e dimensoes configurados como SI.

O estado por esfera e:

```text
posicao = (x, y, z)
velocidade = (vx, vy, vz)
raio = r
orientacao = quaternion (x, y, z, w)
velocidade angular = (wx, wy, wz)
material = (massa, mu_estatico, mu_dinamico, restituicao)
```

As orientacoes iniciais sao a identidade e as velocidades angulares iniciais sao zero. Os centros de spawn sao elevados pela normal local da rampa, nao pelo eixo vertical global, para comecarem a uma distancia de raio da superficie. O estado rotacional e armazenado em dois `vec4` por esfera; o quarto componente da velocidade angular e reservado.

## Esferas rigidas e inercia

O corpo e uma esfera de raio `r`, massa `m` e inercia de esfera solida uniforme:

```text
I = 2/5 * m * r²
```

Como a esfera e isotropica, a orientacao nao altera a geometria nem a inercia usada nas colisoes. A orientacao integrada e usada para renderizar as marcas visuais da esfera.

## Rampa

A superficie nominal e uma funcao altura:

```text
y = -s*x
    + 0,48*sin(0,055*x)
    + 0,16*sin(0,19*x + 1,4)
    + 0,065*sin(0,47*x)
    + 0,14*sin(0,55*z + 0,035*x)
    + 0,045*sin(1,25*z - 0,11*x)
    + 0,006*z²
s = tan(45°)
```

O sinal negativo faz a altura media diminuir quando X aumenta. Ondulacoes longitudinais e transversais com amplitude sub-metrica tornam cristas e vales mais legiveis sem substituir a inclinacao principal. `ProceduralTrack.Height` e `groundHeight` no compute shader usam a mesma funcao; a malha visivel tambem amostra essa funcao. Alterar a forma em apenas um ponto desalinharia o desenho e o contato. O shader calcula numericamente as derivadas locais:

```text
dh/dx ~= (h(x + e,z) - h(x - e,z)) / (2e)
dh/dz ~= (h(x,z + e) - h(x,z - e)) / (2e)
n = normalize((-dh/dx, 1, -dh/dz))
```

`e` vale 0,01 na funcao de contato. A normal `n` e usada para calcular a separacao entre o centro da esfera e a superficie e resolver penetracao. O contato de esfera usa distancia centro-superficie menos o raio.

### Distancia de contato

O shader calcula um ponto de superficie `(x, h(x,z), z)` e projeta a diferenca para a normal:

```text
separacao = dot(posicao - pontoSuperficie, n) - raio
```

Uma separacao ligeiramente positiva de ate 0,002 continua sendo tratada como contato, reduzindo jitter causado por pequenas variacoes numericas. Se houver penetracao, o centro e movido na direcao da normal.

### Cores por energia potencial

A cor do billboard e calculada apenas para renderizacao, usando a massa da esfera e sua altura atual:

```text
Uvisual = m * g * max(y - yReferencia, 0)
cor = paleta( clamp(Uvisual / Umax, 0, 1) )
```

`yReferencia` e a altura da linha central no ultimo ponto da distribuicao inicial; `Umax` e estimado com a altura inicial mais alta, a massa maxima e uma margem. Baixa energia aparece vermelha/laranja, energia intermediaria amarela e alta energia ciano/azul. E uma energia potencial relativa a um zero escolhido para visualizacao; nao e armazenada no estado nem participa de forcas, integracao ou colisoes.

## Integracao temporal e arrasto

O passo nominal e:

```text
dt = 1/120 s ~= 0,008333333 s
```

O integrador semi-implicito atualiza primeiro a velocidade e depois a posicao:

```text
v[n+1] = v[n] + a[n] * dt
x[n+1] = x[n] + v[n+1] * dt
```

O shader aplica gravidade e arrasto aerodinamico quadratico:

```text
F_drag = -1/2 * rho * Cd * A * |v| * v
a_drag = F_drag / m
```

`rho`, `Cd` e o fator de inercia da esfera sao configurados em `SimulationSettings` e injetados em GLSL por `ShaderSource`. A area projetada usada e `pi*r²`; efeitos de turbulencia, sustentacao e variacao da area com a orientacao nao sao modelados. `PhysicsSimulation` acumula tempo real para executar passos fixos, limita o delta recebido por update a 0,05 s e pode executar varios passos para recuperar atraso.

## Contato e atrito rotacional

O material por esfera contem coeficientes independentes:

```text
mu_s = coeficiente de atrito estatico
mu_d = coeficiente de atrito dinamico
```

Nos contatos, o shader calcula a velocidade no ponto de contato:

```text
v_contato = v + omega x r_contato
```

A resposta normal usa impulso com massa inversa e inercia inversa. O atrito tangencial limita o impulso requerido pelos coeficientes estatico e dinamico e aplica o impulso no ponto de contato; o produto vetorial entre o braco de contato e o impulso altera a velocidade angular, gerando torque e rolamento.

Para duas esferas, o denominador do impulso normal inclui as contribuicoes lineares e angulares:

```text
K_n = invMass_A + invMass_B
    + I_inv_A * |r_A x n|²
    + I_inv_B * |r_B x n|²
j_n = -(1 + e) * v_rel,n / K_n
```

O impulso so e aplicado quando os corpos se aproximam. A restituicao entre esferas e o menor valor dos dois materiais. Para contato estatico, o objeto oposto e tratado como imovel, e o impulso tangencial pode gerar torque na esfera.

O shader corrige tambem parte da penetracao com `slop = 0,001` e fator `0,8`; a correcao gradual reduz sobrecorrecao, mas pode deixar penetracao residual. Na rampa, a restituicao e desativada para velocidades normais de aproximacao inferiores a `0,35`.

## Colisao esfera-esfera

Para cada esfera:

1. Usa sua posicao integrada para encontrar a celula espacial.
2. Consulta as 27 celulas (vizinhos `-1..1` nos eixos X, Y, Z).
3. Percorre as listas de indices dos buckets da grade hash.
4. Ignora o proprio indice.
5. Testa a distancia entre centros contra a soma dos raios.
6. Calcula normal, penetracao e velocidade relativa nos pontos de contato.
7. Aplica correcao de posicao e, se os corpos estiverem se aproximando, impulsos normal e tangencial com resposta angular.

A consulta usa a grade do estado de entrada; a posicao da propria esfera ja foi integrada localmente. Assim, os vizinhos ainda representam as posicoes do passo anterior. Isso e uma solucao paralela aproximada, e nao um solver sequencial de pares com estado mutavel compartilhado.

## Obstaculos

O campo `IsBox` define duas interpretacoes de `Obstacle`:

- Caixa: collider axis-aligned de meia extensao `(hx,hy,hz)`.
- Cilindro: teste de distancia ao volume cilindrico, com raio e meia altura informados no buffer de obstaculos.

A resposta altera a posicao para remover sobreposicao e aplica impulso normal e atrito usando o material da esfera. Os obstaculos sao estaticos; nao recebem impulso nem armazenam estado dinamico.

## Bordas e esferas que deixam a pista

As bordas da rampa limitam Z a `[-9 + r, 9 - r]`. A velocidade que aponta para fora e invertida e multiplicada pela restituicao da esfera.

Nao existe respawn nem emissao continua. As esferas ficam no mundo depois de ultrapassar a janela da camera ou cair da rampa; por isso, podem deixar de ser visiveis. `R` reconstrui o estado inicial completo.

## Limites de precisao e interpretacao

- As esferas sao isotropicas: orientacao nao altera a geometria de colisao. As marcas visuais que acompanham o quaternion nao representam textura ou deformacao fisica.
- Colisoes sao discretas; nao existe teste de colisao continua (CCD). Corpos muito rapidos podem atravessar superficies finas entre passos.
- Cada invocacao resolve contatos usando estado de vizinhos lido do passo anterior e so grava seu proprio corpo. Nao ha acumulacao de impulsos por pares, solver iterativo global, friccao de rolamento independente ou correcao sequencial de pilhas; em agrupamentos densos podem ocorrer penetracao, jitter e energia numerica.
- A busca hash usa uma unica lista ligada por bucket; colisoes do hash podem repetir candidatos.
- O resultado numerico pode depender da ordem atomica em que os discos entram em cada bucket.
- Os valores em metros, quilogramas e segundos sao a convencao adotada pelo projeto; nao existe calibracao contra dados reais.

## Validacao ao alterar o modelo

Para cada mudanca fisica:

1. Verificar os sinais da gravidade e da inclinacao.
2. Conferir se as normais apontam para fora da rampa/objeto.
3. Verificar que o impulso so ocorre para velocidade normal de aproximacao.
4. Conferir a resposta com massas iguais e com uma massa muito maior que a outra.
5. Testar os limites de atrito: abaixo do limite estatico e em deslizamento dinamico.
6. Confirmar que os parametros injetados em GLSL correspondem a `SimulationSettings`.
7. Confirmar compilacao de C# e compilacao real dos shaders em contexto OpenGL.

## Instrumentacao para melhoria iterativa

O rastreador grava uma sessao JSONL, por padrao, em `%LOCALAPPDATA%/NPhysics/traces`. Uma linha `session` identifica o schema, o dispositivo OpenGL e os parametros principais. Uma linha `sample` agrega aproximadamente um segundo de atividade; uma linha `capture` registra o uso de `F3`. O arquivo pode ser consumido por scripts de analise sem dependencias externas.

Os contadores sao acumulados na GPU e lidos por readback assíncrono em um anel de quatro buffers. Uma fence e consultada com timeout zero; dados indisponiveis nao bloqueiam a thread de renderizacao, e uma captura e adiada se todos os slots estiverem ocupados. Os campos observaveis sao:

| Campo | Interpretacao |
|---|---|
| `candidateChecks` | Candidatos de disco testados na lista hash; pode contar o mesmo par mais de uma vez |
| `diskContacts` | Sobreposicoes disco-disco resolvidas por invocacao de disco; contatos do par podem aparecer nos dois sentidos |
| `rampContacts` | Invocacoes que entraram na resolucao de contato com a rampa |
| `obstacleContacts` | Sobreposicoes resolvidas contra obstaculos |
| `gridBucketConflicts` | Insercoes em buckets hash ja ocupados; inclui compartilhamento legitimo e colisao de hash |
| `invalidStates` | Escritas com posicao, velocidade linear, velocidade angular ou orientacao NaN/infinita; e um alarme, nao uma correcao automatica |
| `maximumSpeed` | Maior modulo da velocidade observado na janela |
| `maximumAngularSpeedRadiansPerSecond` | Maior modulo da velocidade angular em rad/s observado na janela |
| `visibleDiskFrames` | Soma dos discos visiveis por dispatch de culling no intervalo; dividir por quadros renderizados para estimar a media por quadro |
| `lastGridClearGpuMilliseconds` | Duracao GPU da ultima limpeza da grade concluida |
| `lastGridBuildGpuMilliseconds` | Duracao GPU da ultima construcao da grade concluida |
| `lastSimulationGpuMilliseconds` | Duracao GPU da ultima simulacao de um passo concluida |
| `lastCullingGpuMilliseconds` | Duracao GPU do ultimo dispatch de culling concluido |
| `lastDrawingGpuMilliseconds` | Duracao GPU do ultimo lote de desenho concluido |
| `physicsStepCount` | Passos fisicos executados no intervalo; pode ser comparado com a taxa de quadros |
| `focused`, `paused` | Estado da janela no momento da captura |
| `averagePhysicsSubmissionMilliseconds` | Tempo CPU medio por quadro gasto submetendo os dispatches de fisica, dividido pelos quadros da janela |
| `averageFrameMilliseconds` | Media de `FrameEventArgs.Time` entre quadros renderizados |
| `averageRenderSubmissionMilliseconds` | Tempo CPU medio em render e `SwapBuffers` por quadro; pode incluir espera de VSync |

Os tempos GPU sao os ultimos resultados disponiveis, nao uma media exata do intervalo JSONL. As contagens de contato sao eventos por esfera, nao pares unicos. Contadores de 32 bits podem dar wrap em cargas extremas. O rastreador nao salva posicao/velocidade de cada corpo: fazer isso para 100.000 esferas por amostra criaria transferencias e arquivos grandes, alem de alterar o desempenho.

Instrumentar cada candidato e contato usa atomicos e acrescenta custo. Para comparar, registre uma execucao com coleta ligada, pressione `F3` para desligar e compare janela, cena e dispositivo equivalentes. O rastreamento desligado suprime atomicos e queries, mas a simulacao continua. Evite interpretar uma diferenca pequena como ganho real sem repetir execucoes.

### Como usar os dados

1. Reproduza a mesma configuracao e capture alguns segundos de estado inicial, movimento e areas densas.
2. Compare `candidateChecks` com `diskContacts`: muitos candidatos por contato apontam para custo na busca, tamanho da celula, hashing ou densidade.
3. Compare `gridBucketConflicts` entre configuracoes antes de alterar o numero de buckets.
4. Observe `maximumSpeed` e `invalidStates` ao ajustar passo, colisao ou restitucao.
5. Compare `visibleDiskFrames / frameCount` com tempo de render GPU para estimar o custo das instancias.
6. Compare submissao CPU e tempos GPU por estagio: CPU alto sugere custo de submissao; GPU alto indica o estagio compute/rasterizacao mais caro.
7. Mude uma variavel por vez, execute varias repeticoes e preserve os JSONL para comparar antes/depois.

O console mostra o caminho exato do arquivo. O titulo da janela resume o ultimo tempo de fisica, candidatos, contatos e invalidos. `F3` escreve evento no trace quando a coleta muda de estado.
