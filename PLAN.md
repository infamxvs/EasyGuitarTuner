# Plan: EasyGuitarTuner - Aplicatie .NET MAUI

## Stare curenta: IMPLEMENTAT

Toate fisierele descrise in acest plan au fost create si compilate cu succes.

---

## Arhitectura generala

Sunetul de la microfon este capturat continuu prin streaming. Chunk-urile PCM sunt acumulate intr-un buffer glisant. Algoritmul HPS (Harmonic Product Spectrum) bazat pe FFT detecteaza frecventa fundamentala. Aceasta este stabilizata printr-o logica de smoothing cu confirmare in 2 cicluri si hold time, apoi comparata cu notele standard ale chitarei.

```
Microfon (Plugin.Maui.Audio IAudioStreamer)
    -> AudioCaptureService      (buffer glisant 16384 esantioane, suprapunere 50%)
    -> PitchDetectorService     (RMS gate + Hann + FFT + HPS via MathNet -> Hz)
    -> TunerViewModel           (confirmare 2 cicluri + EMA + hold time -> NoteResult)
    -> NoteAnalyzerService      (nota + cents deviation)
    -> MainPage                 (AnalogMeterView + afisaj 3 coloane: STANDARD/440Hz | nota+Hz | cents/CENTS)
    -> FileSessionLogger        (tuner-session.log)
```

---

## Fisiere existente

### Modele

**`Models/NoteResult.cs`**
- `NoteName` (ex: "E")
- `Octave` (ex: 4)
- `FrequencyHz` (ex: 329.6)
- `CentsDeviation` (-100 la +100)
- `TuningStatus` — enum: `Flat`, `InTune`, `Sharp`

---

### Servicii

**`Services/IAudioCaptureService.cs`** + **`Services/AudioCaptureService.cs`**
- Foloseste `Plugin.Maui.Audio` `IAudioStreamer` (streaming continuu, fara goluri)
- Configureaza streamer-ul: Mono, PCM 16-bit, 44100 Hz
- Acumuleaza chunk-urile PCM intr-un buffer glisant cu suprapunere 50%
- Fereastra de analiza: 16384 esantioane (~371ms) → ~5 analize/secunda
- Emite eveniment `OnAudioCaptured(byte[] pcmData)` cand bufferul e plin

**`Services/IPitchDetectorService.cs`** + **`Services/PitchDetectorService.cs`**
- Primeste bytes PCM brut (16-bit signed, mono)
- Verifica prag RMS (0.003) — ignora linistea/zgomotul ambient, dar permite detectia coardelor subtiri (E4) cu decay rapid
- Aplica fereastra Hann
- Aplica FFT via `MathNet.Numerics`
- Aplica **HPS (Harmonic Product Spectrum)** cu 5 armonice — detecteaza fundamentala, nu armonicele
- **Corectie eroare de octava**: dupa HPS, compara peak-ul local (+/-2 bins) in jurul suboctavei (peakBin/2) cu peak-ul local in jurul peakBin; coboara octava doar daca suboctava are >= 60% din amplitudine
- Interpolare parabolica sub-bin pentru precizie mai buna
- Filtreaza in afara domeniului 70–1200 Hz

**`Services/INoteAnalyzerService.cs`** + **`Services/NoteAnalyzerService.cs`**
- Tabel corzi standard:

| Coarda | Nota | Frecventa |
|--------|------|-----------|
| 6      | E2   | 82.41 Hz  |
| 5      | A2   | 110.00 Hz |
| 4      | D3   | 146.83 Hz |
| 3      | G3   | 196.00 Hz |
| 2      | B3   | 246.94 Hz |
| 1      | E4   | 329.63 Hz |

- Calculeaza deviatia in cents: `cents = 1200 * log2(f_masurat / f_tinta)`
- Prag InTune: < 5 cents | Galben: 5–15 cents | Rosu: > 15 cents

