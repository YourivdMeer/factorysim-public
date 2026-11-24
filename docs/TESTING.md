# TESTING

Teststrategie voor FactorySim. Er bestaat nog geen code; dit document legt vast hoe er getest gaat worden. Commando's werken vanaf de mijlpaal die erbij staat.

## Drie lagen

| Laag | Wat | Waarmee | Automatisch? | Vanaf |
|------|-----|---------|:------------:|-------|
| **Unit** | Core-logica: `Tag`, `TagStore`, `TagMap`, `SimController`; later parser en interpreter van spoor B | xUnit, `dotnet test` | Ja | M2 |
| **Integratie** | `ModbusTcpLink` tegen de Python fake-PLC: echo, sorteerscenario op tag-niveau, reconnect | xUnit (`Category=Integration`) + `tools/fake_plc` | Ja | M3 |
| **End-to-end** | App + echte PLC: CODESYS Control Win V3 (spoor A); `.exp` op de scène (spoor B) | Acceptatietest-script hieronder | Handmatig | M4 |

Daarnaast is er de **zelftest van de scène** (sinds M1): de app draait zichzelf zonder toekijken en meldt PASS of FAIL. Dat dekt het physics-gedrag. Wat **niet** automatisch getest wordt is de UI en de toetsbediening; die loop je handmatig na volgens het acceptatietest-script hieronder.

## Zelftest van de scène

Godot 4.7.2 .NET staat in een eigen map buiten de repo; `$godot` in de commando's is het pad naar de exe. Gebruik de `_console.exe` zodat de uitvoer in de terminal komt. Eerst builden met `dotnet build FactorySim.sln`.

```
$godot = '<pad>\Godot_v4.7.2-stable_mono_win64_console.exe'
& $godot --headless --path src/FactorySim.App -- --selftest=sort:20     # 20 dozen sorteren op hoogte (~1 minuut)
& $godot --headless --path src/FactorySim.App -- --selftest=soak:600    # 10 minuten onbeheerd, zonder pusher
& $godot --path src/FactorySim.App -- --selftest=sort:6 --screenshot=C:\pad\beeld.png   # met venster + schermafbeelding
```

