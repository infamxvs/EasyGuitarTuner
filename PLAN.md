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
    -> MainPage                 (AnalogMeterView + Label Hz + Label nota/cents)
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
- Proprietati bindabile: `FrequencyText`, `StatusText`, `NeedleAngle`, `ToggleButtonText`, `IsListening`
- `StatusText`:
  - Cand exista semnal: `"B3   +4.9 cents"` (nota + octava + deviatie)
  - Cand nu exista semnal: `"Incepeti sa cantati"` daca asculta, `"Apasati Start"` altfel
- `FrequencyText`: ex `"247.6 Hz"` sau `"0.0 Hz"` cand nu se detecteaza
- Se aboneaza la `OnAudioCaptured` din `AudioCaptureService`
- Strategie de smoothing pe frecventa detectata (confirmare + EMA + hold):
  - **Cold start / Saltea mare** (`|RAW - SMOOTH| / SMOOTH > 0.15`, JumpThreshold=0.15): noul RAW intra in `_pendingFrequency` — acul NU se misca inca. Doar daca urmatorul ciclu confirma cu o frecventa similara (`< 5%` diferenta, SimilarityThreshold=0.05), `SMOOTH = RAW` (snap). Filtreaza glitch-urile izolate de 1 ciclu.
  - **Variatii mici** (sub 15%): EMA cu `SmoothingAlpha = 0.4` — raspuns rapid la vibrato/drift fara saltari abrupte.
  - **Hold time** la linistire (RAW=0 si SMOOTH>0): incrementeaza `_silenceCount`. Cat timp e sub `HoldCycles = 3`, SMOOTH ramane neschimbat (acul si afisajul stau fixe pe ultima nota). La al 3-lea ciclu de liniste, reseteaza `SMOOTH = 0`, `_pendingFrequency = 0`, `_silenceCount = 0`. Crucial pentru coardele inalte (B3, E4) cu decay fizic scurt.
  - Cost: ~200ms latenta la inceputul fiecarei note noi (confirmare) + ~400ms persistenta vizuala dupa atenuare (2 cicluri vizibile inainte de reset). Castig: glitch-uri eliminate si afisare suficient de lunga pentru toate cele 6 coarde (E4 trece de la ~200ms la ~1.2s afisaj).
- Logheaza pentru fiecare ciclu si **RMS-ul real** (util pentru diagnoza)
- `MapCentsToAngle`: mapeaza cents [-100, +100] la unghi ac [-90, +90] grade
- Orchestreaza `PitchDetectorService`, `NoteAnalyzerService` si `ISessionLogger`

---

### Controale UI

**`Controls/AnalogMeterView.cs`** — SkiaSharp custom control (tema light, design modern)
- Fundal transparent (pagina ofera fundalul deschis `#F2F5F8`)
- Arc semicircular gros (8.5% din raza) cu capete rotunjite, culoare gri-albastrui `#DCE3EB`
- Segment albastru solid `#2E80E0` peste arc, in zona "in tune" (±15 centi)
- Pana albastra translucida (rgba 46,128,224,38) — triunghi de la pivot pana la arc, in aceeasi zona ±15 centi
- Tick marks interioare:
  - Minor (1.2px) la fiecare 5 centi
  - Major (1.6px, mai lung) la fiecare 20 centi, cu eticheta numerica (-100, -80, -60, -40, -20, +20, +40, +60, +80, +100)
  - Tick-urile sunt suprimate in zona |cents| < 15 pentru a evita aglomerarea pe pana albastra
- Ac negru `#111827` subtire (1.8% din raza), drept, pleaca din pivot
- Pivot: cerc plin negru (5.5% din raza)
- Range cents: ±100, mapat la unghi ±90 grade
- **Calcul raza** care garanteaza incadrarea completa in panza tinand cont de stroke-ul gros si pivot:
  - `sideMaxRadius = (centerX - pad) / (1 + strokeHalf)`
  - `heightMaxRadius = (height - 2*pad) / (1 + strokeHalf + pivotFraction)`
  - `radius = min(sideMaxRadius, heightMaxRadius)` — limita binding
  - `centerY = pad + radius * (1 + strokeHalf)` — exact 8px margine sus la marginea exterioara a arcului