**`Services/ISessionLogger.cs`** + **`Services/FileSessionLogger.cs`**
- Scrie un fisier `tuner-session.log` in folderul proiectului
- Fisierul este **rescris complet** la fiecare pornire a aplicatiei
- Logheaza: start/stop captura, RMS, frecventa RAW, frecventa SMOOTH, nota detectata, cents, status
- Formatul unei linii: `[HH:mm:ss.fff] RMS=0.0082 | RAW=82.45 Hz | SMOOTH=82.40 Hz | NOTA=E2 | CENTS=+0.5 | InTune`

---

### ViewModel

**`ViewModels/TunerViewModel.cs`**
- Proprietati bindabile: `NoteText`, `OctaveText`, `FrequencyText`, `CentsText`, `NeedleAngle`, `ToggleButtonText`, `IsListening`
- `NoteText` / `OctaveText`: nota detectata si octava (ex: `"E"` + `"2"`); cand nu exista semnal `NoteText = "—"`, `OctaveText = ""`
- `FrequencyText`: ex `"247.6 Hz"` sau `"0.0 Hz"` cand nu se detecteaza
- `CentsText`: deviatia rotunjita la intreg cu semn (ex: `"-1"`, `"+4"`); gol cand nu exista semnal
- Se aboneaza la `OnAudioCaptured` din `AudioCaptureService`
- Strategie de smoothing pe frecventa detectata (confirmare + EMA + hold):
  - **Cold start / Saltea mare** (`|RAW - SMOOTH| / SMOOTH > 0.15`, JumpThreshold=0.15): noul RAW intra in `_pendingFrequency` — acul NU se misca inca. Doar daca urmatorul ciclu confirma cu o frecventa similara (`< 5%` diferenta, SimilarityThreshold=0.05), `SMOOTH = RAW` (snap). Filtreaza glitch-urile izolate de 1 ciclu.
  - **Variatii mici** (sub 15%): EMA cu `SmoothingAlpha = 0.4` — raspuns rapid la vibrato/drift fara saltari abrupte.
  - **Hold time** la linistire (RAW=0 si SMOOTH>0): incrementeaza `_silenceCount`. Cat timp e sub `HoldCycles = 3`, SMOOTH ramane neschimbat (acul si afisajul stau fixe pe ultima nota). La al 3-lea ciclu de liniste, reseteaza `SMOOTH = 0`, `_pendingFrequency = 0`, `_silenceCount = 0`. Crucial pentru coardele inalte (B3, E4) cu decay fizic scurt.
  - Cost: ~200ms latenta la inceputul fiecarei note noi (confirmare) + ~400ms persistenta vizuala dupa atenuare (2 cicluri vizibile inainte de reset). Castig: glitch-uri eliminate si afisare suficient de lunga pentru toate cele 6 coarde (E4 trece de la ~200ms la ~1.2s afisaj).
- Logheaza pentru fiecare ciclu si **RMS-ul real** (util pentru diagnoza)
- `MapCentsToAngle`: clampeaza cents la [-50, +50] si mapeaza la unghi ac [-50, +50] grade (aliniat la marcajele cadranului)
- Orchestreaza `PitchDetectorService`, `NoteAnalyzerService` si `ISessionLogger`

---

### Controale UI

**`Controls/AnalogMeterView.cs`** — SkiaSharp custom control (grafica pe baza de imagini)
- Deseneaza doua imagini incarcate din `Resources/Raw`: fundalul cadranului VU vintage (`background.png`, 900×1950) si acul indicator (`needle.png`, 20×284)
- Imaginile sunt incarcate o singura data ca `SKImage` prin `FileSystem.OpenAppPackageFileAsync` (pixel-perfect, fara rescalare DPI), apoi cache-uite
- **Fundal**: scalat cu `scale = min(width/bgW, height/bgH)` si centrat (fit "contain", fara distorsiune); raportul imaginii ≈ raportul ferestrei deci umple ecranul
- **Ac**: desenat peste fundal, ancorat la baza lui (jos-centru = pixel `(needleW/2, needleH)`). Transformarea: `Translate(pivot)` → `Scale(scale)` → `RotateDegrees(NeedleAngle)` → `DrawImage(needle, -needleW/2, -needleH)`. Rotatia se face exact in jurul bazei acului
- **Pivot** exprimat ca fractii din imaginea de fundal: `PivotXFraction = 0.5`, `PivotYFraction = 0.56` (valori validate vizual — acul sta corect pe pivotul cadranului)
- Sampling de calitate (`SKSamplingOptions` Linear/Linear)
- Singura proprietate bindabila: `NeedleAngle`