| Modus | Wat het doet | PASS als |
|-------|--------------|----------|
| `sort:N` | Band en emitter aan; sorteert met dezelfde sensorlogica als een PLC-programma (hoge doos voor de pusher → band stil → duwen → terug → band aan) tot N dozen verwijderd zijn | Alle N verwijderd, geen hoge doos aan het einde van de band, geen lage in de zijbaan, geen doos gevallen |
| `soak:S` | Band en emitter aan gedurende S seconden, pusher blijft terug | Geen doos gevallen, niets in de zijbaan, nooit meer dan 6 dozen tegelijk in de scène (geen opstopping) |
| `controls` | 6 s draaien, 2 s pauze, 2 s hervatten, reset; dan de simulatie-tags (taak 14) aan via het scènemenu, Run, Pause, Run, Reset (± 16 s, echte tijd) | Tijdens pauze beweegt geen doos; na hervatten wel; na reset is de scène leeg, staan de tellers op nul en is de toestand Stopped. De acceptatiescène heeft `Sim paused` en `Sim reset` niet; aangezet staan ze op coil 7 en 8 (`Sim running` blijft op 6); bij Run is `Sim running` aan en `Sim paused` uit, bij Pause andersom; na Reset is `Sim reset` 1 s ± 0,1 s hoog (echte tijd) en blijft `Sim paused` uit |
| `plc:N` met `--plc-port=P` | Verbindt via Modbus TCP met een server op `127.0.0.1:P`, geeft een Start-puls en laat de PLC sorteren tot N dozen verwijderd zijn; daarna Stop | Minstens N verwijderd, geen hoge doos aan het einde, geen lage in de zijbaan, geen doos gevallen, 0 fouten op de koppeling |
| `plc:N` met `--test-plc` (plan 29) | Als hierboven, maar met de ingebouwde test-PLC: de zelftest drukt de knop *Ingebouwde test-PLC* in het PLC-venster; geen Python nodig | Als hierboven, en de test-PLC heeft zelf dozen gesorteerd; na *Verbreken* is de test-PLC weg en antwoordt de poort niet meer |
| `editor` | Scène-editor: plaatsen, draaien, verplaatsen, verwijderen, opslaan/openen, exporteren/importeren, een fout bestand weigeren, automatisch opslaan in een tijdelijke map (een kopie na een wijziging, geen zonder, de nieuwste 3 blijven, herstellen, kopieën weg na *Opslaan*), en de ontworpen lijn 12 s draaien (± 14 s) | Alle bewerkingen kloppen, de bestanden komen identiek terug, automatisch opslaan staat uit in elke zelftest behalve hier, de herstelde scène is gelijk aan de nieuwste kopie, en dozen bereiken de remover zonder te vallen; scènegalerij (taak 20): twee scènes in een tijdelijke map, lijst met aantallen en beschrijving, openen via de kaart, kapot bestand met melding, schermafbeelding `_gallery` |
| `line` | Bouwt met de editor emitter → band → bocht → band → stopblad → remover en draait die 30 s: eerst met de stop omhoog, dan omlaag | Stop omhoog: niets bij de remover en minstens 3 dozen in de rij; stop omlaag: minstens 4 aangekomen; niets gevallen |
| `panel` | Bouwt met de editor een bedienpaneel: drukknop, noodstop, keuzeschakelaar, signaallamp, signaalzuil, waarschuwingslamp, potentiometer, digitaal display, sirene (± 3 s) | Tags aanwezig (ook de analoge); in rust alleen de noodstop `TRUE` (NC); indrukken, loslaten en omschakelen geven de juiste sensorwaarden; elke lamp volgt alleen zijn eigen tag; een klik op de schermpositie van de knop bedient hem en loslaten elders laat hem los; de potentiometer geeft 4 stappen als 200 en na een klik rechts van de knop 250; het display toont `1234` en `-56` van de PLC; de sirene volgt haar tag; reset zet alles in rust (knop op 0, display 0, sirene uit) |
| `timescale` | Het sorteerprogramma van `sort:20` op ×4 (20 dozen) en op ×0,25 (4 dozen); dan pauze en 10 keer *Stap*; dan reset (± 100 s headless) | Per schaal dezelfde uitslag als `sort:20` (laag naar het einde, hoog naar de zijbaan, niets gevallen); echte tijd / simulatietijd tussen 0,15 en 0,5 bij ×4 en tussen 2,5 en 6 bij ×0,25, gemeten met een `Stopwatch`; de 10 stappen zetten de simulatietijd precies 10/120 s verder en de wereld liep precies 10 ticks; na de reset schaal ×1, tijd 0 en 120 ticks per seconde. Haalt het venster gemiddeld geen 60 fps, dan wordt de timing niet beoordeeld (regel `timing niet beoordeeld`); de rest blijft gelden |
| `trace` | Opent het tabblad *Trace* en sorteert 10 dozen met het programma van `sort:20`; exporteert de trace; dan 30 s simulatietijd zonder en 30 s met opname (band en emitter aan); dan reset (± 75 s headless) | Sortering als bij `sort:N`; evenveel opgaande flanken van `Pusher` als hoge dozen; vóór elke flank een opgaande flank van `At pusher`, 0–1 s eerder; `trace.csv` heeft de kop `Time;Tag;Value;Forced`, oplopende tijden en precies dezelfde flanken van `Pusher`; fps met en zonder opname minder dan 5 % verschil (alleen met venster en gemiddeld ≥ 60 fps, anders alleen gelogd); na de reset is de trace leeg. Met `--screenshot` een beeld van het tabblad (`_trace`) |
| `stats` | Bouwt een band van 4 m (tag *Conveyor*) met een emitter op x = −1,75 (interval 3 s) en een remover op x = 2,25; opwarmen tot het eerste item weg is, dan de cijfers op nul en 120 s simulatietijd meten; dan 60 s de band om de 5 s aan en uit; dan reset (± 190 s simulatietijd) | Bij het opwarmen gelijk aan de tellers van emitter en remover; doorvoer 20/min ± 5 % (ook het totaal en de laatste 60 s); doorlooptijd binnen 10 % van (ingang remover − emitter) ÷ 0,6 m/s = 5,92 s; niets gevallen; aan-tijd van de band 50 % ± 2 %; tabblad *Cijfers* met 1 remover en 1 aangedreven onderdeel, `stats.csv` met kop en 5 regels; na reset alles op nul en de CSV met 3 regels. Met `--screenshot` een beeld van het tabblad (`_stats`) |
| `bidir` | Bouwt een band van 4 m met *Twee richtingen*, een emitter (één doos), een photo-eye op x = −1,0 en x = 1,5, en een tweede band met een bistabiele pusher; de PLC laat de doos drie keer heen en weer rijden; dan beide uitgangen van de band 1 s aan; dan een puls van 0,2 s op elke spoel van de pusher en beide spoelen 1 s aan; dan reset (± 32 s simulatietijd) | Tags `Conveyor`, `Conveyor reverse`, `Pusher`, `Pusher retract`; drie rondes zonder vallen; met beide uitgangen aan beweegt de doos minder dan 1 cm in 1 s, de band staat stil en de statusbalk meldt het; na de puls op uit is `front` aan en blijft aan, na de puls op in is `back` aan; met beide spoelen aan blijft de pusher op 0 m, `Conflict` en de melding staan aan; na reset band stil, pusher achteraan, geen melding. Met `--screenshot` een beeld (`_bidir`) |
| `blocks` | Opent de MVP-scène, bewaart het pusherstation (11 onderdelen) als bouwblok in een tijdelijke map, plaatst het twee keer in een lege scène (één keer 180° gedraaid) en zet het voorbeeld *Pusherstation* met een emitter en twee removers in een lege scène; 10 dozen met het programma van `sort:20` (± 35 s) | Knop *Bewaar als bouwblok…* alleen bij een groep; library: *Invoer*, *Pusherstation*, *Sorteerpunt*, dan het eigen blok; twee keer geplaatst: 22 unieke namen, geen tagprobleem, geen overlapmarkering, één stap ongedaan maken per plaatsen; het voorbeeld zet de pusher op dezelfde plek als de MVP-scène; 10 van 10 weggehaald, geen hoge doos aan het eind, geen lage in de zijbaan, niets gevallen |
| `snap` | Bouwt met de editor band → bocht → band: de bocht met de verkeerde draaiing ver weg geplaatst en bij het eind van de eerste band gesleept, de tweede band uit de library bij het eind van de bocht; ook slepen zonder klikken (`Alt`) en ongedaan maken; daarna 10 lage dozen over de lijn (± 36 s) | Zonder klikken: op het raster en niet gedraaid; met klikken: bocht op (−2, −1) en 270° gedraaid, band op (−1, −2); de einden vallen op 1 mm samen; slepen en klikken is één stap voor ongedaan maken; geen overlapmarkering; 10 dozen bij de remover, niets gevallen |
| `decor` | Zet bordes, schakelkast, bedieningszuil, trap, leuning en een 90° gedraaid veiligheidshek op een rij; een emitter (één lage doos) boven het bordes (1,5 m), de kast (1,6 m), de zuil (1,5 m) en de trap (1,8 m), en een band van 2 m met emitter die tegen het hek loopt (± 8 s simulatietijd) | Vijf dozen gemaakt; de onderkant van de doos op het bordes binnen 2 cm van `deck_height` (1,0 m); de dozen op de kast en de zuil komen binnen de maat nooit meer dan 5 cm onder de bovenkant van de botsvorm; de doos op de trap komt nooit onder de lijn van de treden; de doos op de band komt voorbij x = 6,1 maar niet door het hek en blijft boven 0,8 m; geen doos onder 5 cm boven de vloer. Met `--screenshot` een beeld (`_decor`) |
| `tank` | Bouwt met de editor een tank met kleppen van 1,0 m³/s; een tweepuntsregeling op tags (vulklep open onder 0,80 m, dicht boven 1,20 m, afvoer open) draait 25 s; daarna filter *Analoog*, een force en een reset (± 27 s) | Bovengrens gehaald; daarna binnen 5 cm van de grenzen; vulklep ≥ 4 keer omgeschakeld; debiet > 100 l/s gezien; niveautag = niveau van de tank (± 2 cm); tag-tabel met *Analoog* toont precies de vier tags, de I/O-lijst 2 + 2; een geforceerd niveau (50) is wat de PLC leest; na de reset is de tank leeg en zijn de kleppen dicht |
| `sorters` | Bouwt met de editor twee lijnen met een rechte uitgang en een zijbaan: wielsorteerder (rechtdoor, dan links) en zwenkarm (terug, dan uit; de lijn loopt eerst leeg voor de arm zwenkt) (± 66 s) | Per fase komen de dozen alleen bij de juiste remover aan (minstens 2), niets gevallen, arm in de juiste eindstand |
| `heavy` | Bouwt met de editor twee rollenlijnen met een rechte uitgang en een zijbaan: draaitafel en kettingovername. Een PLC-volgorde op tags (sensor in het midden, eindstanden) stuurt ze eerst rechtdoor en dan naar de zijbaan (± 126 s) | Per fase komen de dozen alleen bij de juiste remover aan (minstens 2), niets gevallen, eindstand `at 0` klopt |
| `station` | Bouwt met de editor twee banden onder een pick & place-station (eerst 2 assen, dan 3) en tilt de dozen over met een PLC-volgorde op tags; tussendoor een reset (± 91 s) | Per station minstens 4 dozen overgezet en bij de remover aangekomen, niets gevallen; na de reset houdt de grijper niets vast en staat hij in rust |
| `lift` | Bouwt met de editor een rollenbaan, een lift en een rollenbaan met remover 3 m hoger; een PLC-volgorde op tags brengt de dozen naar boven; daarna een reset (± 91 s) | Eindstanden kloppen, de doos gaat mee omhoog, minstens 3 dozen bij de remover boven, niets gevallen; na de reset staat de lift beneden |
| `palletizer` | Bouwt met de editor een palletiseerder met een pallet-aanvoer op rollen, een dozenband op 1,9 m en een remover; een PLC-volgorde op tags stapelt twee lagen van zes dozen en voert de pallet af; daarna een reset (± 71 s) | 12 dozen rechtop binnen de omtrek van de pallet; 12 dozen en 1 pallet bij de remover, niets gevallen; na de reset staat alles in rust |
| `machining` | Bouwt met de editor een bewerkingscentrum met een invoerband met grondstof-emitter en een uitvoerband met remover; een PLC-volgorde op tags laat vier onderdelen bewerken (om en om basis en deksel) en houdt tijdens de tweede cyclus de uitvoerband stil; daarna een reset (± 75 s) | In rust niet bezig; de robot wacht boven de bezette uitvoer en zet dan pas neer; 4 onderdelen klaar, 2 basissen en 2 deksels; 4 producten bij de remover, niets gevallen; na de reset is de cel leeg en vrij |
| `crane` | Bouwt met de editor een magazijnkraan, een stelling, een rollenbaan met pallet-emitter (*in*) en een rollenbaan met remover (*uit*); een PLC-volgorde op tags zet drie pallets in vak 1, 5 en 12 en haalt vak 5 eruit; dan `go` naar vak 15 en `go` met de vorken uit; reset; daarna jog-modus naar kolom 2, niveau 3 (± 90 s) | Pallets rechtop binnen 5 cm van hun vak; vak 5 komt bij de remover aan (1 pallet geteld, 2 blijven in de stelling); `error` bij een ongeldig doel en bij `go` met de vorken uit, de kraan beweegt dan niet; geen rijden met de vorken uit; jog-sensoren kloppen; na de reset staat de kraan in rust en is de scène leeg |
| `robot` | Bouwt met de editor een robotarm op een sokkel tussen twee banden, een grondstof-emitter, een stopblad met sensor en een remover; leert vier posities via *Op punt* en *Nieuw*, slaat de scène op en opent het; een PLC-volgorde op tags brengt vier items van band 1 naar band 2; `go` naar positie 9; reset (± 60 s) | Vier posities geleerd en na openen gelijk; punt buiten bereik geweigerd; home niet te wijzigen; 4 items bij de remover, niets gevallen; item in de zuignap halverwege; `error` bij positie 9 zonder beweging; na de reset staat de arm in home en is de scène leeg |
| `assembly` | Eerst een emitter die op *Stapelbak* staat naast een remover (10 s). Dan opent hij het voorbeeldscène `assembly_line.fsscene.json`: twee emitters (deksel en basis), een pick & place met drie assen, een stopblad, positioneerbalken, twee sensoren en een remover; een PLC-volgorde op tags (aanvoer, klemmen, pakken, neerzetten, loslaten, klemmen open, stop omlaag) maakt drie eindproducten (± 45 s) | Alleen stapelbakken bij de remover; drie cycli, drie eindproducten, die alle drie de remover bereiken (geen losse basis of deksel voorbij het stopblad), niets gevallen |
| `sensors` | Bouwt met de editor één scène met vijf rijen: blauwe en metalen grondstof langs een capacitieve en een inductieve sensor; reflectiesensor met reflector en een zonder; lichtscherm; encoder op een lopende band; veiligheidsdeur. De emitters stoppen na 28 s zodat de banden leeglopen (± 45 s) | Elke tag telt als een PLC de flanken: capacitief = alle items, inductief = alleen de metalen, reflectiesensor en lichtscherm = alle dozen, sensor zonder reflector 0, encoder = afstand / pulsafstand (± 2) en B laag bij elke flank van A; de vision-sensor in rij 1 geeft precies de nummers 9 (grondstof blauw) en 17 (metaal) en zijn bit `detected`; het lichtscherm telt 1–8 onderbroken stralen; waarschuwing voor de sensor zonder partner; de deur is dicht in rust, open na een klik (≥ 89°) en weer dicht na de tweede |
| `transport` | Bouwt met de editor twee scènes met rijen naast elkaar, elk met een eigen emitter en remover. Scène 1: band, aftakband, weegband en band met doorgang; omhoog over de hellende band naar 1,8 m (hoge dozen); omlaag met de instelling *Omlaag*; pallets omhoog. Scène 2: glijgoot; lage glijgoot; uitlijner (dozen starten 25 cm naast het midden); hoekaanslag; pallets over drie vrije rollen; positioneerbalken met een PLC-volgorde op tags (sensor, band, klemmen); pallets over een rollenbocht; een rollenstop die 28 s omhoog staat (± 100 s) | Scène 1: per rij minstens 4 dozen (bij de pallets 3) aangekomen, niets gevallen, alles rechtop; de weegband geeft een gewicht boven 0. Scène 2: minstens 3 dozen over elke glijgoot, uitlijner levert dozen binnen 3 cm van het midden af, hoekaanslag laat niets door en houdt minstens 2 dozen vast, minstens 2 pallets over de vrije rollen, minstens 2 dozen geklemd en op 1 cm van het midden waarna ze doorrijden, minstens 3 pallets over de rollenbocht, een pallet tegen de rollenstop voordat hij omlaag gaat en daarna minstens 1 erdoor, niets op de vloer |
| `api` met `--plc-port=P` | Zet de test-API aan op een vrije poort en start `tools/api/run_test.py` als subprocess met het token, de poort en `--seconds 40`; het script opent `mvp_sorting`, verbindt via de API met de fake-PLC op `127.0.0.1:P`, zet de simulatie aan, geeft een Start-puls en leest de tellers; daarna zet de zelftest de API uit (± 45 s) | Server op 127.0.0.1, token van 32 tekens, het PLC-venster meldt "Luistert op 127.0.0.1:…"; het script eindigt met exitcode 0; `mvp_sorting` geopend, dozen in beide removers, geen hoge aan het einde en geen lage in de zijbaan; daarna gepauzeerd en niet verbonden; de poort is vrij na het uitzetten en het venster meldt "Uit". Het proces wordt elk frame bekeken, niet afgewacht: de main thread moet de wachtrij van de API blijven legen |
| `ui` | Loopt de schermindeling en de editorhulpen na via dezelfde bewerkingen als de knoppen (± 3 s) | 3D-beeld vrij in beide modi; tags gegroepeerd per onderdeel; I/O-lijst compleet (ingangen + uitgangen = alle tags); filter, zoeken en force; library zoeken en categorie; instellingen per onderdeel (bij een emitter ook de lijst *Item* met keuze en undo/redo); undo/redo; groepen selecteren en bewerken, overlapmarkeringen, de vloermaat, tag aanwijzen en de GVL-export; instellingen (taak 15) naar een tijdelijk bestand en terug: PLC-venster op 5020, camera op de opgeslagen stand, de camera van een andere scène niet, een kapotte poort valt terug op 502 en de rest blijft, en de zelftest raakt `settings.cfg` niet; kopiëren en plakken; overlapwaarschuwing komt en gaat; onderstel van 8 balken onder een verhoogde band; camera richt op het midden van het vrije deel; bordjes (taak 19): vloertekst en bordje met é en regeleinde, undo, opslaan en openen, dupliceren, labels aan beide kanten, geen botsvorm onder de vloertekst, schermafbeelding `_signs`; app-icoon (plan 25): `res://branding/icon.svg` laadt als 512 × 512; versie en *Over* (plan 28): titelbalk met versie en scènenaam en die volgt een nieuwe naam, *Over* met versie, licentie-, netwerk- en merkzin, drie mappen als volledig pad, *Map openen* geeft het pad van zijn rij (met een opnemende opener, er opent geen Verkenner); welkomstvenster (plan 30): niet bij de start van een zelftest, de regel voor tonen met een tijdelijk instellingenbestand (lege map ja, oud bestand nee, na een eerste start ja, na *Niet meer tonen* nee), **Aan de slag** opent het, *Niet meer tonen* sluit het en zet `welcome.show` uit; crashmelding (plan 31): een gemaakte crash schrijft `crash-….txt` in de logmap van *Over* en de opgenomen melding noemt pad en versie, een exception in een uitgestelde aanroep komt in de statusregel en **Details** opent `godot.log` (opnemende opener); test-PLC (plan 34): in `assembly_line` is de knop **Ingebouwde test-PLC** uit met een tooltip die de MVP-scène noemt, een druk vanuit code start niets, in de MVP-scène is hij aan; daarna komt de scène van ervoor terug; werkbalk (plan 35): **Help ▾** opent *Aan de slag* en *Over*, en bij een venster van 1600, 1366 en 1280 px is de layout in beide modi niet breder dan het venster en staan bij ontwerpen beide zijpanelen in beeld (de zelftest zet het venster daarna terug op 1600 × 900) |

