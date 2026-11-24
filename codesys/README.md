# CODESYS V3-project koppelen

Stappen om het acceptatieproject in CODESYS V3.5 op te zetten en aan de app te koppelen via Modbus TCP.

> **Status**: deze stappen zijn gebaseerd op de officiële documentatie en **nog niet op deze pc doorlopen**. Dat gebeurt in mijlpaal M4; afwijkingen komen dan hier en in `docs/TROUBLESHOOTING.md`.

In deze map staat alleen eigen ST-broncode als tekst. Het CODESYS-project zelf (`.project`) is binair en komt niet in git.

| Bestand | Inhoud |
|---------|--------|
| `acceptatie/IO.gvl.st` | Global Variable List `IO`: de in- en uitgangen |
| `acceptatie/PLC_PRG.st` | Start/stop + sorteren op hoogte |

## Nodig

- CODESYS Development System V3.5 **SP16 of hoger**, met CODESYS Control Win V3. Zonder licentie draait de runtime in demo-modus: twee uur per sessie, daarna opnieuw starten.
- Poort 502 vrij op deze pc.

## 1. Project

1. *File → New Project → Standard project*. Device: **CODESYS Control Win V3**; taal van `PLC_PRG`: Structured Text.
2. Rechtsklik *Application → Add Object → Global Variable List*, naam **`IO`**. Vervang de inhoud door die van `acceptatie/IO.gvl.st`.
3. Open `PLC_PRG`. Plak het declaratiedeel en het programmadeel uit `acceptatie/PLC_PRG.st` in het bovenste en onderste venster.
4. *Task Configuration → MainTask*: interval **t#10ms**.

## 2. Runtime

1. Rechtsklik op het CODESYS Control Win-icoon in de systray → **Start PLC**.
2. Dubbelklik *Device* → *Communication Settings* → **Scan network…** → kies deze pc.
3. Vanaf SP17 vraagt CODESYS om een device user aan te maken: doe dat en onthoud de gegevens.

## 3. Modbus TCP Slave Device

1. Rechtsklik *Device* → *Add Device…* → *Fieldbuses → Ethernet Adapter → Ethernet*.
2. Rechtsklik *Ethernet* → *Add Device…* → *Fieldbuses → Modbus → Modbus TCP Slave Device*.
3. Dubbelklik het slave-device, tab *General*: vink **Discrete Bit Areas** aan; **Coils: 8**, **Discrete Inputs: 8**; poort 502.
4. Tab *Modbus TCP Slave Device I/O Mapping*:

| Kanaal | Bit | Variabele |
|--------|----:|-----------|
| Coils | 0 | `IO.iStart` |
| Coils | 1 | `IO.iStop` |
| Coils | 2 | `IO.iAtPusher` |
| Coils | 3 | `IO.iHighBox` |
| Coils | 4 | `IO.iPusherBack` |
| Coils | 5 | `IO.iPusherFront` |
| Coils | 6 | `IO.iSimRunning` |
| Coils | 7 | `IO.iSpare` |
| Discrete Inputs | 0 | `IO.oConveyor` |
| Discrete Inputs | 1 | `IO.oPusher` |
| Discrete Inputs | 2 | `IO.oEmitter` |
| Discrete Inputs | 3 | `IO.oRunLamp` |
| Discrete Inputs | 4–7 | `IO.oSpare1` … `IO.oSpare4` |

5. Zet onderaan *Always update variables* op **Enabled**.

De mapping is ook uit de app te halen: knop *Exporteer mapping (CSV)*.

## 4. Downloaden en starten

1. *Build → Build* (F11): geen fouten.
2. *Online → Login* (Alt+F8); bevestig de download.
3. *Debug → Start* (F5).

## 5. App verbinden

1. Start de app (zie *Bouwen en starten* in de [README](../README.md)).
2. Verbindingsregel: host `127.0.0.1`, poort `502`, Unit **255** (of 0).
3. Klik **Verbinden**: de statusregel wordt groen en telt uitwisselingen.
4. Klik **Run** en daarna **Start**. Dozen spawnen; hoge dozen gaan de zijbaan in.

Zonder toekijken, als acceptatietest:

```
& $godot --headless --path src/FactorySim.App -- --selftest=plc:50 --plc-port=502
```

## Eerst zonder CODESYS proberen

De fake-PLC doet hetzelfde als dit programma, op poort 5020:

```
py tools/fake_plc/fake_plc.py --mode sorter --port 5020
```

Verbind de app dan met poort 5020. Werkt het daar wel en met CODESYS niet, dan zit het verschil in de CODESYS-configuratie: zie `docs/TROUBLESHOOTING.md` (Modbus-verbinding, CODESYS Control Win).

## Wat M4 nog moet uitwijzen

- Of coils in dit device inderdaad PLC-ingangen zijn en discrete inputs PLC-uitgangen.
- **Analoge tags** (taak 6, nog niet in CODESYS gecontroleerd): de app schrijft getallen naar de *holding registers* (FC16) en leest ze uit de *input registers* (FC04), elk register één `INT`. De aanname is dat het Modbus TCP Slave Device de holding registers als ingangswoorden van de PLC afbeeldt en de input registers als uitgangswoorden, net als bij de bits. Controleer ook het teken (−1 moet als −1 aankomen) en of het device de registers apart moet krijgen (*Holding registers*, *Input registers* in de configuratie).
- **Simulatie-tags** (taak 14, standaard uit): zet je in een scène `Sim paused` en `Sim reset` aan, dan komen ze in de acceptatiescène op coil 7 en 8. Coil 8 past niet in het device met 8 coils van stap 3: stel dan meer coils in (bijvoorbeeld 16). Het acceptatieproject zelf gebruikt ze niet en verandert niet.
- Of Unit ID 0/255 klopt voor de geïnstalleerde versie.
- Of *Always update variables* nodig is.
- De werkelijke reactietijd tegen Control Win bij een task van 10 en 20 ms.
