# Plan: EasyGuitarTuner - Aplicatie .NET MAUI

## Stare curenta: IMPLEMENTAT

Toate fisierele descrise in acest plan au fost create si compilate cu succes.

---

## Arhitectura generala

Sunetul de la microfon este capturat continuu prin streaming. Chunk-urile PCM sunt acumulate intr-un buffer glisant. Detectia frecventei fundamentale se face printr-unul din doi algoritmi interschimbabili la compilare (`TunerSettings.ActivePitchAlgorithm`): HPS (Harmonic Product Spectrum, spectral/FFT — implicit) sau YIN (domeniul timpului — alternativ). Frecventa bruta este stabilizata de un serviciu dedicat (filtru median + EMA + snap), apoi mapata pe cea mai apropiata nota cromatica (orice nota, orice acordaj).

```
Microfon (Plugin.Maui.Audio IAudioStreamer)
    -> AudioCaptureService      (buffer glisant, suprapunere 50%)
    -> IPitchDetectorService    (comutabil la compilare):
         - PitchDetectorService    (HPS: RMS gate + Hann + FFT + HPS via MathNet -> Hz brut)
         - YinPitchDetectorService (YIN: difference function + CMNDF + prag + interpolare parabolica -> Hz brut)
    -> FrequencyStabilizer      (median + EMA + snap -> Hz stabil)
    -> NoteAnalyzerService      (nota cromatica cea mai apropiata + cents in [-50, +50])
    -> TunerViewModel           (deadband + EMA pe unghi -> NoteResult + NeedleAngle)
    -> MainPage                 (AnalogMeterView cu animatie ac + afisaj 3 coloane: STANDARD/440Hz | nota+Hz | cents/CENTS)
    -> FileSessionLogger        (tuner-session.log)
```

---

## Fisiere existente

### Configurare

**`TunerSettings.cs`** — clasa statica, punct UNIC de reglaj pentru toti parametrii acordorului. Toate componentele citesc valorile de aici (nu mai exista `const` raspandite prin servicii). Contine si `enum PitchAlgorithm { Hps, Yin }`. Grupuri:
- **Comutator algoritm**: `ActivePitchAlgorithm` (`PitchAlgorithm.Hps` implicit) — alege detectorul; se schimba si se reconstruieste aplicatia
- **Captura audio (comun)**: `SampleRateHz` (48000), `AnalysisWindowSamples` (32768)
- **Detectie pitch HPS**: `NoiseFloorRms` (pragul de volum, COMUN ambilor algoritmi), `MinFrequencyHz` (60), `MaxFrequencyHz` (1000), `HarmonicCount` (7)
- **Detectie pitch YIN (alternativ)**: `YinWindowSamples` (4096 — subset din buffer), `YinThreshold` (0.10 — pragul CMNDF canonic de Cheveigne), `YinMinFrequencyHz` (60), `YinMaxFrequencyHz` (1000), `YinAperiodicityCutoff` (0.80 — peste atat dip-ul => semnal neperiodic, fara nota)
- **Stabilizare frecventa**: `MedianWindow` (5), `FrequencySmoothingAlpha` (0.4), `JumpThreshold` (0.15), `HoldCycles` (3)
- **Analiza nota**: `ReferencePitchHz` (440 — A4, referinta scala cromatica), `InTuneToleranceCents` (5.0)
- **Afisaj / ac**: `CentsDeadband` (2.0), `AngleSmoothingAlpha` (0.6), `MaxCentsForNeedle` (50), `AngleLerpFactor` (0.3), `AngleSettleThreshold` (0.05), `FrameIntervalMs` (16)
- **Calibrare unghi ac**: `NeedleAngleOffsetDegrees` (0 — unghiul acului la cents=0, regleaza centrul), `MaxNeedleAnglePositiveDegrees` (50 — grade pana la marcajul +50), `MaxNeedleAngleNegativeDegrees` (50 — grade, magnitudine, pana la marcajul -50)
- **Calibrare grafica ac**: `PivotXFraction` (0.5), `PivotYFraction` (0.56)
- **Test calibrare**: `ForcedCentsForCalibration` (`double.NaN` = detectie normala; orice valoare forteaza acul exact la acei centi, pentru verificare vizuala)