---

### Pagini

**`MainPage.xaml`** + **`MainPage.xaml.cs`**
- `AbsoluteLayout` cu pozitionare proportionala peste imaginea de fundal
- `AnalogMeterView` acopera intregul ecran (`LayoutBounds="0,0,1,1"`)
- Afisaj pe 3 coloane (`Grid ColumnDefinitions="*,2*,*"`, `LayoutBounds="0.5,0.73,0.85,0.12"`) plasat peste caseta de afisaj care e deja desenata in imaginea de fundal. Coloana centrala e de 2x latimea laterelelor (raport 1:2:1) ca sa coincida cu caseta neagra; coloanele laterale au `Margin="0,6,0,0"`:
  - **Stanga**: eticheta `STANDARD` + valoare statica `440 Hz` (referinta acordaj, culoare `#C9B68A`, fonturi 7 / 9)
  - **Centru**: **doar text** (fara niciun chenar desenat) plasat peste caseta neagra din imagine — nota mare + octava ca subscript (`FormattedString` doua `Span`-uri, `#F2A640`, fonturi 34 / 16) si frecventa dedesubt (`#E8922E`, font 12)
  - **Dreapta**: valoarea `CentsText` + eticheta `CENTS` (culoare `#C9B68A`, fonturi 11 / 7)
- Buton Start / Stop in stil vintage (fundal `#3A2A18`, border `#8A6A3A`, text `#F5E6C8`) peste panoul de jos (`LayoutBounds="0.5,0.9,0.7,0.09"`)
- Titlul aplicatiei si caseta de afisaj sunt parte din imaginea de fundal (fara header XAML si fara `Border` desenat — peste fundal se aseaza doar text)

---

### Infrastructura

**`MauiProgram.cs`**
- `ISessionLogger` → `FileSessionLogger` (Singleton)
- `IAudioCaptureService` → `AudioCaptureService` (Singleton)
- `IPitchDetectorService` → `PitchDetectorService` (Singleton)
- `INoteAnalyzerService` → `NoteAnalyzerService` (Singleton)
- `TunerViewModel` (Transient)
- `MainPage` (Transient)
- `AppShell` (Singleton)
- Fonts inregistrate: `OpenSans-Regular`, `OpenSans-Semibold`

**`AppShell.xaml.cs`** — primeste `MainPage` prin DI

**`App.xaml.cs`** — primeste `AppShell` prin DI; seteaza fereastra fixa 249×540 px pe Windows (raport 19.5:9)

---

## Permisiuni necesare

| Platforma | Fisier | Permisiune |
|-----------|--------|------------|
| Android   | `Platforms/Android/AndroidManifest.xml`      | `RECORD_AUDIO` |
| Windows   | `Platforms/Windows/Package.appxmanifest`     | `microphone` (DeviceCapability) |
| iOS       | `Platforms/iOS/Info.plist`                   | `NSMicrophoneUsageDescription` |
| MacCatalyst | `Platforms/MacCatalyst/Info.plist`         | `NSMicrophoneUsageDescription` |
| MacCatalyst | `Platforms/MacCatalyst/Entitlements.plist` | `com.apple.security.device.audio-input` |

---

## Sumar fisiere

