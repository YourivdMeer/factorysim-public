# SECURITY

Dit is een lokale oefentool, geen product voor een fabrieksnetwerk. De regels hieronder houden het zo.

## Netwerk

**Modbus TCP heeft geen authenticatie en geen versleuteling.** Iedereen die de poort kan bereiken, kan ingangen en uitgangen lezen en schrijven.

- **Standaard alleen localhost.** De app verbindt standaard met `127.0.0.1`. De Python fake-PLC en de ingebouwde test-PLC (plan 29) binden uitsluitend op `127.0.0.1`; de test-PLC kiest een vrije poort en luistert alleen terwijl hij aan staat.
- **Niet-loopback host = bewuste keuze.** Vult iemand een ander adres in, dan toont de app een waarschuwing en moet dat bevestigd worden.
- **Geen automatisch verbinden.** De app onthoudt host, poort en unit-ID (`settings.cfg`, sinds taak 15), maar verbindt bij het starten nooit vanzelf. Verbinden blijft een klik, en een opgeslagen host die niet `127.0.0.1` is geeft bij *Verbinden* dezelfde waarschuwing.
- **CODESYS Control Win** luistert met het Modbus TCP Slave Device op de netwerkadapters van de pc. Sta in Windows Firewall geen toegang toe voor openbare netwerken en zet poort 502 nooit open naar buiten.
- **Nooit koppelen aan een echte installatie.** De app is bedoeld voor een SoftPLC op dezelfde pc. Verbinden met een PLC die echte machines aanstuurt is buiten scope en gevaarlijk.
- **OPC UA** (latere feature): anonieme toegang alleen op localhost; de security-instellingen van de runtime niet verder openzetten dan nodig.
- Geen telemetrie, geen updates op de achtergrond, geen uitgaande verbindingen behalve de ingestelde PLC-link.

## Test-API

