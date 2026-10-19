# NINA 3.2 - note API per il plugin

Target: NINA 3.2.0.9001, `net8.0-windows7.0`. Data verifica: 2026-10-19.
Fonte unica: pacchetti NuGet locali in `packages/<nome>/3.2.0.9001/lib/net8.0-windows7.0/`.

## 0. Metodo e legenda

- Nei pacchetti non esiste nessun file `*.xml` di documentazione (controllati NINA.Plugin, Core, Image, Profile, Equipment, WPF.Base, Sequencer). Il tag [XML] quindi non compare.
- Le firme sono state lette dalle tabelle dei metadati ECMA-335 dei .dll con un lettore Python locale (sola lettura). Un membro marcato [DLL] esiste con quella firma nell'assembly; non dice nulla sul comportamento.
- Tag usati:
  - [DLL] firma letta dai metadati;
  - [DLL: grep] solo stringa presente nel binario, non prova una firma;
  - [LAB] codice o file del lab (`outputs/nina-plugin-lab`);
  - NON VERIFICATO.
- Dipendenze di `NINA.Plugin` 3.2.0.9001 (nuspec, net8.0-windows7.0): NINA.Astrometry, NINA.Core, NINA.Equipment, NINA.Image, NINA.PlateSolving, NINA.Profile, NINA.Sequencer, NINA.WPF.Base (tutte 3.2.0.9001); Microsoft.CSharp 4.7.0; NJsonSchema 11.3.2; System.Data.SqlClient 4.9.0; System.Net.Http 4.3.4; System.Text.RegularExpressions 4.3.1; Trinet.Core.IO.Ntfs 4.1.2. Framework reference: `Microsoft.WindowsDesktop.App.WPF`.

## 1. PluginBase e manifest

Namespace `NINA.Plugin` (classi) e `NINA.Plugin.Interfaces` (interfacce).

| Membro | Firma | Tag |
|---|---|---|
| `PluginBase` | `public class PluginBase`, implementa `IPluginManifest`, costruttore pubblico senza parametri | [DLL] |
| `Initialize` | `public virtual Task Initialize()` | [DLL] |
| `Teardown` | `public virtual Task Teardown()` | [DLL] |

Proprietà pubbliche solo get in `PluginBase`: `string Identifier`, `string Name`, `string License`, `string LicenseURL`, `string Author`, `string Homepage`, `string Repository`, `string ChangelogURL`, `string[] Tags`, `IPluginVersion Version`, `IPluginVersion MinimumApplicationVersion`, `IPluginInstallerDetails Installer`, `IPluginDescription Descriptions`.

| Interfaccia | Membri | Tag |
|---|---|---|
| `IPluginManifest` | `Initialize()`, `Teardown()` abstract; proprietà come sopra | [DLL] |
| `IPluginVersion` | `int Major`, `int Minor`, `int Patch`, `int Build` | [DLL] |
| `IPluginDescription` | `ShortDescription`, `LongDescription`, `FeaturedImageURL`, `ScreenshotURL`, `AltScreenshotURL` (string) | [DLL] |
| `IPluginInstallerDetails` | `string URL`, `InstallerType Type`, `string Checksum`, `InstallerChecksum ChecksumType` | [DLL] |

Enum in `NINA.Plugin.ManifestDefinition`: `InstallerType` = {DLL, ARCHIVE}; `InstallerChecksum` = {MD5, SHA1, SHA256}. [DLL]

Campi del manifest JSON (template del lab): `Name`, `Identifier` (GUID come stringa), `Author`, `License`, `LicenseURL`, `ChangelogURL`, `Repository`, `Homepage`, `Version{Major,Minor,Patch,Build}`, `MinimumApplicationVersion{...}`, `Tags[]`, `Descriptions{ShortDescription, LongDescription, FeaturedImageURL, ScreenshotURL, AltScreenshotURL}`, `Installer{URL, Type, Checksum, ChecksumType}`. [LAB] `plugins/HelloNina/manifest.template.json`. Il modello `PluginManifest` ha anche una proprietà `Schema` che il template non usa. [DLL]

