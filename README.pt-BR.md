# MchoseBattery

Aplicativo de bandeja (tray) para Windows 10/11 que mostra a bateria do headset
**MCHOSE V9 Pro** lida diretamente do dongle USB 2.4 GHz, sem o MCHOSE HUB.

- Sem janela: apenas um ícone na bandeja com o percentual no tooltip.
- Somente leitura: envia um único comando conhecido e nada além disso.
- Tolerante a falhas: dongle removido, headset desligado, timeout ou resposta
  inválida nunca derrubam o aplicativo.

## Requisitos

- Windows 10/11 x64 (o código também publica para `win-arm64`).
- .NET SDK 8 para compilar (`winget install Microsoft.DotNet.SDK.8`).
- Nenhum pacote NuGet de HID: a comunicação usa P/Invoke em `setupapi.dll`,
  `hid.dll` e `kernel32.dll`.

## Compilar e executar

```powershell
dotnet restore
dotnet build
dotnet test tests/MchoseBattery.Core.Tests
dotnet run --project src/MchoseBattery.Tray
```

Publicação como executável único autocontido:

```powershell
dotnet publish src/MchoseBattery.Tray -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

O executável fica em
`src/MchoseBattery.Tray/bin/Release/net8.0-windows/win-x64/publish/MchoseBattery.exe`.

## Menu da bandeja

| Item | Efeito |
|---|---|
| `MCHOSE V9 Pro: NN%` / `Headset desconectado` / `Dongle não encontrado` | Estado atual (somente informativo) |
| `Atualizar agora` | Força uma leitura imediata |
| `Abrir ao iniciar o Windows` | Grava/remove o valor `MchoseBattery` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| `Sair` | Encerra o aplicativo |

A leitura automática ocorre a cada 30 segundos e também logo após qualquer
notificação de conexão/remoção de dispositivo HID.

## Ícone da bandeja

O próprio ícone mostra o percentual, sem precisar do mouse. A cor segue a carga:

| Faixa | Cor | Texto no ícone |
|---|---|---|
| 0–20% | vermelho | `0`..`20` |
| 21–40% | âmbar | `21`..`40` |
| 41–100% | verde | `41`..`100` |
| Headset desconectado | cinza | `--` |
| Dongle não encontrado | cinza | `?` |

O ícone é redesenhado no tamanho que o Windows pede (`SystemInformation.SmallIconSize`),
então acompanha a escala de DPI. Ele só é recriado quando o texto ou a cor mudam.

Para mantê-lo sempre visível: **Configurações → Personalização → Barra de tarefas →
Outros ícones da bandeja do sistema → MchoseBattery**. Sem isso o Windows o esconde
no menu de estouro (a seta `⌃`).

## Diagnóstico

```powershell
MchoseBattery.exe --diagnose            # ou --list-hid: lista todas as coleções HID (nada é enviado)
MchoseBattery.exe --inspect-device "<caminho>"   # capacidades de uma coleção (nada é enviado)
MchoseBattery.exe --probe-battery "<caminho>"    # único modo que escreve; imprime request/reply em hex
```

`--probe-battery` exige o caminho completo impresso por `--diagnose`, recusa
qualquer dispositivo fora de `291D:385D` e recusa caminhos que não identifiquem
exatamente uma coleção HID presente.

Os modos de diagnóstico escrevem no terminal que os invocou. Como o executável é
`WinExe`, use `MchoseBattery.exe --diagnose | Out-String` (ou redirecione a
saída) se o console não anexar automaticamente.

## Limite de segurança e evidência do protocolo

O perfil abaixo vale **apenas para o V9 Pro** e foi obtido por engenharia reversa
para fins de interoperabilidade. Ele foi **confirmado em hardware real**
(ver "Validação em hardware"):

```text
VID:PID        291D:385D
Frame          64 bytes (input e output)
Request        55 65 01 00 ... 00
Assinatura     55 65
Bateria        byte 2, percentual direto 0..100
Status         byte 3, guardado só para diagnóstico
Timeout        500 ms
```

O aplicativo envia **somente** esse request de leitura, e somente depois de o
dispositivo casar com o perfil. Nenhum comando de firmware, EQ, RGB, volume ou
desconhecido é enviado. O V9 comum não é assumido como compatível: se ele não
responder com a assinatura válida, o estado mostrado será `Headset desconectado`.

## Validação em hardware

Captura real de um V9 Pro, via `--probe-battery`:

```text
Path:        \\?\hid#vid_291d&pid_385d&mi_00&col05#...
VID:PID:     291D:385D; Version: 0x0012
Manufacturer: C-Media Electronics Inc
Product:     MCHOSE V9 PRO
Usage Page:  0xFF90; Usage: 0x0001   (vendor-defined)
Input: 64; Output: 64; Feature: 0

Request:  55 65 01 00 ... 00
Reply:    55 65 14 02 00 ... 00
Battery:  20%; status: 0x02
```

A coleção que responde é a **`col05`** da interface `mi_00`, com Usage Page vendor
`0xFF90`. O byte 2 da resposta (`0x14` = 20) é o percentual direto. O byte 3 (`0x02`) é
registrado mas não interpretado -- não há evidência suficiente para afirmar o que ele
significa.

## Estados

| Estado | Significado |
|---|---|
| `Dongle não encontrado` | Nenhuma coleção `291D:385D` com reports de 64 bytes |
| `Headset desconectado` | Dongle presente, mas sem resposta válida (headset desligado, fora de alcance ou timeout) |
| `MCHOSE V9 Pro: NN%` | Resposta validada pelo parser |

## Logs

`%LocalAppData%\MchoseBattery\mchose-battery.log`, limitado a 256 KiB (o trecho
mais antigo é descartado). Somente transições de estado e falhas são gravadas;
leituras normais bem-sucedidas não geram linhas.

## Estrutura

```text
src/MchoseBattery.Core    enumeração HID nativa, protocolo, transporte, polling, log
src/MchoseBattery.Tray    executável WinForms sem janela, CLI de diagnóstico, registro de inicialização
tests/MchoseBattery.Core.Tests  testes MSTest, sem necessidade de hardware
```

## Licença e isenção de responsabilidade

Distribuído sob a licença MIT — veja [LICENSE](LICENSE).

Este é um projeto independente, **sem qualquer vínculo, patrocínio ou aprovação
da MCHOSE ou da C-Media Electronics**. "MCHOSE" e "V9 Pro" são marcas de seus
respectivos donos e aparecem aqui apenas para identificar o hardware compatível.

O protocolo foi obtido por engenharia reversa para fins de interoperabilidade e
confirmado em hardware próprio. O
aplicativo envia **um único comando de leitura** e nenhum comando de escrita,
firmware ou configuração. Ainda assim, o software é fornecido "como está", sem
garantia: você assume o risco de executá-lo contra o seu dispositivo.
