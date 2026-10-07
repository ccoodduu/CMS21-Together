# Playtest: hurtigstart (dansk)

Kort vejledning til et playtest med 2–3 spillere over Steam. Den fulde guide på engelsk er `docs/install.md` og
`docs/hosting.md`. Tjeklisten over, hvad I skal prøve, er `docs/playtest.md`.

## Det skal alle have

- Car Mechanic Simulator 2021 på Steam. Steam skal køre, mens I spiller.
- Samme version af mod'en. Alle bruger den samme zip-fil: `CMS21-Together-<version>-client.zip`.
- Ingen gameplay-mods (QoLmod, TK, LvxBetterCarSpawns, LvxOwnedCarsOnly, QuickShop og AutosaveMod afvises). Grafik-mods og LoadOptimizer er fine.
- Tag en kopi af dine egne saves først, hvis du er nervøs for dem: `%USERPROFILE%\AppData\LocalLow\Red Dot Games\Car
  Mechanic Simulator 2021`. Mod'en gemmer ikke i dine egne saves, mens du er i en session.

## 1. Installer MelonLoader 0.5.7

Det skal være præcis version 0.5.7. MelonLoader 0.6 virker ikke med mod'en.

1. Find spilmappen: højreklik på **Car Mechanic Simulator 2021** i Steam > **Administrer** > **Gennemse lokale
   filer**.
2. Gå til https://github.com/LavaGang/MelonLoader/releases/tag/v0.5.7.
3. Vælg én af de to måder:
   - **Installeren:** hent `MelonLoader.Installer.exe` og kør den. Vælg spillets `Car Mechanic Simulator 2021.exe` i
     spilmappen. Fjern fluebenet ved **Latest**, og vælg **v0.5.7** i listen. Klik **INSTALL**.
   - **Manuelt:** hent `MelonLoader.x64.zip`, og pak den ud direkte i spilmappen. Bagefter ligger mappen `MelonLoader`
     og filen `version.dll` ved siden af `Car Mechanic Simulator 2021.exe`.
4. Start spillet én gang og luk det igen. Et sort konsolvindue åbner sammen med spillet. Første start tager flere
   minutter, fordi MelonLoader forbereder spillet. Bagefter findes mapperne `Mods`, `UserData` og `UserLibs` i
   spilmappen.

## 2. Installer mod'en

1. Luk spillet.
2. Pak `CMS21-Together-<version>-client.zip` ud direkte i spilmappen. Hvis Windows spørger, så vælg **Erstat filerne
   i destinationen**.
3. Tjek, at disse filer nu findes:
   - `Mods\CMS21-Together.dll`
   - `UserLibs\CMS21_Together_Core.dll`, `UserLibs\Facepunch.Steamworks.Win64.dll` og `UserLibs\steam_api64.dll`
   - mappen `TogetherServer` (kun værten bruger den)
4. Start spillet. Konsollen skal vise `Together Mod <version> initialized!` og `Steamworks initialized successfully.`
5. I hovedmenuen er der nu en **Multiplayer**-knap øverst til højre. Skriv dit navn i navnefeltet, hvis du ikke vil
   bruge dit Steam-navn.

Hvis konsollen skriver `Steam DLL not found in UserLibs`, mangler `UserLibs\steam_api64.dll`. Pak zip-filen ud igen.

## 3. Værten starter en session

1. Hovedmenu > **Multiplayer** > fanen **Host**.
2. Vælg **New session** og en sværhedsgrad. Lad **Steam joins** være slået til. Port, antal spillere og kodeord kan
   blive, som de er.
3. Klik **Start**. Serveren åbner i sit eget vindue (minimeret), og spillet joiner selv. Værten er admin.

Steam bruger Valves relay, så der skal ikke åbnes porte i routeren. Steam skal køre på værtens PC.

## 4. De andre joiner over Steam

Vælg én af måderne:

- Højreklik på værten i Steam-vennelisten og vælg **Deltag i spil** / **Join Game**.
- Eller: hovedmenu > **Multiplayer** > fanen **Friends**, og klik **Join** ud for værten.
- Eller: værten inviterer fra sessionspanelet (**F9**).

Det, I skal prøve, står i `docs/playtest.md`. Start med punkt 3 (join over Steam), fordi det aldrig er testet før.

## Taster

| Tast | Hvad |
|---|---|
| **F9** | Sessionspanel: spillere, ping, Steam-venner, invitér og kick (værten) |
| **F7** | Hent garagen fra serveren igen, hvis noget ser forkert ud |
| **F8** | Fejlrapport: gemmer logs fra dig, de andre og serveren med det samme id |

## Når noget går galt

- **Spillet kører stadig:** tryk **F8** lige efter fejlen, og skriv kort, hvad I gjorde.
- **Spillet crashede eller frøs:** dobbeltklik på `Collect-Logs.bat` i spilmappen. Den laver en zip med logs.
- Send zip-filerne til den, der står for playtestet, sammen med en kort beskrivelse. Værten kan også sende `TogetherServer\Log\Latest.txt`.

## Afinstaller

Slet `Mods\CMS21-Together.dll`, de tre `CMS21`/`Facepunch`/`steam_api64`-filer i `UserLibs` og mappen
`TogetherServer`. MelonLoader fjernes ved at køre installeren igen og klikke **UN-INSTALL** (eller slette mappen
`MelonLoader` og `version.dll`).
