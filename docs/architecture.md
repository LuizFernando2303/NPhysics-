# Arquitetura e ciclo de vida

Este documento descreve a divisao de responsabilidades, a direcao das dependencias e o ciclo de vida da aplicacao NPhysics.

## Visao geral

O projeto separa a janela e o loop da aplicacao, a simulacao numerica, o mundo procedural, a renderizacao e os programas GLSL. As classes de alto nivel coordenam os subsistemas; os shaders executam trabalho massivamente paralelo.

```text
Program
  ├── --train / --validate-dataset (comandos sem janela de simulacao)
  ├── --gpu-model-check (janela OpenGL temporaria)
  └── PhysicsSimulation (GameWindow)
      ├── ProceduralTrack
      ├── GpuPhysics
      │   ├── SimulationSettings
      │   ├── GlProgram / ShaderSource
      │   ├── GpuDiagnostics
      │   └── DatasetGenerator (opt-in)
      │       ├── CaptureTraining.comp.glsl
      │       ├── AsyncGpuReadback
      │       └── DatasetWriter (.nphysdata)
      ├── SimulationTrace
      └── SceneRenderer
```

As ferramentas de aprendizado ainda sao independentes do backend fisico ativo:

```text
DatasetReader -> DatasetLoader -> GraphNetwork / GraphTrainer -> ModelSerializer (.nphysmodel)
                                                        └── GpuModelWeights -> LinearProbe.comp.glsl
```

`PhysicsSimulation` possui os componentes durante a vida da janela. A janela cria e descarta os recursos OpenGL; por isso, os objetos que chamam `GL.*` devem ser inicializados e descartados com um contexto OpenGL valido na thread da janela.

## Responsabilidades por componente

### `Program`

`Program.Main` trata os comandos de validacao do dataset, treinamento experimental e probe GPU. Sem esses comandos, cria `PhysicsSimulation` e inicia a simulacao Teacher interativa. A leitura dos argumentos e simples e especifica para as opcoes documentadas; nao e um parser geral.

### `PhysicsSimulation`

Deriva de `GameWindow` do OpenTK e e responsavel pelo ciclo de vida interativo:

- cria uma instancia de `ProceduralTrack` e `GpuPhysics`;
- inicializa a janela e o viewport;
- inicializa buffers e shaders na callback `OnLoad`;
- processa entrada de teclado em `OnUpdateFrame`;
- acumula delta de tempo e agenda passos fixos de fisica;
- atualiza o progresso da camera e a janela de obstaculos;
- cria view e projection em `OnRenderFrame`;
- libera os componentes em `OnUnload`.

O acumulador usa `FixedStep`; ele separa o tempo da simulacao do tempo irregular entre quadros. O delta de quadro e limitado a 0,05 s, mas o `while` do acumulador nao limita a quantidade de passos executados para recuperar atraso.

### `SimulationSettings`

Centraliza a configuracao de quantidade de esferas, passo, gravidade, inclinacao, faixas aleatorias de materiais, dimensoes, inercia, arrasto do ar, largura de rampa, espacamento inicial e parametros da grade de vizinhos.

Os valores que tambem sao usados por GLSL sao convertidos para texto invariavel por cultura em `ShaderSource`. Assim, o separador decimal permanece `.` mesmo em um sistema configurado para outra cultura.

### `GpuPhysics`

Possui os handles dos buffers, programas compute, uniform locations e o array de obstaculos da janela atual. A classe:

- cria os buffers;
- inicializa e reenvia o estado dos discos no reset;
- atualiza o buffer de obstaculos quando muda a janela de segmentos;
- executa clear, build-grid e simulate na ordem correta;
- produz a lista GPU de discos visiveis;
- expoe handles necessarios ao renderizador;
- libera recursos GPU;
- cria `DatasetGenerator` somente quando a captura e ativada no startup ou por `F5`.

Com a captura desativada, o passo Teacher nao faz dispatch de captura nem readback de estados.

O dispatch de captura ocorre depois de `Simulate` e antes da alternancia de `_currentState`. Ele recebe os dois estados, materiais, grade e links. O readback usa um anel de buffers e fences sem espera de CPU; slots ainda ocupados sao pulados.

### `Dataset` e `Learning`

`DatasetGenerator` executa `CaptureTraining.comp.glsl` e grava blocos binarios `.nphysdata`. `DatasetReader` verifica a assinatura, os tamanhos/contagens dos blocos e valores finitos, entregando blocos incrementalmente; a validacao atual ainda nao rejeita todas as versoes de schema no cabecalho. `Learning` contem o forward experimental da GNN, o treinador em CPU e o serializador do modelo. Esse caminho nao e chamado pelo solver Teacher.

`GpuModelWeights` valida o `.nphysmodel` v2 e empacota as camadas para um SSBO. `GpuModelProbeWindow` carrega o modelo em contexto OpenGL temporario e verifica uma camada linear. Ainda nao existe inferencia GNN completa na GPU, nem selecao Teacher/Neural no runtime.

O estado corrente e identificado por `_currentState`, que seleciona os buffers de posicao, velocidade linear e estado angular. O estado angular contem quaternion e velocidade angular em dois `vec4` por esfera. A cada passo os tres conjuntos alternam entre os buffers de entrada e saida.

### `ProceduralTrack` e `Obstacle`

`ProceduralTrack.Height(x, z)` e a definicao CPU da superficie usada para semear discos, posicionar obstaculos e gerar vertices. A mesma funcao matematica e espelhada no shader de simulacao para manter o contato alinhado com o desenho.

`CreateObstacles(cameraX)` escolhe um intervalo de segmentos em torno da camera. Os valores de cada segmento sao derivados de hashes, produzindo a mesma geometria para o mesmo numero de segmento. `Obstacle` e um record struct imutavel que transporta a descricao CPU usada pelo renderer e pela serializacao para SSBO.

