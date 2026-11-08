# Baterias Futuristas

Mod para Oxygen Not Included com baterias inspiradas em tecnologias reais. A primeira é a **bateria gravitacional**, que guarda energia erguendo um peso.

Testado na build U59-744825. Está em desenvolvimento.

## Bateria gravitacional

O jogador monta a torre em três peças, na aba Energia, depois de pesquisar Regulação Avançada de Energia:

1. **Base:** fica no chão e tem a conexão elétrica no tile do meio, a automação e o filtro de material.
2. **Trilhos:** empilhados sobre a base, sem espaços.
3. **Motor:** fecha o topo da torre.

A capacidade é massa × altura, com cerca de 8,3 J por kg por tile.

| Metal da base | Peso máximo | Trilhos máximos |
| --- | --- | --- |
| Aço | 16 t | 100 |
| Ferro | 8 t | 50 |
| Outros | 4 t | 20 |

A bateria não perde carga parada. Perde cerca de 8% no ciclo de carga e descarga, e essa perda vira calor no motor. O motor carrega a até 480 W.

## Compilar

É preciso ter o jogo instalado e o .NET SDK. Ajuste `GameManaged` em `BateriasFuturistas.csproj` para a pasta do seu jogo e rode:

```
dotnet build -c Release
```

O build instala o mod em `Documents\Klei\OxygenNotIncluded\mods\Local\BateriasFuturistas`. Depois, ative **Baterias Futuristas** no menu de mods.

## Arte

As artes em `art/` foram geradas com IA. Para refazer as animações a partir delas, é preciso Python com Pillow:

```
python tools/make_structure_kanims.py .
python tools/make_gravity_kanim.py .
```
