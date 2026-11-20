# Pipeline GPU e memoria

Este documento detalha os buffers OpenGL, o pipeline por passo de simulacao e os pontos de sincronizacao usados pelo projeto.

## Requisitos do pipeline

O contexto requisitado e OpenGL 4.3 Core porque o projeto usa:

- compute shaders;
- SSBOs (`std430`);
- operacoes atomicas em buffers;
- barreiras de memoria entre dispatches;
- desenho indireto;
- desenho de triangulos instanciado por identificador de instancia.

Uma compilacao C# bem-sucedida nao compila GLSL. A validacao final de shader acontece quando o programa cria um contexto OpenGL e chama `glCompileShader`.

## Estado por esfera

Cada atributo linear usa `vec4` por esfera, totalizando 16 bytes por item:

| Atributo | Componentes | Interpretacao |
|---|---|---|
| Posicao | `x,y,z,w` | `w` e o raio |
| Velocidade | `x,y,z,w` | `w` reservado e inicializado como zero |
| Material | `x,y,z,w` | massa, atrito estatico, atrito dinamico, restituicao |
| Orientacao | `x,y,z,w` | quaternion, com `w` escalar |
| Velocidade angular | `x,y,z,w` | `wx,wy,wz`; `w` reservado |

Orientacao e velocidade angular sao empacotadas em dois `vec4` consecutivos por esfera. O uso de `vec4` simplifica os alinhamentos de `std430`, evita arrays de `vec3` com stride ambiguo e permite acesso direto por indice da esfera.

## SSBOs e bindings

Os numeros de binding sao uma interface entre o codigo C# e cada programa shader. Eles podem ser diferentes entre programas; se um binding mudar em GLSL, o `GL.BindBufferBase` correspondente daquele dispatch/desenho tambem deve mudar. A tabela abaixo descreve os bindings do shader de simulacao, exceto quando explicitado.

| Binding | Conteudo | Consumidores |
|---:|---|---|
| 0 | Posicoes correntes de entrada | construir grade, simular, culling, vertex shader |
| 1 | Velocidades correntes de entrada | simular |
| 2 | Posicoes de saida | simular |
| 3 | Velocidades de saida | simular |
| 4 | Cabecalhos de buckets da grade | limpar, construir grade, simular |
| 5 | Lista ligada de indices por disco | construir grade, simular |
| 6 | Dois `vec4` por obstaculo | simular |
| 7 | Material por disco | simular, vertex shader |
| 8 | Contadores de metricas | simular (binding 6 no build da grade; binding 2 no culling) |
| 9 | Estado angular de entrada (compute) ou orientacao corrente (render) | simular, vertex shader |
| 10 | Estado angular de saida | simular |

Para renderizar, o buffer de indices visiveis e associado ao binding 1 do programa grafico. Ele compartilha uma alocacao com o comando indireto:

```text
byte 0..15: comando `DrawArraysIndirect` (quatro inteiros de 32 bits)
byte 16..19: word reservado para alinhar o array SSBO
byte 20.. : um uint por indice de disco visivel
```

O comando `DrawArraysIndirect` tem 16 bytes: contagem de vertices, contagem de instancias, primeiro vertice e base instance. O shader SSBO acrescenta um word reservado de 4 bytes, de modo que o array flexivel de indices comece no offset 20. A base instance e zero; os cinco words C# usados para inicializar a area correspondem ao comando de 16 bytes mais esse padding.

## Tamanhos

Para `N = 1.000.000`, um `vec4` por corpo ocupa `N * 16` bytes = 16.000.000 bytes (aproximadamente 15,26 MiB).

| Buffer | Formula | Tamanho nominal |
|---|---:|---:|
| Posicoes ping | `N * 16` | 16 MB |
| Posicoes pong | `N * 16` | 16 MB |
| Velocidades ping | `N * 16` | 16 MB |
| Velocidades pong | `N * 16` | 16 MB |
| Spin ping | `N * 32` | 32 MB |
| Spin pong | `N * 32` | 32 MB |
| Materiais | `N * 16` | 16 MB |
| Links de vizinhos | `N * 4` | 4 MB |
| Cabecalhos hash | `4.194.304 * 4` | 16 MiB |
| Indices de visibilidade | `N * 4` | 4 MB |
| Comando indireto | `5 * 4` | 20 bytes |

O total desses SSBOs e aproximadamente 169 MB decimais (cerca de 161 MiB), sem contar contadores de metricas, obstaculos, VBOs, EBOs, framebuffer, runtime OpenGL ou alocacoes internas do driver. Posicoes, velocidades lineares e spin ping-pong somam 128 MB; materiais ocupam mais 16 MB.

Durante reset, quatro arrays CPU de floats sao criados: posicoes, velocidades lineares e materiais ocupam aproximadamente 16 MB cada; spin ocupa 32 MB por armazenar dois `vec4` por esfera. O total temporario e cerca de 80 MB enquanto os dados sao enviados, alem do overhead do runtime.

## Pipeline de um passo

### 1. Limpar a tabela hash

`ClearGrid.comp.glsl` executa 256 threads por workgroup. Cada thread escreve `-1` em um cabecalho. Para 4.194.304 buckets:

```text
workgroups = ceil(4.194.304 / 256) = 16.384
```

Ao terminar, o C# chama uma barreira `ShaderStorageBarrierBit` antes do proximo dispatch.

