# ASSETS

3D-assets van de simulator: wat er is, hoe het eruit moet zien, hoe het gebouwd wordt en hoe je er een toevoegt. De assets zijn **alleen visueel**; physics-colliders blijven de eenvoudige primitives uit de code en het simulatiegedrag verandert niet.

_Bijgewerkt: 2026-11-21_

## De asset library in het kort

Sinds 2026-11-16 is er een modulaire library van **102 assets** in negen categorieën: items, light load, heavy load, sensors, operators, stations, warning devices, walkways en simulation. De volledige lijst met maten staat in [LIBRARY.md](LIBRARY.md); hoe je er scènes mee bouwt in [SCENES.md](SCENES.md).

- **Catalogus**: `assets/library/catalog.json` beschrijft per asset de categorie, het modelbestand, de maten, de losse (bewegende) onderdelen, de hoogte van de origin en het gedrag in de simulatie. De app en de editor lezen alleen de catalogus; een asset toevoegen vraagt geen wijziging in de app, tenzij het nieuw gedrag krijgt.
- **Modulair**: alle assets komen uit dezelfde bouwkit (`fslib/kit.py`: frame, poten, band, rollen, kast, buizenframe) en dezelfde maatvoering. Banden zijn 0,8 m breed met de bovenkant op 0,8 m; rollenbanen 1,3 m breed op 0,5 m; lengtes in stappen van 2, 4 en 6 m. Daardoor sluiten onderdelen op elkaar aan en ziet alles er als één familie uit.
- **Generator in modules** (`assets/blender_scripts/`):

| Bestand | Inhoud |
|---------|--------|
| `build_assets.py` | Startpunt: bouwt alles, schrijft de catalogus, thumbnails en contactbladen |
| `fslib/base.py` | Scène, materialen, geometrie, export; de eerste dertien MVP-modellen |
| `fslib/kit.py` | Bouwkit, palet en modulematen |
| `fslib/parts_transport.py` | Items, light load, heavy load |
| `fslib/parts_plant.py` | Sensors, operators, warning devices, walkways, stations |

- **Eigen ontwerpen**: elk model is een eigen ontwerp uit eigen code, met de gangbare soorten onderdelen van een kleine fabriekslijn (banden, sensoren, sorteerders, stations).
- **Gedrag**: 68 assets werken in de simulatie (conveyors, hellende band, sensoren (capacitief, inductief, reflectie, lichtscherm, vision), tank, potentiometer, digitaal display, sirene, encoder, veiligheidsdeur, glijgoten, vrije rollen, uitlijner, hoekaanslag, positioneerbalken, bochten, stops, pusher, diffuse sensor, emitter, removers, rail, drukknoppen, noodstop, keuzeschakelaar, signaallampen, signaalzuil, waarschuwingslamp, wielsorteerder, zwenkarm, kettingovername, draaitafel, pick & place, lift, palletiseerder, bewerkingscentrum, magazijnkraan met stelling, robotarm met 6 assen, emitters voor pallets en grondstof, decorstukken met botsvorm: schakelkast, bedieningszuil, leuning, bordes, trap, veiligheidshek), 20 zijn items, 1 is **alleen visueel** (de RFID-lezer). Stations zijn vereenvoudigd, met de bewegende delen wel als losse objecten voor later.
- **Passieve onderdelen** (gedrag `passive`): de botsvormen staan als `colliders=[collider(...), ...]` bij het asset in het generatorscript en komen in de catalogus. Ze zijn in de assen van Godot (x langs de band, y omhoog, z dwars; Blender (x, y, z) is daar (x, z, −y)) en horen bij het model: pas je het model aan, pas dan ook de botsvormen aan.
- **Bestanden**: `assets/library/<categorie>/<id>.glb`, met een kopie plus palet-thumbnails van 128 px in `src/FactorySim.App/library/`. Per categorie een contactblad in `docs/assets-preview/library_<categorie>.png`.

