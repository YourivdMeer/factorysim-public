# FactorySim

Een lokale Windows-app met een 3D-fabrieksscène (emitter → band → photo-eye → pusher → remover en ruim honderd andere onderdelen) die je bestuurt met je eigen PLC-programma, zonder echte hardware. Bedoeld om te leren en te oefenen.

- **Spoor A**: een CODESYS V3-project in CODESYS Control Win V3 stuurt de scène via Modbus TCP. Zonder CODESYS kan het ook met de meegeleverde fake-PLC (Python).
- **Spoor B** (later): een CoDeSys V2.3-export (`.exp`) draait in een runtime in de app zelf.

Gebouwd met Godot 4.7.2 (.NET), C# op `net8.0`, Jolt physics en NModbus. Geen code of assets van Factory I/O of CODESYS: alles is eigen werk (zie [docs/SECURITY.md](docs/SECURITY.md)).

## Vereisten

- Windows 10/11
- .NET SDK 8 of nieuwer
- Godot 4.7.2 **.NET-editie** (de console-exe is handig voor zelftests)
- Python 3.9+ via de `py`-launcher, alleen voor de fake-PLC en de test-API (alleen de standaardbibliotheek)
- Optioneel: CODESYS V3.5 SP16+ met Control Win V3 (spoor A met een echte runtime), Blender (alleen om de 3D-modellen opnieuw te maken)

## Bouwen en starten

Vanuit de map van deze repo (`$godot` is het pad naar de Godot-exe):

```
dotnet build FactorySim.sln
dotnet test FactorySim.sln
& $godot --path src/FactorySim.App
```

Na een wijziging in C# eerst `dotnet build`, dan Godot starten. Een bepaalde scène openen: `-- --scene=<pad>.fsscene.json`.

Koppelen zonder CODESYS:

```
py tools/fake_plc/fake_plc.py --mode sorter --port 5020
```

Zet in de app de poort op 5020, klik **Verbinden**, **Run** en **Start**.

Zelftests en de volledige regressie (± 16 minuten): zie [docs/TESTING.md](docs/TESTING.md) en `bash tools/regress.sh`.

## Documentatie

| Wat | Waar |
|-----|------|
| Scènes ontwerpen, bestandsformaat, test-API | [docs/SCENES.md](docs/SCENES.md) |
| Alle onderdelen | [docs/LIBRARY.md](docs/LIBRARY.md) |
| Architectuur | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| CODESYS koppelen | [codesys/README.md](codesys/README.md) |
| Testen | [docs/TESTING.md](docs/TESTING.md) |
| Bekende problemen | [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) |
| Wijzigingen per versie | [WIJZIGINGEN.txt](WIJZIGINGEN.txt) |

FactorySim valt onder de MIT-licentie, zie [LICENSE](LICENSE). De licenties van wat de app gebruikt (Godot met de onderdelen die het meeneemt, de .NET-runtime, NModbus) staan in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt); opnieuw maken met `py tools/notices/build_notices.py --godot "<Godot-exe>"`.

CODESYS is een merk van CODESYS GmbH; Factory I/O is een product van Real Games. FactorySim heeft geen band met CODESYS GmbH of met Real Games en wordt niet door hen ondersteund.