### 2. Construir a grade

`BuildGrid.comp.glsl` executa 128 threads por workgroup. Cada disco:

1. divide a posicao pela largura de celula;
2. converte para coordenada inteira;
3. aplica uma funcao hash espacial;
4. troca atomicamente o cabecalho do bucket pelo proprio indice;
5. guarda em `links[id]` o indice anterior do bucket.

O numero de buckets e potencia de dois para que o shader possa usar `hash & mask`.

### 3. Simular

`Simulate.comp.glsl` tambem usa 128 threads por workgroup. Cada invocacao processa uma esfera e escreve posicao, velocidade linear e estado angular apenas no buffer de saida daquele indice. A leitura vem dos buffers de entrada, da grade, links, obstaculos, materiais e estado angular dos vizinhos.

O shader procura os buckets da celula atual e seus vizinhos imediatos. Como o hash pode mapear celulas espaciais diferentes ao mesmo bucket, uma lista pode conter candidatos de outras celulas. O teste geometrico rejeita a maioria dos candidatos, mas nao existe teste da coordenada espacial original do item.

### 4. Alternar estado

Depois da barreira de armazenamento, `_currentState` passa a apontar para o buffer que acabou de ser escrito. O antigo buffer de entrada fica disponivel como destino do proximo passo.

## Pipeline de culling e desenho

1. O C# inicializa `indexCount` e zera `instanceCount` do comando indireto.
2. `CullDisks.comp.glsl` testa cada centro contra o frustum transformado.
3. Para um disco visivel, `atomicAdd` reserva um slot e grava o indice de disco.
4. A barreira combina `ShaderStorageBarrierBit` e `CommandBarrierBit`.
5. `SceneRenderer` associa posicoes, indice visivel, material e orientacao aos bindings do vertex shader.
6. `glDrawArraysIndirect` consome a contagem criada na GPU.
7. O vertex shader busca indice e monta um quad billboard voltado para a camera; o fragment shader reconstrui a profundidade esferica por pixel e desenha cor chapada com contorno e brilho estilizados.

Cada esfera visivel usa seis vertices de quad em vez de uma malha com multiplos segmentos. A posicao do centro e o raio continuam vindo dos SSBOs; o billboard e uma aproximacao visual para economizar trabalho de vertices, nao uma mudanca de estado fisico. A orientacao continua integrada pela fisica, mas nao e consultada pelo renderizador, pois a esfera cartoon nao exibe marcas de giro.

Essa etapa economiza rasterizacao de discos fora da camera. Ela ainda percorre todos os centros em compute e nao reduz os passos da simulacao fisica.

## Geracao e incorporacao dos shaders

`NPhysics.csproj` inclui `Shaders\*.glsl` como recurso embutido. Os nomes de recurso seguem:

```text
NPhysics.Shaders.ClearGrid.comp.glsl
NPhysics.Shaders.BuildGrid.comp.glsl
NPhysics.Shaders.Simulate.comp.glsl
NPhysics.Shaders.CullDisks.comp.glsl
NPhysics.Shaders.Surface.vert.glsl
NPhysics.Shaders.Surface.frag.glsl
```

O argumento de `ShaderSource.Load` omite o sufixo `.glsl`. O carregador substitui marcadores como `@GRAVITY@` por constantes formatadas com cultura invariavel. Um marcador esquecido resulta em erro de compilacao GLSL; por isso, revisar o texto final resolvido e o log do driver quando um shader falhar.

## Barreiras e hazards

As barreiras sao parte da corretude, nao apenas otimizacao:

- Limpeza da grade -> build grid: shader-storage barrier.
- Build grid -> simulate: shader-storage barrier.
- Simulate -> proximo consumidor da posicao: shader-storage barrier.
- Culling -> desenho indireto/lista visivel: shader-storage e command barrier.

Se um compute shader for adicionado, identificar qual comando seguinte consome suas escritas e escolher a barreira OpenGL que cobre esse consumidor.

## Pressao de desempenho

Em cada passo de simulacao, o projeto:

- escreve todos os 4.194.304 buckets;
- insere um milhao de discos;
- executa um milhao de invocacoes de simulacao;
- percorre ate 27 listas hash por disco;
- consulta obstaculos ativos por disco.

Em cada quadro de renderizacao, o culling faz mais um milhao de testes. O principal custo depende da distribuicao espacial e de quantos discos estao nos mesmos buckets, nao apenas do numero de discos visiveis.

## Diagnostico GPU

Quando houver um erro:

1. Capturar o tipo de shader e o log apresentado por `GlProgram`.
2. Confirmar que o arquivo GLSL aparece em `Assembly.GetExecutingAssembly().GetManifestResourceNames()`.
3. Confirmar substituicao de todos os marcadores `@...@`.
4. Comparar `layout(binding = N)` no shader com `GL.BindBufferBase(..., N, ...)`.
5. Conferir tamanhos de buffers em bytes e o stride de cada item.
6. Verificar se a barreira existe entre o dispatch que escreve e o consumidor.
7. Confirmar limites de workgroup suportados pelo dispositivo.
8. Reproduzir com uma quantidade pequena de discos em branch/teste temporario antes de escalar.

O passo 8 exige parametrizar a quantidade; atualmente a configuracao e uma constante fixa de um milhao e nao existe argumento CLI para reduzi-la.
