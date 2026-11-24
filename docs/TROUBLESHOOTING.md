# TROUBLESHOOTING

Bekende problemen en oplossingen. **Aanvullen zodra je iets oplost**: symptoom, oorzaak, oplossing, datum.

Status per item: **Verwacht** = uit onderzoek of ervaring elders, hier nog niet meegemaakt · **Bevestigd** = op deze pc meegemaakt en opgelost.

## Paden en OneDrive

### OneDrive-sync en bestandslocks — *Verwacht*
Speelt als de projectmap alsnog onder OneDrive komt te staan.
- **Symptomen**: build faalt met "file is being used by another process"; Godot meldt dat `.godot/` niet geschreven kan worden; git toont spontane wijzigingen of conflict-kopieën (`bestand-PCNAAM.ext`); trage builds.
- **Oorzaak**: OneDrive synchroniseert en vergrendelt bestanden in `bin/`, `obj/`, `.godot/` en `.git/` terwijl ze worden geschreven.
- **Oplossingen**, in volgorde van voorkeur:
  1. Houd de repo buiten een OneDrive-map.
  2. Pauzeer OneDrive-synchronisatie tijdens het bouwen.
  3. Sluit `bin/`, `obj/` en `.godot/` uit van synchronisatie.
- Zet nooit een `.git`-map in een gesynchroniseerde map op twee pc's tegelijk.

### Spatie in het pad — *Verwacht*
- **Symptoom**: een commando ziet een pad met een spatie als twee argumenten.
- **Oplossing**: paden altijd tussen aanhalingstekens; in PowerShell `-LiteralPath` gebruiken.

## Modbus-verbinding

### Connect mislukt — *Verwacht*
| Oorzaak | Controle | Oplossing |
|---------|----------|-----------|
| PLC of fake-PLC draait niet | Systray-icoon CODESYS Control Win; draait `fake_plc.py`? | *Start PLC*, login + start; of fake-PLC starten |
| Applicatie niet in RUN | CODESYS statusbalk | *Debug → Start* |
| Verkeerde poort | CODESYS: 502 · fake-PLC: 5020 | Poort in het verbindingspaneel aanpassen |
| Project draait in *Simulation* mode | *Online → Simulation* aangevinkt | Uitvinken; fieldbussen werken niet in simulatie |

### Verbonden, maar geen reactie — *Verwacht*
| Oorzaak | Oplossing |
|---------|-----------|
| Verkeerde Unit ID | Bij CODESYS SP16+ 0 of 255; bij oudere versies 1 |
| Uitgangen op coils gemapt in plaats van discrete inputs | Mapping in het Modbus TCP Slave Device nalopen |
| Variabelen worden niet bijgewerkt | *Always update variables* aanzetten in de I/O Mapping |
| Verkeerde offsets | `tagmap.json` vergelijken met de I/O Mapping (CSV-export) |

### Reactietijd hoger dan het poll-interval doet verwachten — *Bevestigd en opgelost 2026-11-15*
- **Symptoom**: sensorflank → actuator duurde gemiddeld 28 ms (p99 53 ms) bij poll 10 ms en scan 10 ms.
- **Oorzaak**: Windows rondt wachttijden standaard af op 15,6 ms, waardoor een poll van 10 ms in de praktijk ~16 ms werd.
- **Oplossing**: `ModbusTcpLink` en de fake-PLC vragen met `timeBeginPeriod(1)` een timerresolutie van 1 ms aan zolang ze draaien. Daarna gemiddeld 22 ms (p99 32 ms).
- CODESYS Control Win heeft zijn eigen timing; meet de werkelijke waarden in M4.

### Er blijft een `python.exe` van de fake-PLC draaien — *Verwacht*
- De `py`-launcher start `python.exe` als kindproces. Stop je alleen de launcher, dan blijft de server luisteren en is de poort bezet.
- **Controle en oplossing**: `Get-CimInstance Win32_Process -Filter "Name='python.exe'" | Where-Object CommandLine -match 'fake_plc'` en dat proces stoppen. De integratietests ruimen de hele procesboom zelf op.

### Verbinding valt af en toe weg — *Verwacht*
- Time-out te krap bij een zware pc-belasting → poll-interval of time-out verhogen in het verbindingspaneel.
- De app herverbindt vanzelf; blijft dat uit, kijk dan in de foutteller en het log.

## Firewall en poort 502

### Poort 502 is bezet — *Verwacht*
- **Controle**: `Get-NetTCPConnection -LocalPort 502` toont welk proces luistert.
- **Oplossing**: het andere proces stoppen, of in het Modbus TCP Slave Device én in de app een andere poort kiezen.

### Windows Firewall vraagt om toegang — *Verwacht*
- CODESYS Control Win luistert op de netwerkadapters. Voor dit project is alleen localhost nodig: kies **geen** toegang voor openbare netwerken.
- Verkeer via `127.0.0.1` wordt normaal niet geblokkeerd. Lukt verbinden niet, controleer dan of beveiligingssoftware loopback-verkeer filtert.
- Zie [SECURITY.md](SECURITY.md): poort 502 nooit naar buiten openzetten.