De modus `plc` vraagt een draaiende PLC. Met de fake-PLC (dit is de acceptatietest van M3):

```
py tools/fake_plc/fake_plc.py --mode sorter --port 5020          # in een eigen terminal
& $godot --headless --path src/FactorySim.App -- --selftest=plc:50 --plc-port=5020
```

Dezelfde modus werkt tegen CODESYS Control Win V3 met `--plc-port=502` (M4). Zonder Python, met de ingebouwde test-PLC (plan 29; `regress.sh` draait hem ook):

```
& $godot --headless --path src/FactorySim.App -- --selftest=plc:50 --test-plc
```

De modus `api` vraagt dezelfde draaiende fake-PLC; `tools/regress.sh` draait hem direct na `plc:50`, tegen dezelfde fake-PLC:

```
& $godot --headless --path src/FactorySim.App -- --selftest=api --plc-port=5020
```

Sinds M2 stuurt de zelftest de scène uitsluitend via tags aan, met dezelfde aanroepen als een PLC-link. Hij test dus ook de tag-laag en de volgorde binnen een tick.

De uitvoer bestaat uit regels die met `SELFTEST` beginnen; de exitcode is 0 bij PASS en 1 bij FAIL. `--headless` draait zonder venster; de physics is dezelfde. De reeks laag/hoog is reproduceerbaar (seed 1).