Een asset toevoegen aan de library: schrijf een bouwfunctie in het passende `parts_*.py` met de stukken uit de kit, zet een regel `asset(id, categorie, naam, bouwfunctie, …)` in de lijst van die module, en draai de generator en de Godot-import. Het verschijnt dan vanzelf in de catalogus en in het palet van de editor.

## Uitgangssituatie

Tot nu toe werd alles in code opgebouwd uit primitives (`BoxMesh`, `PlaneMesh`) in `src/FactorySim.App/Main.cs` en `Parts/*.cs`. Colliders zijn `BoxShape3D`'s met dezelfde maten. Er waren geen modelbestanden.

| Object | Was | Collider (blijft) |
|--------|-----|-------------------|
| Hoofdband | doos 6,0 × 0,2 × 0,8 m + vlak met streeptextuur | doos 6,0 × 0,2 × 0,8, bovenkant op 0,80 m |
| Zijbaan | doos 1,6 × 0,2 × 1,2 m + vlak | doos 1,6 × 0,2 × 1,2, bovenkant op 0,79 m |
| Geleiderails (6 stuks) | dozen 0,04 dik × 0,08 hoog | idem |
| Dozen | doos 0,30 × 0,20 × 0,30 (laag) en 0,30 × 0,35 × 0,30 (hoog) | idem |
| Pusher | kop 0,60 × 0,25 × 0,08 + stang | kop-doos |
| Photo-eyes (2) | blokje + LED + straal | geen (raycast) |
| Emitter | doorzichtig groen volume | geen (area 0,5 × 0,6 × 0,5) |
| Removers (2) | doorzichtig rood volume | geen (area) |
| Vloer | doos 14 × 0,1 × 10 | idem |

## Engine en pijplijn

- **Engine**: Godot 4.7.2. Importeert **glTF 2.0 binair (`.glb`)** als scène; materialen worden `StandardMaterial3D`.
- **Assen**: Godot is Y-omhoog; de glTF-exporter van Blender zet Z-omhoog om. In Blender modelleren we dus zo:

| Blender | Godot | Betekenis |
|---------|-------|-----------|
| +X | +X | looprichting van een band |
| +Z | +Y | omhoog |
| −Y | +Z | duwrichting van de pusher, kijkrichting van een sensor |

- **Bouwen**: `assets/blender_scripts/build_assets.py` maakt alle assets, exporteert ze en rendert per asset een thumbnail. Vanuit de repo-root:

```
& "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" --background --factory-startup --python assets/blender_scripts/build_assets.py
& $godot --headless --path src/FactorySim.App --import
```

  Het script draait in een eigen Blender-proces op de achtergrond en raakt een eventueel open Blender-sessie niet. Het kan ook in een open Blender (Scripting → Run Script, of via de Blender MCP): de eerste keer maakt en toont het zijn eigen scène `FactorySim_Assets`, de tweede keer bouwt het daarin. Na een nieuwe export moet Godot de modellen opnieuw importeren (tweede regel).
- **Bestanden**:

```
assets/
  blender_scripts/build_assets.py     generator — de bron van elk asset
  source/factorysim_assets.blend      Blender-bron (door het script weggeschreven)
  models/*.glb                        export (canoniek)
src/FactorySim.App/models/*.glb       kopie die Godot importeert (door het script gesynchroniseerd)
docs/assets-preview/*.png             thumbnail per asset + voor/na-schermafbeeldingen
```

Godot importeert alleen bestanden binnen het project (`res://`); daarom zet het script een kopie in `src/FactorySim.App/models/`. Godot zet daar bij het importeren `.import`-bestanden naast en pakt de ingebedde texturen uit als losse `.png`'s; die horen ook in git. Alles samen is ongeveer 1,3 MB (modellen 0,6 MB per map, bron 0,15 MB). Wordt één bestand groter dan ~1 MB, dan gaat het naar Git LFS.

- **Laden in de app**: `Parts/Models.cs` laadt `res://models/<naam>.glb` en hangt het model als kind onder de bestaande node. De code zoekt bewegende delen op naam.

## Technische conventies