- Nel template i numeri di versione sono stringhe (`"0"`). Che NINA li accetti come int è NON VERIFICATO (esiste un `ConcreteManifestConverter<T>` [DLL], ma il corpo non è letto).
- Il repository plugin è un endpoint `https://nighttime-imaging.eu/wp-json/nina/v1` [DLL: grep].

Export MEF (il plugin deve esportare esattamente un `IPluginManifest`):

```csharp
[Export(typeof(IPluginManifest))]
public sealed class HelloNinaPlugin : PluginBase
{
}
```

[LAB] `plugins/HelloNina/src/HelloNinaPlugin.cs`. Regola "un solo manifest per plugin": [LAB] `knowledge/extension-points.md`. Il namespace MEF `System.ComponentModel.Composition` è usato dal lab; `AttributedModelServices` compare nel binario di NINA.Plugin [DLL: grep].

Caricamento e cartella di installazione:

| Aspetto | Dato | Tag |
|---|---|---|
| Cartella | `%localappdata%\NINA\Plugins\<Major>.<Minor>.<Hotfix>`, quindi per 3.2 `%localappdata%\NINA\Plugins\3.2.0` | [DLL: grep] per il testo, [LAB] `knowledge/compatibility-matrix.md` per il valore |
| Loader | classe `PluginLoader` (NINA.Plugin), stringhe "valid plugins at" e "The dll inside the plugins folder failed to load" | [DLL: grep] |
| Costruttore `PluginLoader` | ~37 parametri: mediatori (camera, telescopio, focuser, ...), `IProfileService`, `IImageSaveMediator`, `IMessageBroker`, ecc. | [DLL] |
| Scansione DLL | solo radice e un sottolivello della cartella versione | [LAB] `knowledge/extension-points.md`, NON VERIFICATO nel codice |
| Metadati da file | `PluginAssemblyReader.GrabPluginMetaData(string assemblyPath)` -> `Dictionary<string,string>` | [DLL] |
| Costanti cartelle | `Constants.UserExtensionsFolder`, `StagingFolder`, `DeletionFolder`, `BaseUserExtensionsFolder`, ... (campi static string) | [DLL]; valori NON VERIFICATO |
| Assembly NINA a runtime | NINA fornisce i propri assembly; il pacchetto di release non dovrebbe includere le DLL NINA | [LAB] `knowledge/compatibility-matrix.md` |

Nota: il tipo di ritorno di `PluginLoader.Load()` è `Task` (virtual) e il tipo espone `Plugins` (`IDictionary<IPluginManifest,bool>`). [DLL]

## 2. IImageSaveMediator e ImageSaved

Interfaccia `NINA.WPF.Base.Interfaces.Mediator.IImageSaveMediator` [DLL]:

| Membro | Firma |
|---|---|
| `Enqueue` | `Task Enqueue(IImageData imageData, Task<IRenderedImage> prepareTask, IProgress<ApplicationStatus> progress, CancellationToken token)` |
| `Shutdown` | `void Shutdown()` |
| `BeforeImageSaved` | `event Func<object, BeforeImageSavedEventArgs, Task>` |
| `BeforeFinalizeImageSaved` | `event Func<object, BeforeFinalizeImageSavedEventArgs, Task>` |
| `ImageSaved` | `event EventHandler<ImageSavedEventArgs>` |

Implementazione: `NINA.WPF.Base.Mediator.ImageSaveMediator` (implementa `IImageSaveMediator` e `IMediator<IImageSaveController>`) [DLL]. Esiste anche `ImageSaveMediatorX86` [DLL].

`ImageSavedEventArgs` (`NINA.WPF.Base.Interfaces.Mediator`, deriva da `EventArgs`, costruttore pubblico senza parametri). Tutte le proprietà sono `get; set;` pubbliche. [DLL]

| Proprietà | Tipo | Note |
|---|---|---|
| `MetaData` | `NINA.Image.ImageData.ImageMetaData` | vedi tabella sotto |
| `Image` | `System.Windows.Media.Imaging.BitmapSource` | immagine renderizzata |
| `Statistics` | `NINA.Image.Interfaces.IImageStatistics` | |
| `StarDetectionAnalysis` | `NINA.Image.Interfaces.IStarDetectionAnalysis` | contiene HFR |
| `PathToImage` | `System.Uri` | percorso del file salvato |
| `FileType` | `NINA.Core.Enum.FileTypeEnum` | TIFF, FITS, XISF, TIFF_ZIP, TIFF_LZW, RAW |
| `IsBayered` | `bool` | |
| `Duration` | `double` | durata (unità NON VERIFICATO, probabilmente secondi) |
| `Filter` | `string` | nome filtro |