Sinds taak 21 kan de app een kleine HTTP-server starten, zodat een script op deze pc de simulatie kan besturen (endpoints in [SCENES.md](SCENES.md#test-api)). Een open poort is ook op localhost een risico; daarom:

- **Standaard uit.** Aan met *Test-API* in het PLC-venster of met `--api-port=…` bij het starten. Standaardpoort 5080. De app onthoudt niet dat hij aan stond.
- **Alleen `127.0.0.1`.** De server bindt op het loopback-adres (een integratietest controleert het gebonden adres) en is vanaf een andere pc niet bereikbaar.
- **Een token per start.** 32 willekeurige tekens uit een cryptografische generator, nieuw bij elke start; mee te sturen in de header `X-FactorySim-Token`, in constante tijd vergeleken. Zonder of met een fout token: `401`. Het token staat in het PLC-venster en in `%APPDATA%\Godot\app_userdata\FactorySim\api_token.txt` (weg bij uitzetten en bij normaal afsluiten; na een harde crash blijft het bestand staan, maar dat token werkt dan niet meer).
- **Geen CORS-headers, en `Origin` wordt geweigerd.** Een webpagina kan geen aanvraag met een eigen header sturen zonder preflight, en die preflight (`OPTIONS`) krijgt `401` zonder `Access-Control-*`. Een aanvraag met een `Origin`-header (die een browser meestuurt bij een aanvraag vanaf een andere site) krijgt `403`. Zo kan een website in je browser de simulatie niet aansturen.
- **Tegen DNS-rebinding**: de `Host`-header moet `127.0.0.1`, `localhost` of `[::1]` zijn, anders `403`.
- **Geen vrije paden.** Een scène openen gaat op naam, alleen uit de voorbeeldmap en de eigen scènemap; een naam met `/`, `\`, `..` of een schijfletter wordt geweigerd (`400`). De PLC-koppeling via de API verbindt alleen met `127.0.0.1`; een veld `host` wordt geweigerd.
- **Grenzen**: kop hoogstens 8 KB, body hoogstens 64 KB, geen chunked bodies, één aanvraag per verbinding, hoogstens 8 tegelijk, 3 s time-out. Alleen de .NET-standaardbibliotheek (`TcpListener`, `System.Text.Json`), geen extra pakketten.
- **Main thread**: opdrachten die de scène raken gaan via een wachtrij naar de main thread, met een time-out van 5 s (`504`); de server-thread roept Godot nooit rechtstreeks aan.

Getest: de integratietests in `TestApiServerIntegrationTests` (token, gebonden adres, `Origin`, `Host`, 404/400/405/413/504, poort vrij na stoppen) en met de hand met `curl` (2026-11-23: geen token 401, fout token 401, `Origin` 403, vreemde `Host` 403, pad als scènenaam 400, `OPTIONS` 401 zonder CORS-headers). Niet te testen zonder tweede netwerk: dat een andere pc er echt niet bij kan.

## Wat niet in de repo komt

| Niet committen | Waarom | Hoe afgedwongen |
|----------------|--------|-----------------|
| Echte school- of bedrijfsprojecten: `.pro`, `.project`, `.exp`, `.export`, PLCopen XML | Eigendom van school of werkgever; kan vertrouwelijk zijn | `testdata/v23-exp/echt/` en binaire projectformaten staan in `.gitignore` |
| Assets van Factory I/O: modellen, texturen, geluiden, scènes, schermafbeeldingen | Auteursrecht Real Games | Alleen eigen primitives en eigen modellen gebruiken |
| Code uit CODESYS, uit CODESYS-libraries of uit de Factory I/O-tutorials | Auteursrecht en licentie | Eigen ST-voorbeelden; standaard-FB's zelf geïmplementeerd volgens de IEC-norm |
| Wachtwoorden, device-user-gegevens, licentiesleutels | Vanzelfsprekend | Niet in bestanden zetten; `.env` staat in `.gitignore` |

Controle vóór elke commit: `git status` bekijken en niets uit `echt/` of met een projectextensie toevoegen. Staat er toch iets in de geschiedenis, meld het en herschrijf de geschiedenis pas na overleg.

## Licentiegrenzen

### CODESYS
- Binaire projectbestanden (`.project`, `.pro`) worden **niet** geparsed. Ze zijn volgens de fabrikant niet leesbaar buiten de ontwikkelomgeving en de EULA verbiedt reverse engineering.
- Wat wél mag en gebruikt wordt: de door CODESYS zelf aangeboden uitvoer en koppelvlakken — Modbus TCP, OPC UA, de scripting API en de tekstexport `.exp` van V2.3 van je eigen project.
- CODESYS Control Win V3 draait zonder licentie in demo-modus (twee uur per sessie). Die beperking wordt niet omzeild; herstarten is de bedoelde weg.
- `Standard.lib` en andere 3S-libraries worden niet geopend, gekopieerd of meegeleverd.
- Dit is een eigen lezing van de voorwaarden, geen juridisch advies. Bij publicatie van de app: de EULA van de eigen installatie nalezen.

### Factory I/O
- Geen assets, geen code, geen overgenomen teksten. Alleen het gedocumenteerde gedrag dient als inspiratie.
- Bij publicatie een andere productnaam dan "Factory I/O" gebruiken.

### Runtime-libraries en afhankelijkheden
| Afhankelijkheid | Licentie | Opmerking |
|-----------------|----------|-----------|
| Godot 4 | MIT, met de onderdelen die het meeneemt (Jolt, FreeType, HarfBuzz, lettertypen, …) onder hun eigen licenties | Volledige lijst uit de engine zelf in `THIRD-PARTY-NOTICES.txt` (`tools/notices/build_notices.py`) |
| NModbus 3.0.83 | MIT (`LICENSE.txt` in het NuGet-pakket, nagelezen 2026-11-23: Copyright (c) 2006 Scott Alexander, 2015 Dmitry Turin) | Toegevoegd in M3; tekst in `THIRD-PARTY-NOTICES.txt` |
| .NET-runtime (gaat mee in een export) | MIT, .NET Foundation | Tekst in `THIRD-PARTY-NOTICES.txt`, overgenomen uit een .NET Foundation-pakket op deze pc. **Let op**: `C:\Program Files\dotnet\LICENSE.txt` is níet MIT maar de *Microsoft .NET Library*-licentie van de installatie zelf; de runtime-pack die een export meeneemt heeft eigen `LICENSE.TXT` en `THIRD-PARTY-NOTICES.TXT`. Bij plan 32 (Windows-export) nalopen welke van de twee in de export zit en die bestanden in de zip zetten |
| Python-standaardbibliotheek | PSF-licentie | De fake-PLC gebruikt geen pakketten van derden; pymodbus is niet nodig gebleken |
| OPC Foundation UA-.NETStandard (later) | GPLv2 voor niet-leden / RCL voor leden | Prima voor privégebruik; bij distributie van de app opnieuw beoordelen |
| MATIEC, RuSTy, esstee (alleen als testhulp, later) | GPL-familie | Niet meelinken in de app; hooguit als los hulpprogramma in tests |
| OSCAT-export (parser-corpus, later) | Nalopen vóór gebruik | Niet in de repo opnemen zonder licentiecheck |

Regel: **bij elke nieuwe afhankelijkheid de licentie nalopen en in deze tabel zetten** voordat hij wordt toegevoegd.

## Melden
Zie je iets dat tegen deze regels ingaat (een poort die openstaat, een bestand dat niet in git hoort), noteer het in [TROUBLESHOOTING.md](TROUBLESHOOTING.md) en los het op voordat er verder gebouwd wordt.