1. **Eenheden**: 1 eenheid = 1 m; echte maten, gelijk aan de bestaande colliders. Schaal 1,0 op elk object bij export.
2. **Origin** op een logisch punt, gelijk aan de plek waar de code de node zet:

| Asset | Origin |
|-------|--------|
| Conveyor | midden van de bovenkant van de band |
| Doos | midden van de doos (zwaartepunt) |
| Pusher | midden van de duwplaat in ingetrokken stand |
| Photo-eye | de lens (begin van de straal) |
| Geleiderail | midden van de bestaande rail-collider |
| Emitter | op de band, midden onder het spawnpunt |
| Remover | midden van het remover-volume; de ingang wijst naar −X |
| Vloer, sensorpaal | op de vloer |

3. **Visueel ≠ collision**: geen colliders in de `.glb`. Visuele delen mogen niet verder dan ~1 cm buiten de collider steken waar dozen langs komen.
4. **Bewegende delen zijn losse objecten** met een vaste naam (zie de tabel hieronder); de rest van een asset heet `<Asset>_Static`.
5. **Budget**: < 5.000 driehoeken per asset; bevel van één segment voor lichtranden; cilinders 16–24 segmenten.
6. **Materialen**: eenvoudige PBR (base color, metallic, roughness), namen beginnen met `FS_`. Hooguit kleine, door het script gegenereerde texturen (≤ 128 px), ingebed in de `.glb`.
7. **Naamgeving bestanden**: kleine letters met underscores en de maat erin: `conveyor_straight_6m.glb`.
8. **Herkomst**: alleen zelfgemaakt of CC0. Alles hieronder is door het script gegenereerd; er zijn geen externe assets, geen modellen of texturen van Factory I/O.

## Stijlgids

Strak en herkenbaar industrieel, zoals een trainingssimulator: rustige vlakken, weinig kleuren, kleur alleen waar hij iets betekent.

| Materiaal | Kleur (sRGB) | Metallic / roughness | Waarvoor |
|-----------|--------------|----------------------|----------|
| `FS_FrameGrey` | #6B7178 | 0,6 / 0,45 | frames, poten, palen |
| `FS_FrameDark` | #2A2D31 | 0,5 / 0,5 | motoren, behuizingen, voetplaten |
| `FS_Steel` | #C0C3C7 | 1,0 / 0,3 | rollen, zuigerstang, rails |
| `FS_PanelLight` | #C9CDD1 | 0,3 / 0,55 | plaatwerk van emitter en removers |
| `FS_BeltRubber` | donker rubber met streeptextuur | 0 / 0,9 | band |
| `FS_SafetyYellow` | #F2B705 | 0,1 / 0,5 | **alles wat beweegt of gevaar aangeeft**: duwplaat, motorkap, vloerlijnen |
| `FS_Black` | #141517 | 0 / 0,7 | sensorbehuizing, rubber, gordijnstroken |
| `FS_Lens` | #0A0C10 | 0 / 0,1 | lens |
| `FS_Led` | emissief | — | status-LED: rood = detectie, donker = niets (code) |
| `FS_Beam` | rood, doorzichtig | — | sensorstraal |
| `FS_Lamp` | emissief | — | lamp op de emitter: groen = emitter aan (code) |
| `FS_Cardboard` / `FS_CardboardDark` | licht / donker karton met fijne textuur | 0 / 0,85 | lage / hoge doos |
| `FS_Tape` | #B79A6B | 0 / 0,6 | plakband op dozen |
| `FS_Concrete` | grijs beton met fijne textuur | 0 / 0,95 | vloer |

## Assets