`ImageMetaData` (`NINA.Image.ImageData`) [DLL]: proprietà `Image` (ImageParameter), `Camera` (CameraParameter), `Telescope`, `Focuser`, `Rotator`, `FilterWheel`, `Target`, `Observer`, `WeatherData`, `GenericHeaders` (List di IGenericMetaDataHeader). Campi pubblici: `WorldCoordinateSystem`, `Sequence` (SequenceParameter, è un campo non una proprietà). Metodo `FromProfile(IProfile)`.

| Classe | Membri (tutti pubblici) |
|---|---|
| `ImageParameter` | `int Id`, `DateTime ExposureStart`, `DateTime ExposureMidPoint`, `int ExposureNumber`, `string ImageType`, `string Binning`, `double ExposureTime`, `RMS RecordedRMS`; metodo `SetExposureTimes(DateTime, DateTime)` |
| `CameraParameter` | `string Id`, `Name`, `Binning`; `int BinX`, `BinY`, `Gain`, `Offset`, `BayerOffsetX`, `BayerOffsetY`, `USBLimit`; `double PixelSize`, `Temperature`, `ElectronsPerADU`, `SetPoint`; `short ReadoutModeIndex`; `string ReadoutModeName`; `SensorType SensorType`; campo `BayerPatternEnum BayerPattern` |
| `TelescopeParameter` | `string Name`; `double FocalLength`, `FocalRatio`, `Altitude`, `Azimuth`, `Airmass`; `PierSide SideOfPier`; `Astrometry.Coordinates Coordinates` |
| `FocuserParameter` | `string Name`; `int? Position`; `double StepSize`, `Temperature` |
| `RotatorParameter` | `string Name`; `double MechanicalPosition`, `Position`, `StepSize` |
| `FilterWheelParameter` | `string Name`, `string Filter` |
| `TargetParameter` | `string Name`; `double Rotation`, `PositionAngle`; `Coordinates Coordinates` |
| `ObserverParameter` | `double Latitude`, `Longitude`, `Elevation`; `string Name`, `Observatory`, `Site` |
| `WeatherDataParameter` | `double CloudCover`, `DewPoint`, `Humidity`, `Pressure`, `SkyBrightness`, `SkyQuality`, `SkyTemperature`, `StarFWHM`, `Temperature`, `WindDirection`, `WindGust`, `WindSpeed` |
| `SequenceParameter` | `string Title` |

Statistiche e analisi stelle [DLL]:

- `IImageStatistics`: `int BitDepth`, `int Min`, `int Max`, `long MinOccurrences`, `long MaxOccurrences`; `double StDev`, `Mean`, `Median`, `MedianAbsoluteDeviation`; `ImmutableList<OxyPlot.DataPoint> Histogram`.
- `IStarDetectionAnalysis`: `double HFR`, `double HFRStDev`; `int DetectedStars`; `List<NINA.Image.ImageAnalysis.DetectedStar> StarList`.

Mappatura ai dati richiesti (filtro, target, durata, camera, gain, offset, binning, HFR):

| Dato | Dove | Tag |
|---|---|---|
| filtro | `ImageSavedEventArgs.Filter` oppure `MetaData.FilterWheel.Filter` | [DLL] |
| target | `MetaData.Target.Name` | [DLL] |
| durata | `ImageSavedEventArgs.Duration` oppure `MetaData.Image.ExposureTime` | [DLL] |
| camera | `MetaData.Camera.Name` / `Id` | [DLL] |
| gain, offset | `MetaData.Camera.Gain`, `MetaData.Camera.Offset` (anche `IImageData.Properties.Gain/Offset`) | [DLL] |
| binning | `MetaData.Camera.BinX`, `BinY`, `Binning`; `MetaData.Image.Binning` | [DLL] |
| HFR | `ImageSavedEventArgs.StarDetectionAnalysis.HFR` | [DLL] |

