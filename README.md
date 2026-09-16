# MCHOSE V9 Pro Battery Tray

Aplicativo minúsculo de bandeja para Windows que mostra a bateria do headset sem fio
**MCHOSE V9 Pro**, lida direto do dongle USB 2.4 GHz — sem precisar do MCHOSE HUB.

O percentual é desenhado **dentro do ícone da bandeja**, então você lê de relance:

![Estados do ícone em 16, 24 e 32 pixels](docs/img/tray-icons.png)

*Renderizado em 16 px, 24 px e 32 px (de cima para baixo), ampliado 4x para mostrar a grade real de pixels.*

## Por quê

O MCHOSE HUB precisa ficar aberto só para informar a carga. Este aplicativo faz o mesmo
a partir de um único executável autocontido que mora na bandeja, não custa nada enquanto
está ocioso e envia exatamente um relatório HID somente-leitura a cada 30 segundos.

## Recursos

- **Legível de relance** — a carga é o próprio ícone, não uma dica que exige o mouse.
- **Sem janela, sem instalador** — um `.exe` autocontido e entrada opcional na inicialização.
- **Sobrevive a tudo** — dongle removido, headset desligado, timeouts e respostas malformadas
  são todos não-fatais; a bandeja nunca morre e se recupera sozinha.
- **Somente leitura por design** — envia um único comando conhecido de consulta e nada mais.
- **Sem dependências** — nenhum pacote NuGet de HID; P/Invoke direto em `setupapi.dll`,
  `hid.dll` e `kernel32.dll`.

## Requisitos

- Windows 10 ou 11 (x64; o código é neutro quanto à arquitetura e também publica `win-arm64`)
- Um MCHOSE V9 Pro com seu dongle 2.4 GHz — USB VID:PID `291D:385D`
- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar

## Instalação

Compile você mesmo (ainda não há release assinada):

```powershell
git clone https://github.com/rafagfran/mchose-v9-pro-battery-tray.git
cd mchose-v9-pro-battery-tray
dotnet publish src/MchoseBattery.Tray -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

O executável aparece em
`src/MchoseBattery.Tray/bin/Release/net8.0-windows/win-x64/publish/MchoseBattery.exe`.
Copie para onde quiser e execute.

**Para fixar na barra de tarefas:** o Windows esconde ícones novos no menu de estouro.
Para mantê-lo sempre visível vá em *Configurações → Personalização → Barra de tarefas →
Outros ícones da bandeja do sistema* e ligue **MchoseBattery**.

## Desenvolvimento

```powershell
dotnet restore
dotnet build
dotnet test tests/MchoseBattery.Core.Tests    # 56 testes, sem necessidade de hardware
dotnet run --project src/MchoseBattery.Tray
```

## Ícone da bandeja

| Carga | Cor | Texto no ícone |
|---|---|---|
| 0–20% | vermelho | `0`..`20` |
| 21–40% | âmbar | `21`..`40` |
| 41–100% | verde | `41`..`100` |
| Headset desligado / fora de alcance | cinza | `--` |
| Dongle não encontrado | cinza | `?` |

O ícone é redesenhado no tamanho que o Windows pedir (`SystemInformation.SmallIconSize`),
então acompanha a escala de DPI, e só é refeito quando o texto ou a cor realmente mudam.
O fundo é um quadrado arredondado preenchido, para o número manter contraste tanto na
barra clara quanto na escura.

## Menu da bandeja

| Item | Efeito |
|---|---|
| `MCHOSE V9 Pro: NN%` | Estado atual (informativo, desabilitado) |
| `Atualizar agora` | Força uma leitura imediata |
| `Abrir ao iniciar o Windows` | Grava/remove `MchoseBattery` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| `Sair` | Encerra |

## Como as leituras acontecem

**Não** é um contador ao vivo. As leituras ocorrem:

- uma vez na abertura,
- a cada **30 segundos** depois disso,
- imediatamente quando o Windows avisa que um dispositivo HID entrou ou saiu,
- imediatamente quando você clica em `Atualizar agora`.

Ou seja, o número pode estar até 30 segundos desatualizado. Para bateria de headset isso
é irrelevante — a carga não cai 1% em meio minuto — e ler com mais frequência só mantém
o rádio 2.4 GHz ocupado sem nenhum ganho.

## Diagnóstico

```powershell
MchoseBattery.exe --diagnose                    # ou --list-hid: lista todas as coleções HID; não envia nada
MchoseBattery.exe --inspect-device "<caminho>"  # capacidades de uma coleção; não envia nada
MchoseBattery.exe --probe-battery "<caminho>"   # único modo que escreve; imprime request/reply em hex
```

`--probe-battery` exige o caminho completo impresso por `--diagnose`, recusa qualquer
dispositivo que não seja `291D:385D` com reports de entrada e saída de 64 bytes, e recusa
caminho que não identifique exatamente uma coleção HID presente no momento.

Como o executável é `WinExe`, redirecione a saída se o seu console não anexar
automaticamente: `MchoseBattery.exe --diagnose | Out-String`.

## Protocolo

O perfil foi obtido por engenharia reversa para fins de interoperabilidade e
**confirmado em hardware real**:

```text
VID:PID      291D:385D
Frame        64 bytes (entrada e saída)
Request      55 65 01 00 ... 00
Assinatura   55 65
Bateria      byte 2 da resposta, percentual direto 0..100
Status       byte 3 da resposta, registrado mas NÃO interpretado
Timeout      500 ms
```

### Captura em hardware

```text
Path:         \\?\hid#vid_291d&pid_385d&mi_00&col05#...
VID:PID:      291D:385D; Version: 0x0012
Manufacturer: C-Media Electronics Inc
Product:      MCHOSE V9 PRO
Usage Page:   0xFF90; Usage: 0x0001   (definido pelo fabricante)
Input: 64; Output: 64; Feature: 0