### Fake-PLC start niet op poort 502 — *Verwacht*
- Poorten onder 1024 kunnen beheerdersrechten vragen of bezet zijn. De fake-PLC gebruikt daarom standaard **5020**.

## CODESYS Control Win

### Alles werkt en stopt na ongeveer twee uur — *Verwacht*
- **Oorzaak**: zonder licentie draait de runtime in demo-modus, twee uur per sessie.
- **Oplossing**: *Start PLC* opnieuw, daarna login + start. De app herverbindt vanzelf.

### Login vraagt om gebruikersnaam en wachtwoord — *Verwacht*
- **Oorzaak**: vanaf runtime SP17 is device user management verplicht.
- **Oplossing**: bij de eerste verbinding een device user aanmaken en onthouden. Voor Modbus is verder geen security-instelling nodig.

### Systray-icoon ontbreekt — *Verwacht*
- Start `CODESYSControlSysTray.exe` uit de map `GatewayPLC` van de CODESYS-installatie, of start de Windows-service *CODESYS Control Win V3*.

### Modbus-device toont een rood driehoekje — *Verwacht*
- Project in simulatiemodus, verkeerde netwerkadapter bij het Ethernet-device, of de applicatie is niet gestart.

## 32- versus 64-bit CODESYS

### V2.3-project openen lukt niet in CODESYS V3 — *Verwacht*
- **Oorzaak**: de CODESYS V2.3 Converter werkt alleen in een **32-bit** CODESYS V3.
- **Oplossing**: naast de 64-bit versie ook 32-bit V3.5 installeren, daarin de converter installeren, het project converteren en daarna in 64-bit openen. Alleen relevant voor het vangnet "V2.3 via spoor A"; route B heeft geen converter nodig.

### Verkeerde CODESYS-instantie start vanaf de command line — *Verwacht*
- Bij meerdere installaties: `--profile` exact zoals in *Help → About*, en zo nodig `--additionalfolder`.

## Physics

### Dozen blijven stilstaan op een bocht — *Bevestigd en opgelost 2026-11-16*
- **Symptoom**: dozen hoopten zich op bij de overgang van de band naar de bocht en vielen eraf.
- **Oorzaak**: de bocht was één statische body met een draaiende oppervlaktesnelheid (`ConstantAngularVelocity`). De physics-engine (Jolt in Godot 4.7.2) neemt dozen daar niet mee; `ConstantLinearVelocity` werkt wel.
- **Oplossing**: de bocht bestaat nu uit twaalf statische segmenten met elk een rechte oppervlaktesnelheid langs hun eigen raaklijn.

### Pallet staat 10 cm naast zijn vak in de stelling — *Opgelost 2026-11-19*

**Symptoom**: in de zelftest `crane` lagen de pallets op z = −1,30 in plaats van −1,40, buiten de marge van 5 cm.

**Oorzaak**: de editor zet elk onderdeel op een raster van 0,25 m (`Scene.Snap`). De stelling en de pallet-emitter waren op 1,4 m van de rail gedacht en sprongen naar 1,5 m. De vorken reikten maar 1,4 m, dus de pallet lag 10 cm uit het midden op de vorken en kwam zo ook in het vak.

**Oplossing**: alle maten die de gebruiker moet plaatsen liggen op het raster: stelling en rollenbanen op 1,5 m van de rail, vorkslag 1,5 m. Reken bij een nieuw onderdeel met plaatsingsafstanden altijd met veelvouden van 0,25 m.

### Pallet in het onderste vak telt als gevallen — *Opgelost 2026-11-19*

**Symptoom**: de statusbalk en `Scene.CountBoxes()` meldden 1 gevallen doos terwijl alle pallets netjes in hun vak lagen.

**Oorzaak**: `CountBoxes` telt elke doos onder `Main.FallenBelowY` (0,45 m) als gevallen. Een pallet op draagbalken van 0,3 m heeft zijn midden op 0,37 m.

**Oplossing**: het onderste niveau van de stelling ligt op 0,6 m (`first_level_top` in de generator), zodat het midden van een pallet op 0,67 m ligt. Het niveau mag niet te dicht bij de rollenbaanhoogte (0,5 m) liggen, anders lichten `at level 0` en `at level 1` in jog-modus tegelijk op. Items die bewust lager dan 0,45 m horen te liggen zouden `FallenBelowY` moeten aanpassen.

### Tweede pallet verscheen niet — *Opgelost 2026-11-19*

**Symptoom**: in `crane` ging de kraan voor de tweede en derde pallet naar *in* en vond niets.

**Oorzaak**: de pallet-emitter heeft een interval van 10 s en de PLC zette hem maar 2,5 s aan per pallet.

**Oplossing**: de zelftest zet het interval van de emitter op 1 s. Een emitter telt alleen tijd mee terwijl hij aan staat.

### Doos ligt 2 cm naast het midden na de positioneerbalken — *Opgelost 2026-11-20*

**Symptoom**: in de zelftest `transport` mat een doos die de klemmen net losliet 2,0 cm uit het midden, terwijl de geklemde doos zelf op 0,0 cm lag.

