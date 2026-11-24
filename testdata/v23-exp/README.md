# Testmateriaal voor spoor B — CoDeSys V2.3 `.exp`

> **Let op: deze bestanden zijn synthetisch.** Ze zijn met de hand geschreven naar het voorbeeld van echte V2.3-exports en zijn **niet** door CoDeSys gegenereerd of in CoDeSys gecompileerd. Ze zijn bedoeld om de parser en de runtime op te starten, niet als bewijs van hoe het formaat eruitziet. Vervang ze door echte exports zodra die er zijn (map `echt/`).

## Bestanden

| Bestand | Dekt | Verwacht resultaat | Fase |
|---------|------|--------------------|------|
| `01_st_basis.exp` | ST zonder library; GVL met `AT %IX/%QX/%QW`; start/stop; eigen flankdetectie | Start → band, emitter en lamp aan; band stopt zodra er een doos voor de pusher staat; Stop → alles uit; `wStarts` telt startflanken | B1 |
| `02_timers_tellers.exp` | `TON`, `TOF`, `TP`, `R_TRIG`, `F_TRIG`, `CTU`, `CTD`, `SR`; `TIME`-literals; `LIBRARY`-blok; globale variabele zonder adres | Band start 2 s na Start; puls van 500 ms op de pusher per doos; lamp dooft 1 s na Stop; na 10 dozen stopt de band tot Stop | B2 |
| `03_fb_types.exp` | `TYPE` (enum en struct), `FUNCTION`, `FUNCTION_BLOCK` met instantie, `CASE` op enum, `FOR … BY -1`, array, structveld-toegang, `@PATH` met mappen | Hoge dozen worden uitgesorteerd; `wHoog`/`wLaag` tellen mee | B1 (zonder timers) |
| `04_sorteerscene.exp` | Volledig acceptatieprogramma van de MVP-scène (zelfde logica als notitie 06), met `TON`-bewaking, `VAR_CONFIG`, `LIBRARY` en `PLC_CONFIGURATION` | 50 dozen foutloos gesorteerd; fout na 3 s als de pusher zijn eindstand niet haalt | B2 |
| `05_niet_ondersteund.exp` | Bewust problematisch: library-FB zonder bron, `POINTER TO`/`ADR`/`^`, `SIZEOF`, bit-toegang `w.0`, `VAR RETAIN`, adres zonder sim-tag, niet-bereikbare POU, onbekend blok | Project start **niet**; het unsupported-rapport noemt elk punt hieronder | B1 |

## Verwacht unsupported-rapport voor `05_niet_ondersteund.exp`

| # | Waar | Melding | Ernst |
|---|------|---------|-------|
| 1 | `PLC_PRG`, declaratie | Type `VendorAnalogIn` komt uit `VendorIO.lib`; implementatie ontbreekt | Blokkerend |
| 2 | `PLC_PRG`, declaratie + body | `POINTER TO`, `ADR(...)`, dereferentie `^` niet ondersteund | Blokkerend |
| 3 | `PLC_PRG`, body | `SIZEOF` niet ondersteund | Blokkerend |
| 4 | `PLC_PRG`, body | Bit-toegang `wTeller.0` (ondersteund vanaf een latere fase, of direct — te beslissen in B1) | Blokkerend of geen |
| 5 | `PLC_PRG`, declaratie | `VAR RETAIN` wordt als gewone variabele behandeld | Waarschuwing |
| 6 | GVL | `iNietGekoppeld AT %IX7.3` heeft geen sim-tag | Waarschuwing |
| 7 | `FB_Ongebruikt` (pad `/Niet_bereikbaar`) | Bevat pointers, maar is niet bereikbaar vanuit de taak | Waarschuwing |
| 8 | Bestand, blok `_ONBEKEND_BLOK` | Onbekend blok overgeslagen (regelnummer) | Waarschuwing |

## I/O-adressen van de MVP-scène

| Adres | Variabele | Sim-tag |
|-------|-----------|---------|
| `%IX0.0` | `iStart` | Start button |
| `%IX0.1` | `iStop` | Stop button |
| `%IX0.2` | `iAtPusher` | At pusher |
| `%IX0.3` | `iHighBox` | High box |
| `%IX0.4` | `iPusherBack` | Pusher back |
| `%IX0.5` | `iPusherFront` | Pusher front |
| `%IX0.6` | `iSimRunning` | Sim running |
| `%QX0.0` | `oConveyor` | Conveyor |
| `%QX0.1` | `oPusher` | Pusher |
| `%QX0.2` | `oEmitter` | Emitter |
| `%QX0.3` | `oRunLamp` | Run lamp |
| `%QW2`, `%QW3` | tellers | (alleen in de variabelenlijst) |

## Wat aan deze bestanden onzeker is

- **Bevestigd met echte exports**: de vier metadata-regels boven een POU, `@END_DECLARATION` als scheiding, de opbouw van een GVL (`@GLOBAL_VARIABLE_LIST`, `@OBJECT_END`, `@CONNECTIONS`), het `LIBRARY`-blok, het `PLC_CONFIGURATION`-blok en `AT`-adressen in declaraties. Sinds 2026-11-23 ook met vier kleine echte projecten: regeleinden CRLF; LD (`_LD_BODY`) en FBD (`_FBD_BODY`) als token-stroom per netwerk; SFC als tekst (`INITIAL_STEP`, `STEP`, `TRANSITION FROM … TO …`).
- **Niet bevestigd**: de vorm van een `TYPE`-object (kop en `@END_DECLARATION` erna), de tekencodering (de echte exports bevatten alleen ASCII), een cyclische taak in de task configuration (ook de echte exports hebben er geen; `PLC_PRG` draait dan vanzelf), en of `%QW2` na `%QX0.x` zo geadresseerd wordt op een echt target.
- **Ontbreekt nog**: IL, CFC, een `TYPE` en een task configuration. Die komen uit echte exports (fase B0). Deze synthetische bestanden hebben nog steeds geen LD, FBD of SFC; daarvoor zijn de echte exports in `echt/` het voorbeeld.
- Het blok `_ONBEKEND_BLOK` in bestand 05 bestaat niet in CoDeSys; het test alleen of de parser onbekende blokken netjes meldt.

## Tests

`tests/FactorySim.Core.Tests/V23/ExpExportFilesTests.cs` splitst deze vijf bestanden met de container-parser (`FactorySim.Core.V23.ExpContainer`) en controleert dat elke regel met inhoud in precies één object valt. Dezelfde test leest elk `.exp`-bestand dat onder `echt/` staat (categorie `RealExports`). Staan er geen echte exports op de pc, dan slagen die tests zonder iets te controleren.

## Echte exports aanleveren

Zet ze in `testdata/v23-exp/echt/<projectnaam>/`, bij voorkeur:

1. Het hele project als **één** `.exp` (*Project → Export*, alles selecteren, "één bestand").
2. Hetzelfde project **per object** (één bestand per POU).
3. Een tekstbestandje met: CoDeSys-versie (bijv. 2.3.9.x), target/PLC, gebruikte talen, gebruikte libraries.

Voor fase B0 is daarnaast een klein proefproject nuttig met één POU per taal (ST, IL, LD, FBD, SFC), een `TYPE`, een cyclische taak en één in- en uitgang in de PLC Configuration.