Clasa e statica (referita direct, fara DI) ca sa fie accesibila uniform inclusiv din `AnalogMeterView`, care e instantiat din XAML, nu prin container. Valorile sunt `const` imutabile — se modifica in fisier si se reconstruieste aplicatia.

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
- Configureaza streamer-ul: Mono, PCM 16-bit, 48000 Hz
- Acumuleaza chunk-urile PCM intr-un buffer glisant cu suprapunere 50%
- Fereastra de analiza: 32768 esantioane (~683ms) → ~3 analize/secunda
- Emite eveniment `OnAudioCaptured(byte[] pcmData)` cand bufferul e plin

**`Services/IPitchDetectorService.cs`** + **`Services/PitchDetectorService.cs`**
- Primeste bytes PCM brut (16-bit signed, mono)
- Verifica prag RMS (0.003) — ignora linistea/zgomotul ambient, dar permite detectia coardelor subtiri (E4) cu decay rapid
- Aplica fereastra Hann
- Aplica FFT via `MathNet.Numerics`
- Aplica **HPS (Harmonic Product Spectrum)** cu 5 armonice — detecteaza fundamentala, nu armonicele
- **Corectie eroare de octava**: dupa HPS, compara peak-ul local (+/-2 bins) in jurul suboctavei (peakBin/2) cu peak-ul local in jurul peakBin; coboara octava doar daca suboctava are >= 60% din amplitudine
- Interpolare parabolica sub-bin pentru precizie mai buna
- Filtreaza in afara domeniului 60–1000 Hz
- Returneaza frecventa **bruta** (fara smoothing) — stabilizarea se face separat

**`Services/YinPitchDetectorService.cs`** (detector alternativ, implementeaza tot `IPitchDetectorService`)
- Algoritmul **YIN** (de Cheveigne & Kawahara, 2002), in domeniul timpului — total independent de HPS, nu il modifica
- Foloseste doar ultimele `YinWindowSamples` (4096) esantioane din bufferul de captura (Optiunea 1: captura ramane neatinsa)
- Prag RMS (`NoiseFloorRms`, comun) → ignora linistea
- **Difference function** + **CMNDF** (cumulative mean normalized difference, `cmndf[0]=1`) — reduce drastic erorile de octava fara euristici
- **Prag absolut** (`YinThreshold`): primul dip sub prag, urmarit pana la minimul local; daca niciunul, ia minimul global
- **Gate de aperiodicitate** (`YinAperiodicityCutoff`): daca dip-ul ales e prea mare, semnalul e neperiodic → returneaza 0
- **Interpolare parabolica** pe tau pentru precizie sub-esantion → `frecventa = sampleRate / tau`
- Se activeaza din `TunerSettings.ActivePitchAlgorithm = PitchAlgorithm.Yin` (inregistrare DI in `MauiProgram`)

**`Services/IFrequencyStabilizer.cs`** + **`Services/FrequencyStabilizer.cs`**
- Primeste frecventa bruta de la `PitchDetectorService` si returneaza o frecventa stabila
- **Filtru median** pe ultimele 5 valori (`MedianWindow = 5`) — respinge outlierii izolati (armonice, erori de octava, artefacte); un glitch trebuie sa apara in >= 3 din 5 cicluri ca sa afecteze rezultatul
- **EMA** (`SmoothingAlpha = 0.4`) peste mediana pentru netezirea variatiilor mici (vibrato/drift)
- **Snap** la salt mare (`JumpThreshold = 0.15`): la schimbarea reala de coarda, comuta direct pe mediana (raspuns rapid)
- **Hold time** (`HoldCycles = 3`): cand semnalul dispare, pastreaza ultima frecventa cateva cicluri (~1s la 3 analize/sec), apoi reseteaza — crucial pentru coardele inalte cu decay scurt
- `Reset()` curata starea (apelat la oprirea capturii)
- Stateful, dar serviciu izolat (SRP) — usor de reglat fara sa atinga ViewModel-ul

**`Services/INoteAnalyzerService.cs`** + **`Services/NoteAnalyzerService.cs`**
- **Detectie cromatica** (nu mai exista tabel de corzi). Gaseste cea mai apropiata nota din scala egal temperata, indiferent de acordaj:
  - `midi = round(69 + 12 * log2(f / ReferencePitchHz))` (A4 = 440 Hz)
  - `f_tinta = ReferencePitchHz * 2^((midi - 69) / 12)`
  - `NoteName = ["C","C#",...,"B"][midi % 12]`, `Octave = midi / 12 - 1`