| Fisier | Stare |
|--------|-------|
| `Models/NoteResult.cs` | Creat |
| `Services/IAudioCaptureService.cs` | Creat |
| `Services/AudioCaptureService.cs` | Creat |
| `Services/IPitchDetectorService.cs` | Creat |
| `Services/PitchDetectorService.cs` | Creat |
| `Services/INoteAnalyzerService.cs` | Creat |
| `Services/NoteAnalyzerService.cs` | Creat |
| `Services/ISessionLogger.cs` | Creat |
| `Services/FileSessionLogger.cs` | Creat |
| `ViewModels/TunerViewModel.cs` | Modificat (proprietati NoteText/OctaveText/CentsText pentru afisaj pe coloane; smoothing + EMA + hold time; unghi ac ±50° pentru ±50 centi) |
| `Controls/AnalogMeterView.cs` | Modificat (grafica pe imagini: fundal + ac rotit din `Resources/Raw`) |
| `Resources/Raw/background.png` | Creat (fundal cadran VU, 900×1950; include titlul si caseta neagra de afisaj desenate in imagine) |
| `Resources/Raw/needle.png` | Creat (ac indicator, 20×284) |
| `MainPage.xaml` | Modificat (afisaj pe 3 coloane `*,2*,*` la `0.5,0.73`; doar text peste caseta neagra din imagine, fara `Border`; etichete laterale culoare `#C9B68A`) |
| `Services/PitchDetectorService.cs` | Modificat (prag RMS scazut la 0.003 pentru coarde subtiri) |
| `MauiProgram.cs` | Modificat (fonts) |
| `App.xaml.cs` | Modificat (DI + fereastra fixa 249x540 pe Windows) |
| `AppShell.xaml.cs` | Modificat (DI) |
| `AppShell.xaml` | Modificat (golita, continut din code-behind) |
| `Platforms/Android/AndroidManifest.xml` | Modificat (permisiuni) |
| `Platforms/Windows/Package.appxmanifest` | Modificat (permisiuni) |
| `Platforms/iOS/Info.plist` | Modificat (permisiuni) |
| `Platforms/MacCatalyst/Info.plist` | Modificat (permisiuni) |
| `Platforms/MacCatalyst/Entitlements.plist` | Modificat (entitlement microfon) |

---

## Build si rulare din Cursor

Nu ai nevoie de Visual Studio. Poti face build si rula aplicatia direct din terminalul integrat Cursor.

### Deschide terminalul

Apasa **Ctrl+`** (backtick) sau mergi la **View → Terminal**.
Terminalul se deschide automat in radacina proiectului.

### Comenzi

```powershell
# Verifica daca codul compileaza (fara sa porneasca aplicatia)
dotnet build -f net10.0-windows10.0.19041.0

# Build + porneste aplicatia pe Windows
dotnet run -f net10.0-windows10.0.19041.0