Request:  55 65 01 00 ... 00
Reply:    55 65 14 02 00 ... 00
Battery:  20%; status: 0x02
```

A coleção que responde é a **`col05`** da interface `mi_00`, na Usage Page de fabricante
`0xFF90`. O byte 2 da resposta (`0x14` = 20) carrega o percentual diretamente.

O byte 3 (`0x02`) é deliberadamente deixado sem interpretação. Uma única amostra não
basta para afirmar que ele significa "carregando", "em uso" ou qualquer outra coisa —
o `--probe-battery` o imprime e o aplicativo o ignora.

## Limite de segurança

O aplicativo envia **apenas** o request de leitura acima, e só depois que um dispositivo
casa com o perfil. Nenhum comando de firmware, EQ, RGB, volume ou desconhecido é emitido,
e Feature Reports relacionados a firmware estão deliberadamente fora de escopo. O
`WindowsHidTransport` revalida o predicado de candidato e aceita apenas uma cópia byte a
byte do request canônico, então um chamador não consegue contrabandear outro payload.

O V9 comum (não Pro) **não** é assumido como compatível. Se ele não responder com
assinatura válida, você simplesmente vê `Headset desconectado`; o aplicativo não sai
tentando outros comandos.

## Estados

| Estado | Significado |
|---|---|
| `Dongle não encontrado` | Nenhuma coleção `291D:385D` com reports de 64 bytes |
| `Headset desconectado` | Dongle presente, sem resposta válida (headset desligado, fora de alcance ou timeout) |
| `MCHOSE V9 Pro: NN%` | Resposta validada pelo parser |

## Logs

`%LocalAppData%\MchoseBattery\mchose-battery.log`, limitado a 256 KiB (o trecho mais
antigo é descartado). Somente transições de estado e falhas são gravadas — uma leitura
bem-sucedida e sem mudança não gera linha nenhuma.

## Estrutura do projeto

```text
src/MchoseBattery.Core            enumeração HID nativa, protocolo, transporte, polling, log
src/MchoseBattery.Tray            executável WinForms sem janela, CLI de diagnóstico, inicialização
tests/MchoseBattery.Core.Tests    suíte MSTest, sem necessidade de hardware
```

## Contribuindo

Capturas de outros modelos MCHOSE são realmente úteis. Se você tem um, rode `--diagnose`,
depois `--probe-battery` numa coleção `291D:385D`, e abra uma issue com a saída —
principalmente se o byte 3 da resposta mudar durante o carregamento. Esse é o caminho
mais rápido para descobrir o que o byte de status significa de fato, e se outros modelos
compartilham o mesmo perfil.

## Licença e isenção de responsabilidade

Distribuído sob a licença MIT — veja [LICENSE](LICENSE).

Este é um projeto independente, **sem qualquer vínculo, patrocínio ou aprovação da MCHOSE
ou da C-Media Electronics**. "MCHOSE" e "V9 Pro" são marcas de seus respectivos donos e
aparecem aqui apenas para identificar o hardware compatível.

O protocolo foi obtido por engenharia reversa para fins de interoperabilidade e
confirmado em hardware próprio. O aplicativo emite um único comando de leitura e nenhum
comando de escrita, firmware ou configuração. Ainda assim, o software é fornecido "como
está", sem garantia: executá-lo contra o seu dispositivo é por sua conta e risco.