| # | Bestand | Losse objecten (naam → wat de code ermee doet) | Gebruikt door |
|---|---------|-----------------------------------------------|---------------|
| 1 | `box_low.glb`, `box_high.glb` | — | `Box` |
| 2 | `conveyor_straight_6m.glb` | `Belt` → UV schuift met de bandsnelheid · `Roller_Head`, `Roller_Tail` → draaien mee | hoofdband |
| 3 | `conveyor_straight_1m6_wide.glb` | idem | zijbaan |
| 4 | `guide_rail_1m.glb`, `rail_bracket.glb` | — (rail wordt in de lengte geschaald) | rails |
| 5 | `pusher.glb` | `PusherPlate`, `PusherRod` → schuiven met de pusher · `PusherBody` → staat stil | `Pusher` |
| 6 | `photo_eye.glb` | `SensorLed` → kleur volgt de sensor · `SensorBeam` → lengte = bereik | `PhotoEye` |
| 7 | `sensor_post.glb` | — | paal voor beide photo-eyes |
| 8 | `emitter_gantry.glb` | `EmitterLamp` → groen als de emitter aan staat | `Emitter` |
| 9 | `remover_hood_1m0.glb`, `remover_hood_1m4.glb` | — | removers |
| 10 | `floor_14x10.glb` | — | vloer |

De doorzichtige groene en rode volumes van emitter en removers vervallen: het portaal en de afvoerkappen laten zien waar dozen verschijnen en verdwijnen.

## Volgorde van werken

1. Pijplijn bewijzen met de doos: bouwen → exporteren → importeren in Godot → maten en assen controleren.
2. Conveyors en rails (band schuift, rollen draaien).
3. Pusher.
4. Photo-eyes en sensorpaal (LED en straal).
5. Emitter-portaal en afvoerkappen.
6. Vloer.

Na elke groep: thumbnails, zelftest van de scène, commit. Aan het eind: alle tests, de acceptatietest met 50 dozen tegen de fake-PLC, en voor/na-schermafbeeldingen met dezelfde camera.

## Controle per asset

- Thumbnail uit Blender in `docs/assets-preview/<naam>.png`.
- Driehoeken < 5.000 (het script meldt het aantal per asset).
- In Godot: staat op de goede plek, steekt nergens door de collider, bewegende delen bewegen met hun tag.
- `dotnet test` groen; `--selftest=sort:20` geslaagd.

## Een nieuw asset toevoegen

1. Schrijf in `assets/blender_scripts/build_assets.py` een functie `build_<naam>(…)` die een collectie `FS_<naam>` vult. Gebruik de hulpfuncties (`add_box`, `add_cylinder`, `material`) en de materialen uit de stijlgids. Modelleer in meters, met de origin volgens de tabel, en de assen volgens de tabel bovenaan.
2. Geef bewegende delen een eigen object met een vaste naam.
3. Voeg het asset toe aan de lijst `ASSETS` onderaan het script en voer het script uit in Blender. Het schrijft de `.glb` naar `assets/models/`, de kopie naar `src/FactorySim.App/models/` en de bron naar `assets/source/`.
4. Render een thumbnail naar `docs/assets-preview/`.
5. Laat Godot het bestand importeren: `& $godot --headless --path src/FactorySim.App --import`.
6. Laad het in de part met `Models.Instantiate("<naam>")`; laat de collider zoals hij is.
7. `dotnet build`, `dotnet test`, zelftest; zet het asset in de tabel hierboven; commit `.glb`, `.glb.import`, script en thumbnail samen.

Het script werkt in een eigen Blender-scène (`FactorySim_Assets`) en raakt andere scènes, objecten of collecties in het open bestand niet aan.

## Robotarm 6 assen (`robot_6axis`)

Eigen ontwerp uit de generator (`_robot_6axis` in `fslib/parts_plant.py`), in de stijl van de library: oranje schakels (`FS_Orange`, zoals de arm van het bewerkingscentrum), donkere asdeksels en motoren, een stalen flens en een blauwe zuignap met balg (zoals bij pick & place), op een grijze sokkel. 3.148 driehoeken.