### `SceneRenderer`

Cria o programa grafico, VAOs e VBOs dos billboards esfericos, blocos e terreno. A cada quadro:

1. solicita ao compute shader a lista de discos no frustum;
2. define a matriz view-projection e os uniforms graficos;
3. atualiza vertices e desenha a faixa de terreno;
4. desenha os obstaculos da janela ativa;
5. consome o comando de desenho indireto para as esferas visiveis e usa seus quaternions para orientar as marcas visuais.

O renderizador nao copia as posicoes dos discos para CPU.

### `GpuDiagnostics` e `SimulationTrace`

`GpuDiagnostics` possui o SSBO de contadores, os buffers de readback e as queries de tempo. O compute shader atualiza contadores agregados; em intervalos de aproximadamente um segundo, o codigo copia o buffer para um slot livre e associa uma fence. A thread da janela so le os valores depois que a fence sinaliza sem espera; se todos os slots estiverem ocupados, pula a captura daquele intervalo em vez de bloquear a GPU.

`SimulationTrace` escreve cabecalho, eventos de ativacao e amostras em JSONL dentro de `%LOCALAPPDATA%/NPhysics/traces`. A serializacao e feita pela thread da janela. `F3` desativa ou reativa a instrumentacao. O caminho completo do arquivo e informado no console.

### `GlProgram` e `ShaderSource`

`ShaderSource` procura recursos com nomes `NPhysics.Shaders.<nome>.glsl`, le o texto e substitui marcadores de configuracao. `GlProgram` compila shaders de compute ou shaders graficos, verifica status e lanca uma excecao com log caso compilacao ou link falhem.

Os fontes ficam em `Shaders/` e seus subdiretorios `Dataset/` e `Neural/`; sao incluidos no assembly pelas declaracoes `EmbeddedResource` de `NPhysics.csproj`. Ao renomear um recurso, atualizar tanto o arquivo quanto o nome passado a `ShaderSource.Load`.

## Fluxo de inicializacao

1. Sem argumento de ferramenta, `Program.Main` cria a janela e transfere controle ao `GameWindow.Run`.
2. OpenTK cria um contexto OpenGL 4.3 Core e chama `OnLoad`.
3. A janela configura clear color, depth testing, back-face culling e viewport.
4. `GpuPhysics.Initialize` compila os programas compute, cria SSBOs e contadores de diagnostico, localiza uniforms, inicializa arrays e envia posicoes, velocidades e materiais. A captura cria seus recursos somente quando ativada.
5. `GpuPhysics.Initialize` cria tambem a lista inicial de obstaculos e seus dados GPU.
6. `SceneRenderer.Initialize` compila o programa de superficie e cria VAOs/VBOs/EBOs.
7. O loop de eventos chama updates e renders ate a janela fechar.
8. Cada janela de diagnostico pronta e gravada pelo `SimulationTrace`.
9. `OnUnload` descarta renderizador, fisica, queries, fences, buffers de readback e arquivo de trace enquanto o contexto ainda esta valido.

## Fluxo por quadro

### Update

`OnUpdateFrame` ignora input/update quando a janela nao esta focada. Espaco alterna pausa. `R` reinicializa a simulacao. Se estiver em movimento:

1. limita o delta do quadro a 1/20 s;
2. soma o delta ao acumulador;
3. executa `GpuPhysics.Step(1/120)` para cada passo completo;
4. move a camera;
5. atualiza os obstaculos se a camera cruzou um limite de segmento.

A atualizacao do shader de obstaculos e limitada a mudancas de faixa; nao reenvia a lista a cada quadro.

### Render

`OnRenderFrame` limpa color e depth. A camera olha para um ponto adiante, calcula `LookAt`, cria projecao perspectiva e passa a matriz view-projection ao `SceneRenderer`. O renderer culla discos, desenha o mundo e chama `SwapBuffers`.

## Ownership e regras de manutencao

- Um handle OpenGL deve ser criado e destruido sob um contexto valido.
- Nao chamar `Dispose` de buffers/programas em background thread sem transferir contexto.
- Qualquer novo SSBO precisa ter um binding consistente no C#, compute shader e vertex shader que o utiliza.
- O layout binario de uma estrutura compartilhada deve ser explicitamente compativel com `std430`; `vec4` e usado para evitar alinhamentos ambiguos de atributos.
- Toda alteracao em parametros GLSL deve manter `ShaderSource` e `SimulationSettings` sincronizados.
- Falhas de shader devem continuar visiveis com o log do driver; nao substituir excecoes por valores de sucesso.
- Cada recurso OpenGL criado precisa de um caminho de descarte em `OnUnload`/`Dispose`.

## Mapa de chamadas principal

```text
Program.Main
  -> PhysicsSimulation.Run
     -> OnLoad
        -> GpuPhysics.Initialize
           -> ShaderSource.Load
           -> GlProgram.CreateCompute
           -> Reset
           -> UpdateObstacles
        -> SceneRenderer.Initialize
           -> ShaderSource.Load
           -> GlProgram.CreateGraphics
           -> CreateDiskMesh / CreateCubeMesh / CreateTerrainMesh
     -> OnUpdateFrame
        -> GpuPhysics.Step
           -> ClearGrid
           -> BuildGrid
           -> Simulate
        -> GpuPhysics.UpdateObstacles
     -> OnRenderFrame
        -> SceneRenderer.Render
           -> GpuPhysics.CullVisibleDisks
           -> DrawTerrain
           -> DrawObstacles
           -> DrawBalls
     -> OnUnload
        -> SceneRenderer.Dispose
        -> GpuPhysics.Dispose
```