## Tests draaien

Vanuit de repo-root:

```
dotnet test FactorySim.sln                                   # alles
dotnet test FactorySim.sln --filter "Category!=Integration"  # alleen unit (snel, geen Python nodig)
dotnet test FactorySim.sln --filter "Category=Integration"   # alleen integratie
```

De tests met `Category=RealExports` (spoor B, B0) lezen echte V2.3-exports in `testdata/v23-exp/echt/`. Die map staat niet in git; zonder die map slagen ze zonder iets te controleren. Alleen die tests: `--filter "Category=RealExports"`.

Integratietests starten de fake-PLC zelf als subprocess op een vrije poort en stoppen hem na afloop. Ze hebben alleen de `py`-launcher met Python 3.9 of hoger nodig; de fake-PLC gebruikt uitsluitend de standaardbibliotheek, dus er valt niets te installeren.

De latency-meting is een van de integratietests. De cijfers verschijnen met:

```
dotnet test tests/FactorySim.Core.Tests --filter "Category=Integration" --logger "console;verbosity=detailed"
```

Fake-PLC los starten (bijv. om de app eraan te koppelen):

```
py tools/fake_plc/fake_plc.py --mode sorter --port 5020
```

| Modus | Gedrag |
|-------|--------|
| `echo` | Elke coil komt ongewijzigd terug als discrete input met dezelfde offset |
| `sorter` | Dezelfde start/stop- en sorteerlogica als het CODESYS-acceptatieprogramma, als scan-lus van 10 ms |