- **Losse objecten**: `Pedestal`, `Base`, `Link_1` … `Link_6`. Elk is een eigen node in de `.glb`, **niet** aan elkaar gehangen. `Link_i` is het deel dat met as i meedraait; de vork aan het eind van de onderarm hoort bij `Link_4`, het polshuis bij `Link_5`, flens en zuignap bij `Link_6`.
- **Thuisstand**: alle ashoeken 0. De bovenarm staat recht omhoog, de onderarm wijst naar voren langs +X, de pols is omlaag gebogen zodat de grijper recht naar beneden wijst. Zo is elk deel gemodelleerd.
- **Origins**: elk deel heeft zijn origin op het draaipunt van zijn as in de thuisstand, zonder rotatie en met schaal 1. De app zet elk deel elke tick uit de kinematica (`RobotKinematics.Forward`): voet × (draaiing ten opzichte van de thuisstand, om het draaipunt). De posities in de `.glb` doen er daardoor niet toe; alleen de thuisstand en de origins tellen.

| Object | Origin in Godot (x, y, z), voet op de sokkel van 0,75 m | Draait om |
|--------|------------------------------------------------------|-----------|
| `Pedestal` | (0; 0; 0), op de vloer; bovenkant op 0,75 | — de app rekt hem in de hoogte naar de instelling `pedestal` (0 = verborgen) |
| `Base` | (0; 0,75; 0), de voet | staat vast |
| `Link_1` | (0; 0,75; 0) | as 1, verticaal (y) |
| `Link_2` | (0; 1,04; 0) = voet + `baseHeight` | as 2, dwars (z) |
| `Link_3` | (0; 1,31; 0) = as 2 + `upperArm` | as 3, dwars (z) |
| `Link_4` | (0; 1,38; 0) = as 3 + `elbowOffset` | as 4, langs de onderarm (x) |
| `Link_5` | (0,302; 1,38; 0) = polsmidden, `forearm` voor as 4 | as 5, dwars (z) |
| `Link_6` | (0,302; 1,308; 0) = flens, `wristToFlange` onder de pols | as 6, langs de grijper (−y) |

De rand van de zuignap ligt precies `toolLength` (0,050 m) onder het flensvlak. In Blender is het Godot-punt (x, y, z) het punt (x, −z, y).