**Oorzaak**: een tweede doos stond 0,5 m achter de eerste, met zijn voorkant al tussen de klemmen (de bladen zijn 0,5 m lang). Die werd scheef gedrukt (15° tot 20°) en telde mee voor de breedte waarop de klemmen stoppen. Dat komt door een band die stilstaat terwijl de emitter doorgaat met spawnen.

**Oplossing**: laat bij de balken maar één item tegelijk staan. In de zelftest heeft de emitter van die rij een interval van 8 s. Het staat bij de bekende beperkingen in `SCENES.md`.

### Een bochtband scrolt niet — *Bevestigd 2026-11-20, laten staan*

**Symptoom**: de band van `belt_conveyor_curved` en `roller_conveyor_curved` beweegt niet mee terwijl de dozen wel door de bocht gaan.

**Oorzaak**: het model heeft geen `Belt`-object met een textuur die langs de boog loopt; de strook is een stuk vlak.

**Oplossing**: nog niet. Alleen doen als het eenvoudig blijkt (zie plan 02); de simulatie zelf is goed.

### Tijdschaal: `Engine.TimeScale` maakt de physics-stap groter — *Gemeten 2026-11-21*
- **Vraag** (plan 09, stap 0): wat doet `Engine.TimeScale` in Godot 4.7.2 met de physics-ticks?
- **Gemeten** (headless, 3 s per stand, ± 145 fps): bij schaal 1 en 4 en 0,25 blijven het 120 ticks per seconde echte tijd; de `delta` per tick wordt 1/30 s bij ×4 en 1/480 s bij ×0,25. Met `PhysicsTicksPerSecond` = 120 × schaal blijft de `delta` 1/120 s: 480 ticks per seconde bij ×4, 30 bij ×0,25, en de simulatietijd loopt 4 of 0,25 keer zo snel als de echte tijd.
- **Daarom**: `Main.ApplyTimeScale` zet beide, en boven ×1 ook `MaxPhysicsStepsPerFrame` op 8 × schaal (bij 60 fps zijn er 8 per frame nodig voor ×4).
- **Met een venster op een onbeheerde pc** (geplande run, 's nachts) haalde Godot maar 1–5 fps, ook bij ×1 en zonder tijdschaal (`sort:3` met venster: 19 s simulatie in 149 s). Dan haalt de engine hoogstens 8–32 stappen per frame en loopt de simulatie achter op elke schaal. De zelftest `timescale` beoordeelt de timing daarom alleen bij gemiddeld ≥ 60 fps. Met een venster op een wakker scherm is hij nog niet gedraaid.

## Blender en assets

### Na een Blender-rebuild staan er .glb-bestanden en een catalogus in `git status` die je niet bedoelde — *Bevestigd 2026-11-19*

**Symptoom**: `build_assets.py` zonder `--only` schrijft alle modellen opnieuw. Een deel (de items met deksel, `emitter_raw_*`, `stacker_crane`, soms de banden) komt anders uit de export dan in git staat, ook als je er niets aan veranderde.

**Oorzaak**: de export is niet byte voor byte reproduceerbaar.

**Oplossing**: zet alleen de bestanden terug die je niet bedoelde, bijvoorbeeld `git status --short | grep "\.glb$" | awk '{print $2}' | grep -v mijn_asset | xargs git checkout --`. Neem `catalog.json` niet mee in een `git checkout` van een map: de catalogus hoort bij de rebuild en staat op twee plekken (`assets/library/` en `src/FactorySim.App/library/`). Na `--only=...` ook `docs/assets-preview/library_<categorie>.png` terugzetten: die wordt dan overschreven met een blad van alleen die assets.

Wil je geen `git checkout --` gebruiken, zet de onbedoelde modellen dan weg met `git stash push -m "rebuild-glb <taak> <datum>" -- <bestanden>` (taak 13, 2026-11-21: 14 modellen in beide mappen). Draai daarna `--import` en kijk opnieuw naar `git status`.

### Een absoluut pad in de `.blend` — *Opgelost 2026-11-24*
- **Symptoom**: in `assets/source/factorysim_assets.blend` stond het uitvoerpad van de laatste thumbnail of het laatste contactblad: een absoluut pad op de pc waar de generator draaide.
- **Oorzaak**: `render_thumbnail` en de contactbladen zetten `scene.render.filepath`, en `save_source()` slaat de scène daarna op. Dat pad is een vaste buffer van 1024 tekens die in zijn geheel in het bestand komt; een kortere waarde zoals `"//"` overschrijft alleen het begin, de rest van het oude pad blijft staan.
- **Oplossing**: `save_source()` vult de buffer eerst helemaal met spaties en zet hem daarna op `"//"`. Controleren kan door de `.blend` uit te pakken met de Python van Blender (de `.blend` is met zstd gecomprimeerd) en in de bytes naar het pad te zoeken.
- Sla je de `.blend` met de hand op terwijl het bronbestand open is, dan gaat ook de UI-data mee, zoals de laatste map van de bestandskiezer. De generator op de achtergrond doet dat niet.

### Een zelftest-opstelling valt door de vloer buiten x = ±7 m — *Opgelost 2026-11-21*
- **Symptoom**: in de eerste versie van `--selftest=decor` viel de doos boven de trap (op x = 8) tot y = −67 m, en de band op x = 13–15 duwde zijn doos de leegte in.
- **Oorzaak**: de standaardvloer van een nieuwe scène is 14 m lang, van x = −7 tot 7. Daarbuiten is er geen vloer en geen botsvorm.
- **Oplossing**: de rij van `decor` ligt tussen x = −6,5 en 6,6. Een opstelling die langer is, moet eerst de vloer groter maken (*Scène* → *Vloer…*, of `Scene.SetFloor` in een test).

### Een onderdeel knippert in de verkeerde kleur (sirene) — *Opgelost 2026-11-21*
- **Symptoom**: de app laat het verkeerde stuk van een model oplichten; bij de sirene zou de donkere rand knipperen in plaats van het rode lichaam.
- **Oorzaak**: `Models.OwnMaterial` neemt het eerste oppervlak (surface 0) van een onderdeel. De export zet de oppervlakken op volgorde van de materiaalslots, en in het gedeelde palet komt `frame_dark` vóór `red`.
- **Oplossing**: in de generator krijgt het onderdeel `Siren` een eigen materiaallijst met rood eerst (`_alarm_siren` in `fslib/parts_plant.py`). Let hierop bij elk onderdeel dat de app laat oplichten: het materiaal dat moet veranderen moet het eerste zijn.
- Daarbij: na het uitzetten werd de sirene bijna zwart (de donkere helft van het knipperen). `Siren` zet nu bij uit de gewone kleur terug.

### De export bevat een object uit een andere scène (bijv. `Cube`) — *Bevestigd en opgelost 2026-11-15*
- **Symptoom**: elke `.glb` bevatte naast het asset ook de standaard-`Cube`.
- **Oorzaak**: het script bouwde in een eigen scène, maar de glTF-exporter las de selectie van de scène waar Blender op dat moment in stond.
- **Oplossing**: het script werkt alleen nog als zijn eigen scène de actieve is (`get_scene()`); op de achtergrond neemt het de opstartscène over.

### Blender crasht met `EXCEPTION_ACCESS_VIOLATION` tijdens de export — *Bevestigd en opgelost 2026-11-15*
- **Oorzaak**: de exporter aanroepen met `bpy.context.temp_override(scene=…)` op een scène die niet de actieve is. De crash zit in het bijwerken van de dependency graph (Blender 5.3; crashlog in `%TEMP%\blender.crash.txt`).
- **Oplossing**: geen context-override; eerst de scène actief maken, dan exporteren.

### De Blender MCP geeft op elke aanroep "Request timed out" — *Bevestigd 2026-11-15, niet opgelost*
- Gebeurde direct na de aanroep die de crash hierboven veroorzaakte; daarna reageerden ook de `_for_cli`-varianten niet meer. Het Blender-venster zelf bleef open.
- **Omweg**: het generatorscript rechtstreeks draaien met `blender --background --factory-startup --python …` (zie `ASSETS.md`). Dat heeft de MCP niet nodig.
- **Herstel**: Blender en de MCP-koppeling opnieuw starten. Let op: er stond een niet-opgeslagen bestand open in die Blender-sessie.

### `No loader found for resource: res://models/….glb` — *Bevestigd en opgelost 2026-11-15*
- **Oorzaak**: Godot had de nieuwe modellen nog niet (allemaal) geïmporteerd. De import was halverwege afgebroken doordat de uitvoer van het commando werd afgekapt, wat het proces stopte.
- **Oplossing**: `& $godot --headless --path src/FactorySim.App --import` helemaal laten uitlopen (uitvoer niet afkappen met `Select-Object -First`). Er hoort per `.glb` een `.import`-bestand te staan. Een achtergebleven `*.tmp` in `models/` mag weg.

### Een onderdeel van een model wordt niet gevonden (`has no part named …`) — *Verwacht*
- Blender-objectnamen moeten uniek zijn; het script zet daarom tijdens de export tijdelijk korte namen (`Belt`, `PusherPlate`). Komt er `Belt_001` in Godot aan, dan is het asset buiten het script om geëxporteerd. Opnieuw bouwen met het script.

### FPS met V-Sync schommelt tussen ~20 en 60 — *Bevestigd 2026-11-15, oorzaak onbekend*
- Zelfde scène, opeenvolgende runs: 30, 60, 41 FPS; met minder modellen in beeld zelfs 19. Zonder V-Sync (`--disable-vsync`) haalt de scène ruim 800 FPS, dus het ligt niet aan de modellen of de rekentijd.
- Vermoeden: frame-presentatie op deze laptop met twee GPU's (Intel Arc + NVIDIA); Godot gebruikt de geïntegreerde. Niet verder uitgezocht. De simulatie zelf draait op een vaste physics tick en heeft er geen last van.

## Tooling

### Tekst in een Godot-`Label` heeft dubbele regelafstand — *Bevestigd en opgelost 2026-11-15*
- **Symptoom**: de debug-overlay toonde een lege regel tussen elke regel.
- **Oorzaak**: `StringBuilder.AppendLine` schrijft op Windows `\r\n`; Godot toont de `\r` als extra regeleinde.
- **Oplossing**: regels samenvoegen met `string.Join("\n", …)`.

### Godot-uitvoer verschijnt niet in de terminal — *Bevestigd 2026-11-15*
- Gebruik `Godot_v4.7.2-stable_mono_win64_console.exe` in plaats van de gewone `.exe`; alleen de console-variant schrijft naar de terminal en geeft een exitcode terug.

### Wijziging in C# heeft geen effect in Godot — *Verwacht*
- Bij starten vanaf de command line bouwt Godot de C#-code niet zelf. Eerst `dotnet build FactorySim.sln`, dan Godot starten.

### `godot` wordt niet gevonden — *Bevestigd 2026-11-14, opgelost 2026-11-15*
- Godot 4.7.2 .NET staat in een eigen map buiten de repo (checksum gecontroleerd) en niet in `PATH`. Gebruik het volledige pad naar de console-exe; in de commando's heet dat pad `$godot`.
- Zonder Godot compileert de scènecode wel met `dotnet build` (het `Godot.NET.Sdk`-pakket komt van NuGet), maar draait ze niet.

### Godot meldt een versieverschil of wil het project converteren — *Verwacht*
- **Oorzaak**: het `.csproj` verwijst naar `Godot.NET.Sdk/4.7.2`; de editor heeft een andere versie.
- **Oplossing**: dezelfde versie installeren, of het versienummer in `src/FactorySim.App/FactorySim.App.csproj` en de regel `config/features` in `project.godot` aanpassen aan de editor en opnieuw builden.

### Godot maakt een eigen `FactorySim.App.sln` aan — *Verwacht*
- Godot zoekt een solution met de naam van de assembly in de projectmap en maakt die bij de eerste build aan. Dat is in orde; hij mag naast `FactorySim.sln` in de repo-root bestaan en wordt meegecommit.

### `python` wijst naar een andere installatie — *Bevestigd 2026-11-14*
- `python` verwijst op deze pc naar de Python van Espressif (`C:\Espressif\tools\python\`). Gebruik de launcher `py` voor dit project, eventueel met een eigen virtual environment in `tools/fake_plc/.venv/`.

### App start met een andere scène, een andere poort of een vreemd venster — *Bevestigd 2026-11-21*

**Symptoom**: de app opent niet de MVP-scène, het PLC-venster toont niet poort 502, of het venster staat op een andere plek dan verwacht.

**Oorzaak**: sinds taak 15 onthoudt de app scène, PLC-velden, camera, onderpaneel en venster in `%APPDATA%\Godot\app_userdata\FactorySim\settings.cfg`.

**Oplossing**: dat is zo bedoeld. Terug naar de standaard: sluit de app en verwijder `settings.cfg`. Een scène met `--scene=` op de commandoregel gaat altijd voor. Een venster dat op een scherm stond dat er niet meer is, komt vanzelf op de standaardplek.

### Bij het openen vraagt de app "Er is een automatisch opgeslagen versie … Herstellen?" — *Bevestigd 2026-11-21*

**Symptoom**: bij het starten of bij *Openen…* verschijnt de vraag of een automatisch opgeslagen versie hersteld moet worden.

**Oorzaak**: sinds taak 16 schrijft de editor elke 2 minuten (en bij het verlaten van de ontwerpmodus) een kopie van een gewijzigde scène naar `%APPDATA%\Godot\app_userdata\FactorySim\autosave\`. Er is een kopie die nieuwer is dan het scènebestand, bijvoorbeeld na een crash of omdat je niet hebt opgeslagen.

**Oplossing**: *Herstellen* laadt de kopie (nog niet opgeslagen; de kopie blijft staan tot je *Opslaan* kiest). *Niet nu* laat alles zoals het is; de vraag komt terug tot je de scène opslaat. Wil je de kopieën kwijt: sla de scène op, of verwijder de bestanden in de map `autosave`.

### Backslashes verdwijnen uit tekst die via een shell-heredoc wordt geschreven — *Bevestigd 2026-11-21*

**Symptoom**: na een bewerking met een script in een heredoc staat er in C# `'\\'` als `'\'`, met één backslash (compileerfout *Too many characters in character literal*), of in een doc `Godotpp_userdata` in plaats van `Godot\app_userdata`.

**Oorzaak**: de Bash-tool van de agent laat een dubbele backslash in een heredoc als één backslash aankomen; `\a` wordt daarna een bel-teken.

**Oplossing**: schrijf tekst met backslashes met de Write-tool of in een scriptbestand, niet inline in een heredoc. Controleer na een scripted wijziging met `grep` op de plek.

### Zwenkarm slaat een doos van de band — *Bevestigd 2026-11-16*

**Symptoom**: bij het uitzwenken van de zwenkarm-sorteerder vliegt een doos van de band ("Gevallen" loopt op).

**Oorzaak**: de arm zwenkt in 0,4 s over de band. Een doos die op dat moment binnen het bereik van de arm staat, wordt weggeslagen. Gezien op 2026-11-16 in de zelftest `sorters`, toen de arm uitzwenkte terwijl de lijn vol stond.

**Oplossing**: de PLC moet de arm uitsturen vóórdat de doos er is (sensor stroomopwaarts), niet als de doos al voor de arm staat. Langzamer zwenken kan met `"swingSpeed"` in de `params` van het onderdeel. De zelftest laat de lijn nu eerst leeglopen.

### Tag table loopt van het scherm af — *Opgelost 2026-11-16*

**Symptoom**: bij een scène met veel tags (gezien bij de draaitafel-lijn, 14 tags) vielen de onderste rijen en de knop *Exporteer mapping* buiten het venster.

**Oorzaak**: de tabel groeide mee met het aantal tags, zonder grens.

**Oplossing**: boven de twaalf rijen staat de tabel in een scrollvak (`Hud.BuildTagTable`).

### Sensor ziet de dozen op een rollenbaan niet — *Bevestigd 2026-11-16*

**Symptoom**: een diffuse sensor naast een rollenbaan blijft uit.

**Oorzaak**: sensoren worden op 0,9 m geplaatst; een rollenbaan is 0,5 m hoog en een lage doos komt tot 0,7 m. De straal gaat eroverheen.

**Oplossing**: selecteer de sensor in de ontwerpmodus en zet *Hoogte (m)* op 0,6.

### Zelftest `lift` faalde af en toe op de hoogte bovenaan — *Opgelost 2026-11-16*

**Symptoom**: `SELFTEST lift box carried up: FAILED` in een headless run, terwijl dezelfde test met venster slaagde en de gemelde waarden gelijk waren (`surface=3,50`).

**Oorzaak**: de eindstand `at top` sluit al binnen 5 mm van het eind van de slag (`PusherLogic.LimitTolerance`). De test eiste de hoogte op 1 mm nauwkeurig op het moment dat de eindstand voor het eerst aan was; of dat lukte hing af van de timing van het frame.

**Oplossing**: de test rekent nu met de tolerantie van de eindstand. De lift zelf was in orde.

### Zelftest `ui` telde 1 steunbalk in plaats van 8 — *Opgelost 2026-11-17*

**Symptoom**: `SELFTEST ui support frame under a raised conveyor: FAILED` (`raised=1`), terwijl het onderstel op het screenshot compleet was.

**Oorzaak**: de test telde child-nodes op naam. Godot geeft een tweede node met dezelfde naam onder dezelfde ouder een automatische naam (`@Node3D@…`), dus alleen de eerste balk heette nog `Support`.

**Oplossing**: de balken zitten nu in de groep `supports` en de test telt op groep. Tel of zoek nodes niet op een naam die meer dan één keer voorkomt.

### Productdeksel zweefde boven de band — *Opgelost 2026-11-17*

**Symptoom**: een productdeksel (en een eindproduct) lag als item 1 tot 1,5 cm boven de band of de tafel.

**Oorzaak**: de collider van een item is een doos rond de origin, met de maat uit de catalogus. Het model van het deksel was niet rond zijn origin gebouwd (de knop stak alleen naar boven uit), dus model en collider lagen verschoven.

**Oplossing**: beide modellen zijn in de generator gecentreerd. De unit test `Items_are_centred_on_their_origin_like_their_collider` controleert dit voor elk item in de catalogus. Bouw een nieuw item altijd symmetrisch rond de origin.

## Test-API

### `HttpListener` of `TcpListener` (plan 21, stap 0) — *Bevestigd 2026-11-23*

**Vraag**: vraagt `HttpListener` op Windows beheerdersrechten (een URL-reservering met `netsh http add urlacl`) voor `http://127.0.0.1:5080/`?

**Uitkomst**: op deze pc niet: `HttpListener` startte als gewone gebruiker op `http://127.0.0.1:5081/` en `http://localhost:5082/` (getest in PowerShell 5.1). Toch gebruikt de server een `TcpListener` met een kleine eigen HTTP-afhandeling (`HttpWire`: alleen `Content-Length`, geen chunked bodies, geen keep-alive). Zo bindt hij aantoonbaar alleen op `127.0.0.1` (de integratietest leest het gebonden adres), liggen de groottegrenzen en de time-out in eigen code, en hangt het niet af van http.sys of URL-reserveringen die per pc kunnen verschillen.

### Het voorbeeldscript zegt FAIL met een korte `--seconds` — *Bevestigd 2026-11-23*

**Symptoom**: `py tools/api/run_test.py --seconds 20` geeft `RESULT: FAIL` met `"End remover": 0`.

**Oorzaak**: in 20 s heeft nog geen lage doos het einde van de band gehaald; de hoge dozen gaan eerder de zijbaan in. Het script eist dozen in beide removers.

**Oplossing**: laat het minstens ± 30 s lopen (standaard 60 s; de zelftest gebruikt 40 s).

### Zelftest `api`: de main thread mag niet wachten op het script — *Ontwerp 2026-11-23*

De zelftest start `run_test.py` als subprocess. Zou hij op de main thread op het proces wachten (`WaitForExit`), dan leegt niemand de wachtrij van de API en krijgt elke opdracht na 5 s `504`. Daarom kijkt de zelftest elk frame of het proces klaar is.

## Scènes (plan 22)

### Mijn eigen levels zijn weg na de update — *Bekend 2026-11-23*

**Symptoom**: de galerij toont onder *Eigen scènes* niet alles wat er vroeger onder *Eigen levels* stond.

**Oorzaak**: de eigen map is nu `user://scenes/`. De app kopieert `user://levels/` daarheen bij de **eerste** start na de update en daarna nooit meer (het bestand `.migrated-from-levels` in de nieuwe map onthoudt dat). Wat je daarna nog in de oude map zet, ziet de galerij niet.

**Oplossing**: de oude map staat er nog (`%APPDATA%\Godotpp_userdata\FactorySim\levels\`). Open zo'n bestand met *Scène* → *Importeren…* (die toont `.fslevel.json` en `.fsscene.json`) en sla het op; of verwijder `.migrated-from-levels` uit de map `scenes` en start de app opnieuw (bestaande scènes worden niet overschreven).

### `Scene` betekent in de app twee dingen — *Ontwerp 2026-11-23*

Godot heeft geen type `Scene`, wel `PackedScene` en `SceneTree`; daar botst niets. Maar in `Main` heette het 3D-beeld al `Scene` (type `LevelScene`) en in de zelftests heette de verwijzing naar `Main` ook `Scene`. Na de hernoeming heet het model in `Main` `Scene` (type `Scene`, dezelfde naam als het type, dus `Scene.MinFloor` werkt ook), het 3D-beeld `View` (type `SceneView`) en de verwijzing in de zelftests `Main`. Een statische aanroep `Scene.X` in een klasse met een eigen lid `Scene` van een ander type gaat naar dat lid; geef dan de volle naam `FactorySim.Core.Scenes.Scene.X`.

## Icoon en opstartscherm (plan 25)

- **Windows toont nog het oude icoon** (Godot-logo in de taakbalk of in de Verkenner): Windows houdt iconen vast in een cache. Voer `ie4uinit.exe -ClearIconCache` uit, of meld je af en weer aan.
- **Nog het Godot-icoon in de taakbalk bij starten met `--path`**: volgens de Godot-docs geldt `config/windows_native_icon` voor de geëxporteerde app; vanuit de editor of met `--path` kan Godot het standaard-icoon tonen. Echt controleren kan pas na de export (plan `windows-export`, dat de `.ico` ook als exe-icoon zet).
- **Het icoon op 16 px is een vlek**: het volledige icoon heeft te veel details voor 16 en 24 px; daarom gebruikt de `.ico` daar `factorysim-icon-small.svg` met een grotere doos en dunnere lijnen om de doos (de eerste versie met dikke lijnen om de doos liet op 16 px geen amber over).

## Welkomstvenster (plan 30)

### Zelftest `ui`: "side panels in design mode: FAILED" na een extra knop in de werkbalk — *Bevestigd 2026-11-23*

**Symptoom**: na een knop *Aan de slag* rechtsboven in de werkbalk faalde de check `side panels in design mode`; het vrije deel liep tot x = 1437 bij een venster van 1600 px.
**Oorzaak**: in de ontwerpmodus is de werkbalk al bijna 1600 px breed. Een extra knop maakt de minimumbreedte van de hele schil groter dan het venster, en het rechterpaneel schuift buiten beeld.
**Oplossing**: *Aan de slag* staat als knop in het venster van **?** (`AcceptDialog.AddButton`), niet in de werkbalk. Er is ± 16 px over.

### Het welkomstvenster komt niet (meer) — *Ontwerp 2026-11-23*

Het venster komt alleen als er nog geen `settings.cfg` is, of als daar `welcome.show = "true"` in staat (na een eerste start die met *Sluiten* is afgesloten). Wie de app al gebruikte vóór plan 30 heeft die sleutel niet en krijgt het venster niet; het staat via **?** → **Aan de slag**. Opnieuw als eerste start: `settings.cfg` weghalen (`%APPDATA%\Godotpp_userdata\FactorySim\`). Een zelftest toont het nooit.

## Fouten en crashes (plan 31)

### De app ging dicht; waar staat wat er misging? — *Bekend 2026-11-23*

In de logmap: `%APPDATA%\Godotpp_userdata\FactorySim\logs\` (ook in *Over*, rij *Logbestanden*). `godot.log` is het log van de laatste start (oudere als `godot<datum>.log`); na een crash staat er ook `crash-<datum>-<tijd>.txt` met de versie en de hele exception. Een fout die de app niet stopt staat onderin de statusregel; **Details** opent `godot.log`.

### Een C#-exception in `_Process` komt niet bij `AppDomain.UnhandledException` — *Bevestigd 2026-11-23*

**Symptoom**: een handler op `AppDomain.CurrentDomain.UnhandledException` ziet een exception in een callback (`_Process`, een signaal, een uitgestelde aanroep) niet.
**Oorzaak**: Godot vangt die zelf af en logt hem als fout (`ERROR: System.…Exception: …` met de stack); de app loopt door. Alleen een exception op een eigen thread stopt het proces en komt bij de handler.
**Oplossing**: een `Logger` (`ErrorLogger`, `OS.AddLogger`) ziet elke gelogde fout; die gaat naar de statusregel. Gecontroleerd in de zelftest `ui` met een exception in `Callable.From(...).CallDeferred()`. Een `Logger` wordt vanuit elke thread aangeroepen en mag zelf niets loggen; hij zet alleen iets in `ErrorFeed` (met een lock) en `Main` haalt het er elke frame uit.

### Een uitgeschakelde knop gaat toch af met `EmitSignal(Pressed)` — *Bevestigd 2026-11-24*

**Symptoom**: de knop **Ingebouwde test-PLC** staat uit (`Disabled`), maar de zelftest drukt hem met `EmitSignal(BaseButton.SignalName.Pressed)` en de handler loopt toch.
**Oorzaak**: `Disabled` houdt alleen muis en toetsenbord tegen; een signaal dat de code zelf uitzendt gaat gewoon naar de handler.
**Oplossing**: de handler (`Main.StartTestPlc`) controleert zelf of de scène past (`TestPlcFit`) en doet anders niets. De zelftest `ui` drukt de uitgeschakelde knop en controleert dat er geen test-PLC draait.

### Rechterpaneel (deels) buiten beeld in de ontwerpmodus — *Bevestigd 2026-11-24*

**Symptoom**: bij een venster van 1600 px of smaller valt het paneel *Eigenschappen* rechts deels buiten het venster; bij 1366 en 1280 px ook bij simuleren de knoppen rechtsboven.
**Oorzaak**: een `HBoxContainer` krimpt niet onder de som van de minimumbreedtes van zijn kinderen; is die groter dan het venster, dan wordt de hele layout breder dan het venster. De werkbalk was 1648 px bij ontwerpen; onderin maakte een lang exportpad (een `Label` zonder afkappen) de layout 1574 px breed.
**Oplossing** (plan 35): de simulatieknoppen weg bij ontwerpen, *?* en *Over* in **Help ▾**, wisselende teksten een vaste breedte, het exportpad met `ClipText` en de tooltip. Controle in de zelftest `ui`: `Shell.LayoutWidth` niet groter dan het venster bij 1600, 1366 en 1280 px. Let op: `FreeRect` alleen meten is niet genoeg, die zag het niet.

### Zelftest eindigt met "Leaked unsafe reference" en "Illegal instruction" — *Gezien 2026-11-24*

**Symptoom**: na `SELFTEST RESULT: PASS` honderden regels `ERROR: Leaked unsafe reference to object` uit `GodotObject.Finalize()` en de exe stopt met *Illegal instruction*.
**Oorzaak**: vermoedelijk: de zelftest bouwde de scène opnieuw (`SetDesignMode(false)`) en stopte in hetzelfde frame, zodat de .NET-finalizers objecten opruimen die Godot al vrijgaf. Eén keer gezien, daarna niet meer.
**Oplossing**: na de laatste scènewissel een paar frames wachten vóór `Quit` (`RunUiToolbarFit` wacht 10 frames). Gebeurt het weer, kijk dan welke stap vlak vóór het einde de scène wisselt.

## Windows-export (plan 32)

### Export: "no solution file was found at …\src\FactorySim.App\FactorySim.App.sln" — *Bevestigd 2026-11-24*

**Symptoom**: `bash tools/export.sh` (of *Project → Export*) geeft per C#-script `ERROR: Export .NET Project: This project contains C# files but no solution file was found` en `res://….cs is a C# file but no solution file exists`. Er komt toch een `FactorySim.exe` en een `.pck`, maar geen map `data_FactorySim.App_windows_x86_64`; zo'n exe start zonder C#.
**Oorzaak**: Godot zoekt de solution naast `project.godot`, met de naam van `dotnet/project/assembly_name` (`FactorySim.App.sln`). De repo had alleen `FactorySim.sln` in de root.
**Oplossing**: `src/FactorySim.App/FactorySim.App.sln` met App en Core en de configuraties `Debug`, `ExportDebug`, `ExportRelease`. Het exportscript stopt nu met `EXPORT FAILED` als Godot een `ERROR` meldt of als de DLL's ontbreken.

### Export: de zip mislukt met "het bestand … FactorySim.exe wordt gebruikt door een ander proces" — *Bevestigd 2026-11-24*

**Symptoom**: `Compress-Archive` in `tools/export.sh` faalt direct na de export.
**Oorzaak**: vlak na het schrijven houdt een ander proces (waarschijnlijk de virusscanner) de nieuwe exe van 105 MB nog even vast.
**Oplossing**: het script probeert de zip tot zes keer, met 10 s ertussen.

### Export: "Invalid version number "0.1.0-beta.1"" — *Bevestigd 2026-11-24*

**Symptoom**: een waarschuwing bij de export; de exe krijgt dan geen versie in *Eigenschappen → Details*.
**Oorzaak**: de versie van een Windows-exe mag alleen cijfers en punten bevatten; zonder `application/file_version` neemt Godot `config/version`.
**Oplossing**: `application/file_version` en `product_version` in `export_presets.cfg` op `0.1.0.1` (beta 1). Bij een nieuwe versie bijwerken; `tools/export.sh` waarschuwt als ze niet bij `config/version` passen.
