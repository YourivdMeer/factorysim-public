# Fake-PLC

Een kleine Modbus TCP-server in Python die de PLC speelt, zodat de app zonder CODESYS te testen is. Alleen de standaardbibliotheek; Python 3.9 of hoger. Er hoeft niets geïnstalleerd te worden.

```
py tools/fake_plc/fake_plc.py --mode sorter --port 5020
```

| Optie | Standaard | Betekenis |
|-------|-----------|-----------|
| `--mode echo` | | Elke coil komt terug als discrete input met dezelfde offset, elk holding register als input register |
| `--mode sorter` | ✔ | Start/stop + sorteren op hoogte, dezelfde logica als het CODESYS-acceptatieprogramma |
| `--port` | 5020 | TCP-poort; `0` kiest een vrije poort |
| `--scan-ms` | 10 | Scan cycle in milliseconden |
| `--size` | 64 | Aantal coils, discrete inputs, holding registers en input registers |

Bij het opstarten print hij `READY port=… mode=… scan=…ms`; de integratietests lezen daar de poort uit.

## Indeling

Zelfde als `src/FactorySim.App/tagmap.json` en het CODESYS-device:

| Coil (app → PLC) | | Discrete input (PLC → app) | |
|---|---|---|---|
| 0 | iStart | 0 | oConveyor |
| 1 | iStop | 1 | oPusher |
| 2 | iAtPusher | 2 | oEmitter |
| 3 | iHighBox | 3 | oRunLamp |
| 4 | iPusherBack | | |
| 5 | iPusherFront | | |
| 6 | iSimRunning | | |

Ondersteunde function codes: FC01, FC02, FC05, FC15 voor de bits en FC03, FC04, FC06, FC16 voor de registers (sinds taak 6). Een register is een woord van 16 bit; de app zet er een `INT` in als two's complement. De modus `sorter` gebruikt alleen bits. De Unit ID wordt genegeerd.

## Beperkingen

- Bindt uitsluitend op `127.0.0.1`; dat is niet instelbaar (Modbus TCP heeft geen authenticatie).
- Windows-timing: de scan cycle van 10 ms is een streefwaarde, geen garantie.