# Build optimizat (Release, fara debug)
dotnet build -f net10.0-windows10.0.19041.0 -c Release
```

### Ce se intampla la `dotnet run`

1. Codul este compilat automat
2. Fereastra aplicatiei se deschide pe desktop
3. Terminalul ramane activ cat timp aplicatia ruleaza
4. La inchiderea ferestrei, procesul se opreste singur (exit code 0)
5. Fisierul `tuner-session.log` este creat/rescris in folderul proiectului

### Permisiuni microfon pe Windows

Deoarece proiectul ruleaza **neimpachetat** (`WindowsPackageType=None`), permisiunile din `Package.appxmanifest` nu se aplica automat. La primul apas pe **Start**, Windows poate cere acces la microfon printr-un popup. Daca nu apare popup-ul, activeaza manual din:

**Setari Windows → Confidentialitate si securitate → Microfon → Permite aplicatiilor desktop sa acceseze microfonul**

---

## Pachete NuGet utilizate

| Pachet | Versiune | Rol |
|--------|----------|-----|
| `Plugin.Maui.Audio` | 4.0.0 | Streaming audio de la microfon (`IAudioStreamer`) |
| `MathNet.Numerics` | 5.0.0 | Calculeaza FFT (Fourier.Forward) |
| `SkiaSharp.Views.Maui.Controls` | 3.119.4 | Deseneaza grafica (fundal + ac din imagini, rotire ac) |
| `Microsoft.Maui.Controls` | 10.0.20 | Framework-ul MAUI |

---

## Backup GitHub (principal)

Proiectul este versioned pe GitHub la: **https://github.com/infamxvs/EasyGuitarTuner**

- Branch principal: `main`
- Remote: `origin`
- Cont GitHub: `infamxvs`
- GitHub CLI (`gh`) instalat in `C:\Program Files\GitHub CLI`

**Comanda pentru backup rapid:**
```powershell
cd "C:\Users\Dan\source\repos\EasyGuitarTuner"
git add .
git commit -m "Backup: <descriere modificari>"
git push origin main
```

Exista o regula Cursor (`.cursor/rules/github-backup.mdc`) care permite executarea automata a backup-ului cand se cere verbal "fa un backup in github".

**`.gitignore` exclude:** `bin/`, `obj/`, `.vscode/`, `tuner-session.log`, fisiere temporare. Regulile Cursor (`.cursor/rules/`) sunt incluse in repo.

---

## Backup local

Backupurile locale se fac in `D:\SALVARI EasyGuitarTuner\` cu subfolderul avand data si ora (format `YYYY-MM-DD_HH-mm`), ex: `2026-05-28_14-32`.

**Comanda pentru backup local:**
```powershell
$folder = Get-Date -Format "yyyy-MM-dd_HH-mm"; robocopy "C:\Users\Dan\source\repos\EasyGuitarTuner" "D:\SALVARI EasyGuitarTuner\$folder" /E /XD bin obj .vs .git packages /XF *.user
```

**Comanda pentru restaurare dintr-un backup local:**
```powershell
robocopy "D:\SALVARI EasyGuitarTuner\2026-05-28_14-32" "C:\Users\Dan\source\repos\EasyGuitarTuner" /E /PURGE
```

Folderele excluse din backup (`bin`, `obj`, `.vs`, `.git`, `packages`) se regenereaza automat la urmatorul build.

---

## Note tehnice pentru conversatii viitoare

- **HPS vs FFT simplu**: HPS (Harmonic Product Spectrum) inmulteste spectrul FFT cu versiunile sale comprimate la 1/2, 1/3, 1/4, 1/5. Fundamentala iese in evidenta deoarece coincide cu propriile armonice la toate compresiile. Fara HPS, FFT simplu detecta uneori E3 in loc de E2.
- **Corectie octava in HPS**: HPS poate detecta prima armonica (octava superioara) cand aceasta e mai puternica decat fundamentala (frecvent la D3, G3). Fix: dupa gasirea peak-ului HPS, cautam peak local in fereastra +/-2 bins in jurul `peakBin/2` (acopera leakage spectral) si comparam cu peak local in jurul `peakBin`. Coboram octava doar daca suboctava are >= 60% din amplitudine. Pragul mare (60%) si fereastra de cautare evita falsele coborari cauzate de zgomot sau leakage spectral — important pentru B3 (247 Hz) unde un prag mic ar coboara fals la B2 (123 Hz).
- **Suprapunere buffer**: bufferul glisant pastreaza 50% din datele anterioare la fiecare ciclu. Aceasta reduce artefactele de granita si mentine receptivitatea.
- **Strategie smoothing (3 mecanisme combinate)**:
  1. **Confirmare in 2 cicluri** la cold-start sau saltea mare (>15%): RAW nou intra in `_pendingFrequency`, acul nu se misca. Doar dupa ce ciclul urmator confirma o frecventa similara (<5% diferenta) cu pending-ul, comutam `SMOOTH = RAW`. Filtreaza glitch-uri izolate (ex: un ciclu cu RAW=97 Hz urmat de RAW=196 Hz stabil → glitch-ul ignorat).
  2. **EMA** (`smooth = smooth * 0.6 + raw * 0.4`) pentru variatii sub 15% — raspuns rapid la vibrato/drift fara saltari.
  3. **Hold time** (`HoldCycles = 3`) cand RAW=0: SMOOTH ramane neschimbat pentru 2 cicluri vizibile (~400ms), apoi al 3-lea ciclu reseteaza la 0. Crucial pentru coardele inalte (B3, E4) care au decay fizic foarte scurt — fara hold, E4 ar dispărea instant dupa 1 ciclu de detectie.
- **Sample rate asumat**: 44100 Hz pe toate platformele. Daca microfonul Windows ruleaza nativ la 48000 Hz si plugin-ul nu onoreaza setarea, frecventele vor aparea cu ~8.8% mai mari decat real — verificabil din `tuner-session.log` (CENTS constant mare la o coarda cunoscuta).
- **Range ac ±50 centi**: cadranul desenat (`background.png`) are marcaje `-50 ... 0 ... +50`. `TunerViewModel.MapCentsToAngle` clampeaza cents la ±50 si mapeaza la ±50° (`MaxCentsForNeedle = 50`, `MaxNeedleAngleDegrees = 50`), aliniat la marcaje.
- **Grafica pe imagini (AnalogMeterView)**: in loc de desen procedural, controlul incarca `background.png` si `needle.png` din `Resources/Raw` ca `SKImage` (o singura data, cache). Fundalul e scalat "contain" si centrat. Acul e desenat peste fundal cu transformarea `Translate(pivot) → Scale(scale) → RotateDegrees(NeedleAngle) → DrawImage(needle, -needleW/2, -needleH)`, deci pivotul de rotatie e baza acului. Pozitia pivotului in cadran = `PivotXFraction`/`PivotYFraction` (fractii din imagine), reglabile vizual.
- **De ce Resources/Raw si nu MauiImage**: imaginile trebuie pastrate la rezolutia reala (acul e deja la scara corecta fata de fundal). `MauiImage` ar aplica rescalare dupa DPI si ar strica raportul. `MauiAsset` (folderul `Resources/Raw`) pastreaza fisierul pixel-perfect, incarcabil cu `FileSystem.OpenAppPackageFileAsync`.
- **Caseta de afisaj e parte din imagine**: noua imagine de fundal include deja caseta neagra de afisaj (si titlul) desenate. In XAML NU se mai deseneaza niciun `Border` — peste fundal se aseaza doar text (nota/frecventa centrate peste caseta neagra). Sursa imaginii editate e in `Images/background_1950_900.png`; pentru a o folosi se copiaza peste `Resources/Raw/background.png` (acelasi nume de asset folosit de `AnalogMeterView`). Daca pozitia casetei sau a cadranului se schimba la o editare viitoare, se recalibreaza `LayoutBounds` din `MainPage.xaml` (centrul randului de afisaj, acum `0.5,0.73`) si pivotul acului din `AnalogMeterView.cs`.
- **Calibrare pivot/unghi**: `PivotXFraction = 0.5`, `PivotYFraction = 0.56` si `MaxNeedleAngleDegrees = 50` sunt valorile validate vizual — baza acului sta pe pivotul fizic al cadranului si varful atinge `±50` pe marcaje. Daca se schimba imaginea de fundal, aceste valori trebuie recalibrate.
- **Hold time vs decay fizic**: coardele subtiri (B3 = 247 Hz, E4 = 330 Hz) au timp de decay fizic de ~3x mai scurt decat coardele groase (E2 = 82 Hz). Fara hold time, E4 ar fi afisat doar 200ms inainte ca RMS sa scada sub prag. Combinatia "prag RMS scazut la 0.003 + hold de 3 cicluri" extinde afisarea E4 de la ~200ms la ~1.2s, suficient pentru utilizator sa citeasca cents-urile.
- **Raport de aspect 19.5:9**: aplicatia este blocata in portret pe Android (atribut `ScreenOrientation.Portrait` in `MainActivity`) si iOS (doar `UIInterfaceOrientationPortrait` in `Info.plist`). Pe Windows, fereastra este fixata la 249×540 pixeli (540/249 ≈ 2.169 ≈ 19.5:9) prin `CreateWindow` in `App.xaml.cs` cu `MinimumWidth = MaximumWidth = 249` si `MinimumHeight = MaximumHeight = 540`. Utilizatorul nu poate redimensiona fereastra pe Windows.
- **Cum sa setezi NavBarIsVisible**: pentru a ascunde bara purpurie cu titlul aplicatiei (cand pagina e gazduita in Shell), foloseste `Shell.NavBarIsVisible="False"` pe `ContentPage` in XAML. Important pentru un look complet curat fara header.