De fake-PLC bindt alleen op `127.0.0.1` (zie [SECURITY.md](SECURITY.md)).

## Werkwijze

1. Nieuwe Core-functionaliteit: eerst een falende test, dan de implementatie.
2. Bugfix: eerst een test die de bug reproduceert.
3. Vóór elke commit: `dotnet build` en de unit tests groen. Vóór het afsluiten van een mijlpaal: alle tests groen plus de acceptatietest.
4. Timing-gevoelige tests gebruiken ruime marges en tellen gedrag (juiste uitgang, juiste volgorde), geen exacte milliseconden.

## Acceptatietest-script spoor A (M4)

Voorbereiding:
- CODESYS-project opgezet volgens `codesys/README.md`; applicatie in RUN in CODESYS Control Win V3; MainTask 10 ms.
- App gestart, MVP-scène geladen, verbonden met `127.0.0.1:502`, Unit ID 0 of 255.

| # | Stap | Verwacht | OK? |
|---|------|----------|:---:|
| 1 | Connect | Status groen; tag table toont live waarden | ☐ |
| 2 | Run, daarna Start | Emitter spawnt; band loopt; lamp aan | ☐ |
| 3 | 50 dozen laten doorlopen | Alle hoge dozen in de zijbaan, alle lage aan het einde; tellers kloppen; geen vastgelopen dozen | ☐ |
| 4 | Stop indrukken tijdens transport | Band staat binnen 100 ms stil (cycle-statistiek in de app) | ☐ |
| 5 | Start | Proces loopt verder zonder Reset | ☐ |
| 6 | Pause, 10 s wachten, Resume | Scène gaat verder; geen foutmelding in het PLC-programma | ☐ |
| 7 | Reset | Scène leeg; actuatoren in rust | ☐ |
| 8 | In CODESYS: Stop PLC, daarna Start PLC + login/start | App toont *Disconnected* en herverbindt vanzelf | ☐ |
| 9 | 30 minuten laten draaien | Geen crash; ≥ 60 FPS; geen opstopping | ☐ |