- Calculeaza deviatia in cents fata de nota cromatica cea mai apropiata: `cents = 1200 * log2(f_masurat / f_tinta)`. Matematic, rezultatul e mereu in **[-50, +50]** centi — la depasirea unei jumatati de semiton afisajul comuta automat pe nota vecina (ex: -60 centi fata de E2 → D# la +40 centi).
- Functioneaza pentru orice acordaj (standard, Drop D, acordaj cu 2 semitonuri mai jos D2-G2-C3-F3-A3-D4 etc.) fiindca nu depinde de corzile chitarei
- Prag InTune: < 5 cents | Galben: 5–15 cents | Rosu: > 15 cents

**`Services/ISessionLogger.cs`** + **`Services/FileSessionLogger.cs`**
- Scrie un fisier `tuner-session.log` in folderul proiectului
- Fisierul este **rescris complet** la fiecare pornire a aplicatiei
- Logheaza: start/stop captura, RMS, frecventa RAW, frecventa SMOOTH, nota detectata, cents, status
- Formatul unei linii: `[HH:mm:ss.fff] RMS=0.0082 | RAW=82.45 Hz | SMOOTH=82.40 Hz | NOTA=E2 | CENTS=+0.5 | InTune`

---

### ViewModel

**`ViewModels/TunerViewModel.cs`**
- Proprietati bindabile: `NoteText`, `OctaveText`, `FrequencyText`, `CentsText`, `NeedleAngle`
- Expune metodele publice `StartAsync()` / `StopAsync()` (idempotente, protejate de flag-ul intern `_isListening`) — apelate din `MainPage.OnAppearing` / `OnDisappearing` pentru pornire/oprire automata a capturii (fara buton)
- `NoteText` / `OctaveText`: nota detectata si octava (ex: `"E"` + `"2"`); cand nu exista semnal `NoteText = "—"`, `OctaveText = ""`
- `FrequencyText`: ex `"247.6 Hz"` sau `"0.0 Hz"` cand nu se detecteaza
- `CentsText`: deviatia rotunjita la intreg cu semn (ex: `"-1"`, `"+4"`); gol cand nu exista semnal
- Se aboneaza la `OnAudioCaptured` din `AudioCaptureService`
- Orchestreaza fluxul: `PitchDetectorService` (Hz brut) → `FrequencyStabilizer` (Hz stabil) → `NoteAnalyzerService` (nota + cents) → UI. Smoothing-ul pe frecventa nu mai e in ViewModel, ci in `FrequencyStabilizer`
- Stabilizarea unghiului acului (peste frecventa deja stabilizata):
  - **Deadband** (`CentsDeadband = 2.0`): cand `|cents| < 2`, unghiul tinta e fortat la `NeedleAngleOffsetDegrees` (centrul calibrat) — acul sta fix pe centru cand coarda e acordata, fara micro-tremur.
  - **EMA pe unghi** (`AngleSmoothingAlpha = 0.6`): unghiul tinta trimis spre control e netezit (`_smoothedAngle = _smoothedAngle * 0.4 + target * 0.6`), reducand jitter-ul rezidual din cents la frecvente joase. Resetat la 0 cand semnalul dispare.
- Logheaza pentru fiecare ciclu si **RMS-ul real** (util pentru diagnoza)
- `MapCentsToAngle`: aplica deadband, clampeaza cents la [-50, +50], apoi mapeaza liniar pe amplitudini **asimetrice** (`MaxNeedleAnglePositiveDegrees` pentru cents>0, `MaxNeedleAngleNegativeDegrees` pentru cents<0) plus `NeedleAngleOffsetDegrees` — aliniat la marcajele cadranului
- **Mod calibrare** (`ApplyForcedCalibration`): cand `TunerSettings.ForcedCentsForCalibration` nu e `double.NaN`, ViewModel-ul ignora microfonul si fixeaza acul exact la centii ceruti — folosit pentru a verifica vizual corespondenta cents↔marcaje. Se readuce la `double.NaN` dupa calibrare
- Orchestreaza `PitchDetectorService`, `NoteAnalyzerService` si `ISessionLogger`

---

### Controale UI

**`Controls/AnalogMeterView.cs`** — SkiaSharp custom control (grafica pe baza de imagini)
- Deseneaza doua imagini incarcate din `Resources/Raw`: fundalul cadranului VU vintage (`background.png`, 900×1950) si acul indicator (`needle.png`, 20×284)
- Imaginile sunt incarcate o singura data ca `SKImage` prin `FileSystem.OpenAppPackageFileAsync` (pixel-perfect, fara rescalare DPI), apoi cache-uite
- **Fundal**: scalat cu `scale = min(width/bgW, height/bgH)` si centrat (fit "contain", fara distorsiune); raportul imaginii ≈ raportul ferestrei deci umple ecranul
- **Ac**: desenat peste fundal, ancorat la baza lui (jos-centru = pixel `(needleW/2, needleH)`). Transformarea: `Translate(pivot)` → `Scale(scale)` → `RotateDegrees(_currentAngle)` → `DrawImage(needle, -needleW/2, -needleH)`. Rotatia se face exact in jurul bazei acului
- **Animatie lina a acului**: `NeedleAngle` (din binding) este doar *unghiul tinta* (`_targetAngle`). Un `IDispatcherTimer` la ~60 FPS (`FrameIntervalMs = 16`) interpoleaza unghiul desenat `_currentAngle` spre tinta (`_currentAngle += (target - current) * AngleLerpFactor`, `AngleLerpFactor = 0.3`). Acul gliseaza fluid intre cele ~5 valori/secunda primite de la detector.
- **Optimizare mobil**: timer-ul ruleaza **doar cat timp acul se misca**. Cand `|target - current| <= AngleSettleThreshold` (0.05), `_currentAngle` se aliniaza pe tinta si timer-ul se opreste — in repaus (fara semnal/acul fix) consumul CPU/baterie e zero.
- **Pivot** exprimat ca fractii din imaginea de fundal: `PivotXFraction = 0.5`, `PivotYFraction = 0.56` (valori validate vizual — acul sta corect pe pivotul cadranului)
- Sampling de calitate (`SKSamplingOptions` Linear/Linear)
- Singura proprietate bindabila: `NeedleAngle` (unghiul tinta)

---

### Pagini

**`MainPage.xaml`** + **`MainPage.xaml.cs`**
- `AbsoluteLayout` cu pozitionare proportionala peste imaginea de fundal
- `AnalogMeterView` acopera intregul ecran (`LayoutBounds="0,0,1,1"`)
- Afisaj pe 3 coloane (`Grid ColumnDefinitions="*,2*,*"`, `LayoutBounds="0.5,0.73,0.85,0.12"`) plasat peste caseta de afisaj care e deja desenata in imaginea de fundal. Coloana centrala e de 2x latimea laterelelor (raport 1:2:1) ca sa coincida cu caseta neagra; coloanele laterale au `Margin="0,6,0,0"`:
  - **Stanga**: eticheta `STANDARD` + valoare statica `440 Hz` (referinta acordaj, culoare `#C9B68A`, fonturi 7 / 9)
  - **Centru**: **doar text** (fara niciun chenar desenat) plasat peste caseta neagra din imagine — nota mare + octava ca subscript (`FormattedString` doua `Span`-uri, `#F2A640`, fonturi 34 / 16) si frecventa dedesubt (`#E8922E`, font 12)
  - **Dreapta**: valoarea `CentsText` + eticheta `CENTS` (culoare `#C9B68A`, fonturi 11 / 7)
- Fara buton de control: detectia porneste automat la afisarea paginii (`OnAppearing` apeleaza `TunerViewModel.StartAsync()`) si se opreste la inchidere (`OnDisappearing` apeleaza `StopAsync()`)
- Titlul aplicatiei si caseta de afisaj sunt parte din imaginea de fundal (fara header XAML si fara `Border` desenat — peste fundal se aseaza doar text)

---

### Infrastructura

**`MauiProgram.cs`**
- `ISessionLogger` → `FileSessionLogger` (Singleton)
- `IAudioCaptureService` → `AudioCaptureService` (Singleton)
- `IPitchDetectorService` → `PitchDetectorService` (HPS) sau `YinPitchDetectorService` (YIN), in functie de `TunerSettings.ActivePitchAlgorithm` (Singleton) — inregistrare prin `RegisterPitchDetector`
- `IFrequencyStabilizer` → `FrequencyStabilizer` (Singleton)
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
| `TunerSettings.cs` | Creat (punct unic de reglaj — toti parametrii) |
| `Models/NoteResult.cs` | Creat |
| `Services/IAudioCaptureService.cs` | Creat |
| `Services/AudioCaptureService.cs` | Creat |
| `Services/IPitchDetectorService.cs` | Creat |
| `Services/PitchDetectorService.cs` | Creat (HPS — neatins de adaugarea YIN) |
| `Services/YinPitchDetectorService.cs` | Creat (detector alternativ YIN, independent) |
| `Services/IFrequencyStabilizer.cs` | Creat |
| `Services/FrequencyStabilizer.cs` | Creat |
| `Services/INoteAnalyzerService.cs` | Creat |
| `Services/NoteAnalyzerService.cs` | Creat |
| `Services/ISessionLogger.cs` | Creat |
| `Services/FileSessionLogger.cs` | Creat |
| `ViewModels/TunerViewModel.cs` | Modificat (proprietati NoteText/OctaveText/CentsText pentru afisaj pe coloane; smoothing-ul pe frecventa mutat in `FrequencyStabilizer`; deadband pe cents + EMA pe unghi; unghi ac ±50° pentru ±50 centi; eliminat butonul — metode `StartAsync`/`StopAsync` in loc de `ToggleListeningCommand`) |
| `Controls/AnalogMeterView.cs` | Modificat (grafica pe imagini: fundal + ac rotit din `Resources/Raw`; animatie lina a acului prin `IDispatcherTimer` la 60 FPS cu auto-stop in repaus) |
| `Resources/Raw/background.png` | Creat (fundal cadran VU, 900×1950; include titlul si caseta neagra de afisaj desenate in imagine) |
| `Resources/Raw/needle.png` | Creat (ac indicator, 20×284) |
| `MainPage.xaml` | Modificat (afisaj pe 3 coloane `*,2*,*` la `0.5,0.73`; doar text peste caseta neagra din imagine, fara `Border`; etichete laterale culoare `#C9B68A`; eliminat butonul Start/Stop) |
| `MainPage.xaml.cs` | Modificat (override `OnAppearing`/`OnDisappearing` pentru pornire/oprire automata a detectiei) |
| `Services/PitchDetectorService.cs` | Modificat (returneaza Hz brut, fara smoothing; parametrii citesc din `TunerSettings`; domeniu 60–1000 Hz: B1 → G5) |
| `Services/AudioCaptureService.cs` | Modificat (parametrii citesc din `TunerSettings`) |
| `Services/FrequencyStabilizer.cs` | Modificat (parametrii citesc din `TunerSettings`) |
| `Services/NoteAnalyzerService.cs` | Modificat (detectie cromatica via formula MIDI — nota cea mai apropiata + cents in [-50,+50]; inlocuieste tabelul de 6 corzi standard; suporta orice acordaj) |
| `MauiProgram.cs` | Modificat (fonts; `RegisterPitchDetector` alege HPS/YIN dupa `ActivePitchAlgorithm`) |
| `TunerSettings.cs` | Modificat (enum `PitchAlgorithm`; comutator `ActivePitchAlgorithm`; zona separata de parametri YIN) |
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

Deoarece proiectul ruleaza **neimpachetat** (`WindowsPackageType=None`), permisiunile din `Package.appxmanifest` nu se aplica automat. La pornirea aplicatiei (detectia incepe automat), Windows poate cere acces la microfon printr-un popup. Daca nu apare popup-ul, activeaza manual din:

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

- **Doi detectori interschimbabili (HPS si YIN)**: ambii implementeaza `IPitchDetectorService` si returneaza Hz brut, deci restul lantului (stabilizator, analiza nota, UI) e identic indiferent de algoritm. Selectia se face la compilare din `TunerSettings.ActivePitchAlgorithm`; `MauiProgram.RegisterPitchDetector` inregistreaza implementarea corecta in DI (copiaza const-ul intr-o variabila locala ca sa evite avertismentul de cod inaccesibil). HPS-ul (`PitchDetectorService`) e neatins de adaugarea YIN — sunt complet independenti. Scop: testarea ambelor abordari si alegerea finala. YIN foloseste doar ultimele `YinWindowSamples` esantioane din bufferul de captura (Optiunea 1), deci `AudioCaptureService` ramane neschimbat. YIN e O(N²) pe fereastra lui — de aceea fereastra e mica (4096) fata de cea de 32768 a HPS; trebuie sa ramana >= ~4 perioade din nota cea mai joasa (la 60 Hz, perioada ≈ 800 esantioane, deci 4096 e suficient).
- **Reglaje centralizate in `TunerSettings`**: toti parametrii reglabili (prag volum, range Hz, armonice, median, EMA, jump, hold, deadband, animatie ac, pivot etc.) sunt `const` intr-o singura clasa statica `TunerSettings`. Serviciile si controalele NU mai detin constante proprii — citesc de acolo. Pentru orice ajustare se modifica un singur fisier. Clasa e statica (nu prin DI) pentru ca trebuie accesibila si din `AnalogMeterView` (control XAML, neinjectabil prin container). Daca pe viitor se vrea un ecran de Setari in aplicatie, se transforma in POCO injectat cu proprietati.
- **Pragul de volum (start analiza)**: `TunerSettings.NoiseFloorRms`. Folosit in `PitchDetectorService.HasEnoughSignal` — sub acest RMS detectorul returneaza 0 (liniste). Mai mare = ignora sunete slabe (mai putine false detectii, dar coardele cu decay pot disparea mai repede); mai mic = mai sensibil.
- **HPS vs FFT simplu**: HPS (Harmonic Product Spectrum) inmulteste spectrul FFT cu versiunile sale comprimate la 1/2, 1/3, 1/4, 1/5. Fundamentala iese in evidenta deoarece coincide cu propriile armonice la toate compresiile. Fara HPS, FFT simplu detecta uneori E3 in loc de E2.
- **Corectie octava in HPS**: HPS poate detecta prima armonica (octava superioara) cand aceasta e mai puternica decat fundamentala (frecvent la D3, G3). Fix: dupa gasirea peak-ului HPS, cautam peak local in fereastra +/-2 bins in jurul `peakBin/2` (acopera leakage spectral) si comparam cu peak local in jurul `peakBin`. Coboram octava doar daca suboctava are >= 60% din amplitudine. Pragul mare (60%) si fereastra de cautare evita falsele coborari cauzate de zgomot sau leakage spectral — important pentru B3 (247 Hz) unde un prag mic ar coboara fals la B2 (123 Hz).
- **Suprapunere buffer**: bufferul glisant pastreaza 50% din datele anterioare la fiecare ciclu. Aceasta reduce artefactele de granita si mentine receptivitatea.
- **Strategie smoothing pe frecventa (in `FrequencyStabilizer`, 3 mecanisme)**:
  1. **Filtru median** pe ultimele 5 valori brute (`MedianWindow = 5`): mediana e robusta la outlieri — un artefact izolat (armonica, eroare de octava, zgomot) nu o afecteaza pentru ca difera de centru. E nevoie de >= 3 din 5 valori consecvent diferite ca mediana sa se mute. Asta a inlocuit vechea confirmare in 2 cicluri si rezolva "acul sare la pozitii departate de nota".
  2. **EMA** (`smooth = smooth * 0.6 + median * 0.4`) peste mediana pentru variatii mici — netezeste vibrato/drift. La salt mare (>15%, schimbare reala de coarda) face snap direct pe mediana pentru raspuns rapid.
  3. **Hold time** (`HoldCycles = 3`) cand RAW=0: frecventa ramane neschimbata cateva cicluri (~1s la 3 analize/sec), apoi se reseteaza la 0. Crucial pentru coardele inalte (B3, E4) care au decay fizic foarte scurt — fara hold, E4 ar dispărea instant dupa 1 ciclu de detectie. Valoarea (3) e corelata cu rata de update: la fereastra de 32768 esantioane (48000 Hz) sunt ~3 analize/sec, deci 3 cicluri ≈ 1s. Daca se schimba `AnalysisWindowSamples`, `HoldCycles` trebuie reglat ca sa pastreze ~1s.
  - De ce median + EMA (nu doar EMA): EMA *amesteca* outlierul in rezultat (trage acul), pe cand mediana il *ignora*. Combinatia da si respingere de artefacte (median) si netezire fina (EMA).
- **Stabilizare + animatie ac (2 niveluri)**: detectorul produce doar ~5 valori/secunda, deci miscarea directa a acului ar fi sacadata. Solutia are doua straturi complementare:
  1. **Nivel valoare (TunerViewModel)**: deadband pe cents (`< 2` centi → unghi 0, opreste tremurul cand e acordat) + EMA pe unghi (`AngleSmoothingAlpha = 0.6`) ca sa stabilizeze *unde* trebuie sa ajunga acul.
  2. **Nivel miscare (AnalogMeterView)**: `IDispatcherTimer` la 60 FPS interpoleaza lin unghiul desenat spre tinta (`AngleLerpFactor = 0.3`), deci acul *gliseaza* intre valori in loc sa teleporteze.
- **Buget mobil pentru animatie**: timer-ul de 60 FPS din `AnalogMeterView` porneste cand se schimba unghiul tinta si se **opreste automat** cand acul a ajuns (`AngleSettleThreshold = 0.05`). Astfel redesenarea costisitoare ruleaza doar in timpul tranzitiilor (cand canti), nu permanent — esential pentru baterie/CPU pe telefoane. Evita orice loop de animatie always-on.
- **Sample rate asumat**: 48000 Hz pe toate platformele — ales fiindca e rata NATIVA pe majoritatea microfoanelor de telefon (Android/iOS) si evita resampling-ul intern. Pentru domeniul chitarei (60–1000 Hz), o rata mai mare nu aduce niciun castig de detectie (Nyquist la 48000 = 24000 Hz, mult peste necesar) si ar inrautati rezolutia de frecventa la fereastra fixa. Daca microfonul ruleaza nativ la alta rata si plugin-ul nu onoreaza setarea, frecventele vor aparea proportional deplasate — verificabil din `tuner-session.log` (CENTS constant mare la o coarda cunoscuta). Latimea unui bin FFT = `SampleRate / AnalysisWindowSamples` = 48000/32768 ≈ 1,46 Hz (rezolutie bruta buna, plus interpolarea parabolica din `RefinePeakBin` care o aduce bine sub toleranta de 5 centi — relevant in special pentru precizia pe coarda joasa E2).
- **Range ac ±50 centi (calibrare asimetrica)**: cadranul desenat (`background.png`) are marcaje `-50 ... 0 ... +50`. `TunerViewModel.MapCentsToAngle` clampeaza cents la ±50 (`MaxCentsForNeedle = 50`) si mapeaza separat pe fiecare capat: `MaxNeedleAnglePositiveDegrees` (grade spre +50) si `MaxNeedleAngleNegativeDegrees` (grade spre -50), plus `NeedleAngleOffsetDegrees` pentru pozitia exacta a centrului. Cele doua amplitudini sunt separate fiindca grafica nu e neaparat simetrica — se regleaza vizual cu ajutorul `ForcedCentsForCalibration`.
- **Grafica pe imagini (AnalogMeterView)**: in loc de desen procedural, controlul incarca `background.png` si `needle.png` din `Resources/Raw` ca `SKImage` (o singura data, cache). Fundalul e scalat "contain" si centrat. Acul e desenat peste fundal cu transformarea `Translate(pivot) → Scale(scale) → RotateDegrees(NeedleAngle) → DrawImage(needle, -needleW/2, -needleH)`, deci pivotul de rotatie e baza acului. Pozitia pivotului in cadran = `PivotXFraction`/`PivotYFraction` (fractii din imagine), reglabile vizual.
- **De ce Resources/Raw si nu MauiImage**: imaginile trebuie pastrate la rezolutia reala (acul e deja la scara corecta fata de fundal). `MauiImage` ar aplica rescalare dupa DPI si ar strica raportul. `MauiAsset` (folderul `Resources/Raw`) pastreaza fisierul pixel-perfect, incarcabil cu `FileSystem.OpenAppPackageFileAsync`.
- **Caseta de afisaj e parte din imagine**: noua imagine de fundal include deja caseta neagra de afisaj (si titlul) desenate. In XAML NU se mai deseneaza niciun `Border` — peste fundal se aseaza doar text (nota/frecventa centrate peste caseta neagra). Sursa imaginii editate e in `Images/background_1950_900.png`; pentru a o folosi se copiaza peste `Resources/Raw/background.png` (acelasi nume de asset folosit de `AnalogMeterView`). Daca pozitia casetei sau a cadranului se schimba la o editare viitoare, se recalibreaza `LayoutBounds` din `MainPage.xaml` (centrul randului de afisaj, acum `0.5,0.73`) si pivotul acului din `AnalogMeterView.cs`.
- **Calibrare pivot/unghi**: `PivotXFraction = 0.5`, `PivotYFraction = 0.56`, `NeedleAngleOffsetDegrees`, `MaxNeedleAnglePositiveDegrees` si `MaxNeedleAngleNegativeDegrees` sunt valorile validate vizual — baza acului sta pe pivotul fizic al cadranului, centrul cade pe `0` si varful atinge `+50`/`-50` pe marcaje. Procedura: setezi `ForcedCentsForCalibration` pe rand la `+50`, `-50`, `0`, faci screenshot si ajustezi amplitudinile/offset-ul pana coincid cu marcajele. Daca se schimba imaginea de fundal, aceste valori trebuie recalibrate.
- **Hold time vs decay fizic**: coardele subtiri (B3 = 247 Hz, E4 = 330 Hz) au timp de decay fizic de ~3x mai scurt decat coardele groase (E2 = 82 Hz). Fara hold time, E4 ar fi afisat doar 200ms inainte ca RMS sa scada sub prag. Combinatia "prag RMS scazut la 0.003 + hold de 3 cicluri" extinde afisarea E4 de la ~200ms la ~1.2s, suficient pentru utilizator sa citeasca cents-urile.
- **Raport de aspect 19.5:9**: aplicatia este blocata in portret pe Android (atribut `ScreenOrientation.Portrait` in `MainActivity`) si iOS (doar `UIInterfaceOrientationPortrait` in `Info.plist`). Pe Windows, fereastra este fixata la 249×540 pixeli (540/249 ≈ 2.169 ≈ 19.5:9) prin `CreateWindow` in `App.xaml.cs` cu `MinimumWidth = MaximumWidth = 249` si `MinimumHeight = MaximumHeight = 540`. Utilizatorul nu poate redimensiona fereastra pe Windows.
- **Cum sa setezi NavBarIsVisible**: pentru a ascunde bara purpurie cu titlul aplicatiei (cand pagina e gazduita in Shell), foloseste `Shell.NavBarIsVisible="False"` pe `ContentPage` in XAML. Important pentru un look complet curat fara header.
- **Detectie cromatica (nu tabel de corzi)**: `NoteAnalyzerService` nu mai compara frecventa cu cele 6 corzi standard, ci o mapeaza pe cea mai apropiata nota din scala egal temperata prin formula MIDI (`midi = round(69 + 12*log2(f/440))`, `f_tinta = 440*2^((midi-69)/12)`). Avantaje: (1) afiseaza orice nota cromatica (D#, F#, C etc.), nu doar E/A/D/G/B; (2) centii sunt mereu in [-50, +50] fiindca se raporteaza la nota cea mai apropiata — cand depasesti jumatate de semiton, afisajul comuta automat pe nota vecina (-60 centi fata de E2 → D# la +40 centi); (3) suporta orice acordaj (Drop D, acordaj cu 2 semitonuri mai jos D2-G2-C3-F3-A3-D4) fara configurare. Referinta A4 e `TunerSettings.ReferencePitchHz` (440 Hz), reglabila pentru acordaje cu referinta diferita (ex: 432 Hz).
- **Domeniul de detectie 60–1000 Hz** (`MinFrequencyHz` / `MaxFrequencyHz`): ales ca sa acopere de la B1 (61.74 Hz — coarda joasa de chitara cu 7 corzi / bas) pana la G5 (783.99 Hz). Limita de jos 60 Hz sta putin sub B1 si lasa un tampon fata de hum-ul de retea de 50 Hz (in Romania), evitand detectii false din bazaitul electric. Limita de sus 1000 Hz prinde G5 inclusiv cand e +50 centi (~807 Hz) cu marja pentru cautarea peak-ului, dar taie zona inalta inutila (mai putine sanse sa agati o armonica drept fundamentala). Daca se vrea o coarda mai joasa (B0 = 30.87 Hz) sau note mai inalte, se ajusteaza cele doua constante. La frecvente joase rezolutia FFT e ≈1.46 Hz/bin la fereastra de 32768 (48000 Hz), iar interpolarea parabolica din `RefinePeakBin` o aduce sub toleranta de 5 centi — ales pentru precizie buna pe coarda joasa E2. Daca se vrea reactie mai rapida (cu pretul preciziei pe corzile groase), se poate scadea `AnalysisWindowSamples` la 16384 (latenta la jumatate, dar bin dublu si `HoldCycles` trebuie marit ca sa pastreze ~1s).