- Singura proprietate bindabila: `NeedleAngle`

---

### Pagini

**`MainPage.xaml`** + **`MainPage.xaml.cs`**
- Tema deschisa: fundal `#F2F5F8`
- `AnalogMeterView` acopera intregul ecran (canvas full-screen), pivotul acului la `height/2` = centrul ecranului
- Frecventa mare bold `#111827` si status `#6B7280` suprapuse in jumatatea de jos via `VerticalStackLayout VerticalOptions="End"`
- Buton Start / Stop albastru `#2E80E0` cu text alb, corner radius 12

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
| `ViewModels/TunerViewModel.cs` | Modificat (smoothing cu confirmare + EMA + hold time, log RMS) |
| `Controls/AnalogMeterView.cs` | Modificat (tema light minimalist, calcul raza precis) |
| `MainPage.xaml` | Modificat (tema light, layout simplu, NavBar ascuns) |
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
| `SkiaSharp.Views.Maui.Controls` | 3.119.4 | Deseneaza interfata grafica (ac, arc) |
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
- **Range ac ±100 centi**: o nota chitara fata de vecina cea mai apropiata (3-4 semitonuri ≈ 300-400 centi) e mereu clampata la ±100 vizual. Util pentru a vedea "cat de aproape" esti — daca acul e la marginea arcului, mai trebuie sa intinzi/slabesti mult; daca e in pana albastra (±15 centi), esti acordat.
- **Tema vizuala light**: design modern minimalist — fundal `#F2F5F8`, arc gri `#DCE3EB`, accent albastru `#2E80E0`, ac negru. Pana albastra translucida + segment solid albastru pe arc indica zona "in tune".
- **Calcul corect al razei in AnalogMeterView**: canvas-ul acopera intregul ecran. `centerY = height / 2` (pivotul la centrul ecranului). Raza este `min(sideMaxRadius, topMaxRadius)` unde `sideMaxRadius = (centerX - pad) / (1 + strokeHalf)` si `topMaxRadius = (centerY - pad) / (1 + strokeHalf)`. Arcul semicircular se extinde in sus din pivot, in jumatatea superioara a ecranului. Labelurile si butonul sunt suprapuse in jumatatea inferioara fara overlap vizual.
- **Hold time vs decay fizic**: coardele subtiri (B3 = 247 Hz, E4 = 330 Hz) au timp de decay fizic de ~3x mai scurt decat coardele groase (E2 = 82 Hz). Fara hold time, E4 ar fi afisat doar 200ms inainte ca RMS sa scada sub prag. Combinatia "prag RMS scazut la 0.003 + hold de 3 cicluri" extinde afisarea E4 de la ~200ms la ~1.2s, suficient pentru utilizator sa citeasca cents-urile.
- **Raport de aspect 19.5:9**: aplicatia este blocata in portret pe Android (atribut `ScreenOrientation.Portrait` in `MainActivity`) si iOS (doar `UIInterfaceOrientationPortrait` in `Info.plist`). Pe Windows, fereastra este fixata la 249×540 pixeli (540/249 ≈ 2.169 ≈ 19.5:9) prin `CreateWindow` in `App.xaml.cs` cu `MinimumWidth = MaximumWidth = 249` si `MinimumHeight = MaximumHeight = 540`. Utilizatorul nu poate redimensiona fereastra pe Windows.
- **Cum sa setezi NavBarIsVisible**: pentru a ascunde bara purpurie cu titlul aplicatiei (cand pagina e gazduita in Shell), foloseste `Shell.NavBarIsVisible="False"` pe `ContentPage` in XAML. Important pentru un look complet curat fara header.