Vastleggen bij elke uitvoering: datum, app-commit, CODESYS-versie, task cycle, gemeten latency (min/gemiddeld/p99), resultaat per stap.

## Testdata

```
testdata/
  v23-exp/
    README.md                  wat elk bestand dekt + verwacht unsupported-rapport
    01_st_basis.exp            ST, GVL, AT-adressen
    02_timers_tellers.exp      TON/TOF/TP, CTU/CTD, R_TRIG/F_TRIG, SR
    03_fb_types.exp            TYPE, FUNCTION, FUNCTION_BLOCK, CASE, FOR, array
    04_sorteerscene.exp        volledig acceptatieprogramma
    05_niet_ondersteund.exp    bewust niet-ondersteunde constructies
    echt/                      echte exports — NIET in git
```

- De vijf genummerde bestanden zijn **synthetisch**: met de hand geschreven naar het voorbeeld van echte exports, niet door CoDeSys gegenereerd. Ze dienen om parser en runtime op te starten.
- `echt/` staat in `.gitignore`. Elke fase van spoor B heeft een acceptatietest met een echt project uit die map; tot die er zijn geldt de synthetische variant als voorlopige test.
- Tests die `echt/` nodig hebben, worden overgeslagen (niet gefaald) als de map leeg is, en melden dat.

## Teststrategie spoor B (vooruitblik)

- **Parser**: corpus-tests over alle bestanden in `testdata/v23-exp/` — geen crash, onbekende blokken gemeld.
- **Unsupported-rapport**: `05_niet_ondersteund.exp` tegen de verwachte meldingen in de README.
- **Standaard-FB's**: tabel-gestuurde conformance-tests per FB (tijddiagrammen).
- **Determinisme**: twee runs met dezelfde seed geven identieke tag-traces.
- **Trace-vergelijking**: uitgangen per scan vergelijken met een in de V2.3-simulatie opgenomen trace (± 1 scan).