- **Maten**: de `params` in de catalogus zijn `baseHeight` 0,290, `upperArm` 0,270, `elbowOffset` 0,070, `forearm` 0,302, `wristToFlange` 0,072 en `toolLength` 0,050 m, plus `speed` 40 % en `pedestal` 0,75 m. Ze staan één keer als `ROBOT` in de generator, die ze voor de geometrie en de catalogus gebruikt; een unit test vergelijkt de catalogus met `RobotGeometry`.
- **Ruimte tussen de schakels**: de bovenarm loopt aan de kant van Godot +z langs de schouder; vanaf de elleboog blijft alles aan die kant binnen 3,5 cm van het midden, zodat onderarm en pols langs de bovenarm draaien. Gecontroleerd met renders in Blender: as 3 van −110° tot +70° vrij; de grijper raakt de vork van de onderarm als de pols verder dan ongeveer +60° naar achteren buigt (as 5). De arm heeft geen collider.
- **Bron van de kengetallen**: de maten volgen de openbare productspecificatie van de ABB IRB 120 ([product specification IRB 120, Rev. J](https://library.e.abb.com/public/7aa0711a20fa41c49a8fdf3bbc3d5bb0/IRB120-Rev.J-ROBO149EN_D.pdf)): bereik 580 mm, last 3 kg, asbereiken ±165°, ±110°, −110° tot +70°, ±160°, ±120° en ±400°, snelheden 250, 250, 250, 320, 320 en 420 °/s. Het model zelf is een eigen ontwerp: er zijn geen CAD-modellen, tekeningen, texturen of logo's van ABB gebruikt, en de naam ABB staat niet in het model, de objectnamen, de catalogus of de UI.

## Logo en icoon (plan 25)

Het logo is 2D en staat los van de modellen; de regel over een subagent voor 3D-assets geldt hier niet.

| Bestand | Wat |
|---------|-----|
| `assets/branding/factorysim-icon.svg` | Het icoon, 512 × 512 (ladderlijn, band met doos, photo-eye) |
| `assets/branding/factorysim-icon-small.svg` | Eenvoudige versie voor 16 en 24 px: kader, band en een grotere doos, dikke lijnen |
| `assets/branding/factorysim-logo.svg` | Icoon met de naam, "Factory" donkerblauw |
| `assets/branding/factorysim-logo-light.svg` | Hetzelfde voor op donker: "Factory" licht (`#E8ECF2`) |
| `src/FactorySim.App/branding/icon.svg` | Kopie van het icoon voor `config/icon` (een unit test controleert dat hij gelijk is) |
| `src/FactorySim.App/branding/icon.ico` | 16, 24 (kleine versie), 32, 48, 64, 128 en 256 px voor `config/windows_native_icon` en straks het exe-icoon |
| `src/FactorySim.App/branding/splash.png` | Het lichte logo, 640 px breed op een doorzichtig vlak van 720 × 200, voor `boot_splash/image` |

Pas alleen de SVG's in `assets/branding/` aan, nooit de PNG's of de `.ico`. Daarna:

```
py tools/branding/build_branding.py --preview
& $godot --headless --path src/FactorySim.App --import
```

Het script heeft Inkscape nodig (`C:\Program Files\Inkscapein\inkscape.com`, anders `--inkscape=…`); de app zelf niet. `--preview` zet de renders op 16, 24, 32 en 256 px in `docs/assets-preview/branding_icon_*.png`. De `.import`-bestanden naast `icon.svg` en `splash.png` horen in git. Controle: `BrandingFilesTests` (unit) en de zelftest `ui` (het icoon laadt als 512 × 512).

## Stand en afwijkingen (2026-11-15)

Alle scène-objecten gebruiken de nieuwe modellen. Driehoeken per asset (budget 5.000):

| Asset | Driehoeken | | Asset | Driehoeken |
|-------|-----------:|-|-------|-----------:|
| `conveyor_straight_6m` | 1.852 | | `remover_hood_1m4` | 924 |
| `conveyor_straight_1m6_wide` | 1.412 | | `remover_hood_1m0` | 748 |
| `pusher` | 1.008 | | `emitter_gantry` | 588 |
| `photo_eye` | 312 | | `box_low`, `box_high` | 176 |
| `rail_bracket`, `sensor_post` | 132 | | `floor_14x10` | 60 |
| `guide_rail_1m` | 56 | | | |

Afwijkingen van de opdracht:
- **Thumbnails** zijn door het script zelf gerenderd (`render_thumbnail`, EEVEE, 512 px) en niet met de MCP-tool `render_thumbnail_to_path`, omdat de verbinding met de Blender MCP halverwege wegviel (zie `TROUBLESHOOTING.md`). De eerste bouw en de verbindingstest liepen wel via de MCP.
- **Eén commit per groep** in plaats van per asset: de generator met alle modellen, daarna de integratie in de scène. De assets komen uit één script en zijn samen getest.
- **Modellen op twee plekken**: `assets/models/` (zoals gevraagd) en een kopie in het Godot-project, omdat Godot alleen binnen `res://` importeert.
- **Colliders** zijn niet aangepast. Twee plekken waar de visuele vorm bewust kleiner is dan de collider: de band is aan de uiteinden afgerond (de collider is daar 6 cm langer en recht), en de geleiderail is 1,6 cm dik tegenover 4 cm.
- **Afvoerkappen** hebben een tunnel van 42 cm over het einde van de band. Dozen worden verwijderd zodra ze het remover-volume raken; zonder tunnel zou dat zichtbaar vóór de kap gebeuren.

Gecontroleerd in de app: schermafbeeldingen `scene_before.png`, `scene_after.png` (zelfde camera) en `scene_after_push.png` (pusher uitgeschoven, stang zichtbaar, sensortags aan). Dat de band schuift is afgeleid uit het verschil tussen twee frames 0,13 s na elkaar, niet met het oog bekeken. De status-LED's zijn op deze zoomafstand te klein om op de schermafbeelding te beoordelen.