- Quando `Statistics` e `StarDetectionAnalysis` sono valorizzati (possono essere null) e su quale thread viene sollevato `ImageSaved`: NON VERIFICATO.
- Il lab non sottoscrive questo evento: nessun esempio d'uso [LAB].

## 3. PluginOptionsAccessor

- Classe `NINA.Profile.PluginOptionsAccessor`, implementa `NINA.Profile.Interfaces.IPluginOptionsAccessor` [DLL].
- Costruttore: `PluginOptionsAccessor(IProfileService profileService, Guid pluginGuid)` [DLL].
- `static Guid? GetAssemblyGuid(Type type)` [DLL]. Ipotesi: legge il `[assembly: Guid(...)]`; corpo NON VERIFICATO. Il lab ha `[assembly: Guid("91f0d3a6-...")]` in `Properties/AssemblyInfo.cs` [LAB].
- Coppie Get/Set (`name` è la chiave) [DLL]:

| Tipo | Metodi |
|---|---|
| bool, byte, sbyte, char, decimal, double, float (`Single`) | `SetValueX(string name, T value)` / `T GetValueX(string name, T defaultValue)` |
| int, uint, long, ulong, short, ushort | idem (`SetValueInt32`, `GetValueInt32`, ecc.) |
| string, DateTime, Guid | idem (`SetValueString`, `GetValueString`, ...) |
| `System.Windows.Media.Color` | `SetValueColor(string, Color)`, `Color GetValueColor(string, Color)` |
| enum | `void SetValueEnum<T>(string name, T value)`, `T GetValueEnum<T>(string name, T defaultValue)` |

Esempio di firma: `int GetValueInt32(string name, int defaultValue)`, `void SetValueInt32(string name, int value)`.

- Persistenza: `IProfile.PluginSettings` è di tipo `IPluginSettings`, implementata da `PluginSettings` con `SetValue(Guid pluginId, string key, T value)` e `bool TryGetValue(Guid pluginId, string key, out T value)` [DLL]. Quindi le opzioni risiedono nel profilo attivo, indicizzate per GUID del plugin. Che sia proprio questo il percorso usato da `PluginOptionsAccessor` è NON VERIFICATO (il corpo non è letto).
- Profilo attivo: `IProfileService.ActiveProfile` (vedi sezione 4).
- Lab: non usa `PluginOptionsAccessor`; nessun esempio [LAB].

## 4. IProfileService e IProfile

- Interfaccia `NINA.Profile.Interfaces.IProfileService` [DLL]; implementazione `NINA.Profile.ProfileService` [DLL].
- Proprietà: `IProfile ActiveProfile`; `AsyncObservableCollection<ProfileMeta> Profiles`; `bool ProfileWasSpecifiedFromCommandLineArgs`.
- Eventi, tutti di tipo `System.EventHandler` senza argomenti tipizzati: `LocaleChanged`, `LocationChanged`, `BeforeProfileChanging`, `ProfileChanged`, `HorizonChanged`. [DLL]
- Metodi: `bool SelectProfile(ProfileMeta)`, `bool RemoveProfile(ProfileMeta)`, `bool Clone(ProfileMeta)`, `void Add()`, `void ChangeLocale(CultureInfo)`, `ChangeLatitude(double)`, `ChangeLongitude(double)`, `ChangeElevation(double)`, `ChangeHorizon(string)`, `void Release()`. [DLL]
- `IProfile` [DLL]:
  - `Guid Id`, `string Name`, `string Description`, `string Location`, `DateTime LastUsed`; metodo `void Save()`.
  - Sottoimpostazioni: `CameraSettings`, `TelescopeSettings`, `FocuserSettings`, `FilterWheelSettings`, `RotatorSettings`, `ImageSettings`, `ImageFileSettings`, `ApplicationSettings`, `PluginSettings`, `SequenceSettings`, ecc.
- Dispositivi nel profilo [DLL]:
  - `ICameraSettings`: `string Id`, `string LastDeviceName`.
  - `ITelescopeSettings`: `string Name`, `string Id`, `string LastDeviceName`.
  - `IFilterWheelSettings`: `string Id`, `string LastDeviceName`.
  - Il significato di `Id` (ProgID ASCOM, nome driver o altro) è NON VERIFICATO.
- Consiglio: per dati di una specifica immagine usare `ImageSavedEventArgs.MetaData` e non il profilo corrente, che cambia con il cambio profilo. Il nome del filtro per l'immagine è in `FilterWheel.Filter` [DLL].

## 5. Miniatura JPEG

API disponibili [DLL]:

| Tipo | Membro | Firma | Sincrono/asincrono |
|---|---|---|---|
| `IImageData` | `RenderImage()` | `IRenderedImage RenderImage()` | sincrono |
| `IImageData` | `RenderBitmapSource()` | `BitmapSource RenderBitmapSource()` | sincrono |
| `IImageData` | `Data`, `Properties`, `Statistics` | `IImageArray`, `ImageProperties` (Width, Height, BitDepth, IsBayered, Gain, Offset), `AsyncLazy<IImageStatistics>` | proprietà |
| `IImageDataFactory` | `CreateFromFile` | `Task<IImageData> CreateFromFile(string path, int bitDepth, bool isBayered, RawConverterEnum rawConverter, CancellationToken ct)` | asincrono |
| `IRenderedImage` | `Image`, `OriginalImage`, `RawImageData` | `BitmapSource`, `BitmapSource`, `IImageData` | proprietà |
| `IRenderedImage` | `Stretch` | `Task<IRenderedImage> Stretch(double factor, double blackClipping, bool unlinked)` | asincrono |
| `IRenderedImage` | `GetThumbnail` | `Task<BitmapSource> GetThumbnail()` | asincrono |
| `IRenderedImage` | `DetectStars` | `Task<IRenderedImage> DetectStars(bool annotateImage, StarSensitivityEnum sensitivity, NoiseReductionEnum noiseReduction, CancellationToken, IProgress<ApplicationStatus>)` | asincrono |
| `IRenderedImage` | `ReRender`, `Debayer` | sincroni | sincrono |
| `RenderedImage` | `static Create(...)`, `static FromBitmapSource(...)` | richiedono `IProfileService` e servizi di stella: uso da plugin NON VERIFICATO | |

Percorso consigliato:

1. Usare `ImageSavedEventArgs.Image` (un `BitmapSource` già pronto) invece di rifare il render, che richiede i servizi interni.
2. Ridurre la dimensione con WPF (ad esempio `TransformedBitmap`) e codificare con `JpegBitmapEncoder` su un `FileStream`. Queste classi sono della piattaforma WPF di .NET 8 e non sono nei pacchetti NINA: uso e comportamento NON VERIFICATO in questo contesto.
3. Congelare il `BitmapSource` (`Freeze()`) prima di usarlo su un altro thread: NON VERIFICATO, è la prassi WPF standard.

- Lo stretch automatico: `Stretch(factor, blackClipping, unlinked)` accetta i parametri, ma i valori predefiniti usati da NINA (profilo o altro) sono NON VERIFICATO.
- Il lab non crea miniature [LAB].

## 6. Logger

Classe statica `NINA.Core.Utility.Logger` (`abstract sealed`), assembly NINA.Core [DLL]:

| Metodo | Firma |
|---|---|
| `SetLogLevel` | `static void SetLogLevel(LogLevelEnum logLevel)` |
| `IsEnabled` | `static bool IsEnabled(LogLevelEnum level)` |
| `CloseAndFlush` | `static void CloseAndFlush()` |
| `Error` | `static void Error(Exception ex, string memberName, string sourceFilePath, int lineNumber)` |
| `Error` | `static void Error(string customMsg, Exception ex, string memberName, string sourceFilePath, int lineNumber)` |
| `Error` | `static void Error(string message, string memberName, string sourceFilePath, int lineNumber)` |
| `Warning`, `Info`, `Debug`, `Trace` | `static void X(string message, string memberName, string sourceFilePath, int lineNumber)` |

- I tre parametri finali hanno gli attributi `[CallerMemberName]`, `[CallerFilePath]`, `[CallerLineNumber]` (verificato sui metadati). Quindi la chiamata è `Logger.Info("testo");`. I valori predefiniti dei parametri non sono stati letti: NON VERIFICATO.
- Il nome `Debug` collide con `System.Diagnostics.Debug` se si importa quel namespace.
- Percorso dei log: `%localappdata%\NINA\Logs` [DLL: grep].
- Il lab usa `Notification.ShowSuccess` (NINA.Core.Utility.Notification), non il Logger [LAB].

## 7. Pagina Opzioni WPF

- Convenzioni di chiave trovate nel binario di NINA.WPF.Base: suffissi `_Options`, `_Mini`, `_Dockable` [DLL: grep].
- Nel file del lab: `<NomeManifest>_Options` per le opzioni, `<FullyQualifiedTypeName>_Mini` per il template compatto [LAB] `knowledge/extension-contracts.md` (riferimento, non verificato nei sorgenti NINA). Esempio nel lab: `HelloNina.Sequence.HelloInstruction_Mini` in `plugins/HelloNina/src/Resources/PluginTemplates.xaml`.
- Se `<NomeManifest>` sia il campo `Name` del manifest (con spazi) o il nome dell'assembly: NON VERIFICATO.
- Il ResourceDictionary viene esportato così [LAB]:

```csharp
[Export(typeof(ResourceDictionary))]
public partial class PluginTemplates : ResourceDictionary
{
    public PluginTemplates() { InitializeComponent(); }
}
```

- Il ViewModel della pagina opzioni: `IOptionsVM` esiste nel namespace `NINA.WPF.Base.Interfaces.ViewModel` [DLL], ma i membri letti sono solo `AddImagePattern(ImagePattern)`. Non è verificato come si implementa una pagina opzioni di plugin: NON VERIFICATO.
- Esempio XAML di chiave (struttura ricalcata sul lab, chiave e DataType NON VERIFICATI):

```xml
<DataTemplate x:Key="<NomeManifest>_Options" DataType="{x:Type local:MyOptionsVM}">
    <!-- controlli -->
</DataTemplate>
```

## 8. csproj, NuGet e target

Configurazione del lab (sola lettura):

- `Directory.Build.props`: `NinaTargetFramework=net8.0-windows7.0`, `NinaPluginPackageVersion=3.2.0.9001`, `NinaMinimumApplicationVersion=3.2.0.9001`, `NinaPluginFolderVersion=3.2.0`, `Deterministic=true`. Importa `config/nina-target.props` [LAB].
- `nuget.config`: solo `https://api.nuget.org/v3/index.json` [LAB].
- CI: `actions/setup-dotnet` con `8.0.x`, `dotnet restore ... --configfile nuget.config`, poi `dotnet build ... --configuration Release --no-restore` [LAB] `.github/workflows/ci.yml`.

csproj minimo (dal lab, `plugins/HelloNina/src/HelloNina.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows7.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NINA.Plugin" Version="3.2.0.9001" />
  </ItemGroup>
</Project>
```

- Il lab usa `$(NinaTargetFramework)` e `$(NinaPluginPackageVersion)` al posto dei valori letterali [LAB].
- Effetto di `exclude="Build,Analyzers"` sulle dipendenze: NON VERIFICATO sulla copia locale. La regola del lab è di non includere le DLL NINA nel pacchetto di release [LAB].

## 9. Non verificato (riepilogo)

- Thread di `ImageSaved` e valorizzazione di `Statistics` / `StarDetectionAnalysis`.
- Unità di `Duration`.
- Modo in cui NINA inietta i servizi (mediatori, `IProfileService`) in un plugin: il costruttore di `PluginLoader` li riceve [DLL], ma non è verificato che un plugin li riceva via `[ImportingConstructor]`. Il lab usa solo il costruttore senza parametri.
- Chiave esatta del DataTemplate delle opzioni (manifest Name o assembly).
- Corpo di `PluginOptionsAccessor` (persistenza per profilo) e di `GetAssemblyGuid`.
- Valori di default di `Stretch` e dei parametri di `Logger`.
- Accettazione dei numeri di versione come stringa nel manifest JSON.
- Esempi d'uso per `ImageSaved`, `PluginOptionsAccessor`, `IProfileService` e `Logger`: non presenti nel lab.
