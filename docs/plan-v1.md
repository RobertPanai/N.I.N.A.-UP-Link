# My Universe Uplink — Piano tecnico plugin NINA → robertpanai.com

> Documento operativo destinato a un LLM che dovrà implementare il plugin.
> Versione piano: 1.0 — 2026-09-10
> Repository sito: `/Users/robertpanai/Documents/web` (Next.js 16.2.6, App Router, React 19, TS strict)
> Repository plugin: **nuovo**, separato (`nina-my-universe-uplink`)

---

## Indice

| § | Contenuto | Corrisponde alla richiesta |
|---|---|---|
| 0 | Istruzioni per l'LLM esecutore | — |
| 1 | Contesto reale del sito (vincoli verificati nel codice) | — |
| 2 | Requisiti funzionali | 1 |
| 3 | Requisiti non funzionali | 2 |
| 4 | Architettura proposta + ADR motivati | 3 |
| 5 | Struttura di cartelle e classi | 4 |
| 6 | Ciclo di vita del plugin | 5 |
| 7 | Gestione dell'evento ImageSaved | 6 |
| 8 | Modello del payload | 7 |
| 9 | Autenticazione e credenziali | 8 |
| 10 | Coda, retry, timeout, offline | 9 |
| 11 | Duplicati | 10 |
| 12 | Configurazione WPF | 11 |
| 13 | Advanced Sequencer | 12 |
| 14 | Logging e diagnostica | 13 |
| 15 | Strategia di test | 14 |
| 16 | Compatibilità NINA | 15 |
| 17 | Build, packaging, installazione | 16 |
| 18 | Rischi ed evoluzioni | 17 |
| 19 | **Roadmap per fasi (0-8) con Definition of Done** | — |
| 20 | Domande aperte (Q1-Q20) | 18 |

---

## 0. Come usare questo piano (istruzioni per l'LLM esecutore)

Questo piano è la specifica. Prima di scrivere codice:

1. **Non inventare API di NINA.** Le firme esatte di `IImageSaveMediator`, `ImageSavedEventArgs`,
   `PluginBase`, `PluginOptionsAccessor`, `SequenceItem` **vanno verificate** contro:
   - il package NuGet `NINA.Plugin` effettivamente referenziato (leggi i `.xml` di documentazione
     e, se serve, ispeziona gli assembly con `ildasm`/ILSpy);
   - il codice di `daleghent/nina-ground-station` (riferimento vivo e aggiornato);
   - il template ufficiale plugin NINA.
   Il primo deliverable della Fase 0 è `docs/nina-api-notes.md`, che riporta le firme **copiate**
   dalla fonte, con versione del package. Ogni successivo uso di un'API NINA deve citare quella nota.
2. **Ordine obbligatorio delle fasi.** Il server (sito) viene prima del plugin: il plugin ha bisogno
   di un endpoint reale contro cui essere testato, e l'endpoint è testabile con `curl` senza Windows.
3. **Ogni fase ha una Definition of Done verificabile.** Non passare alla fase successiva finché
   la DoD non è soddisfatta e i test passano.
4. **Nessuna dipendenza NuGet superflua nel plugin.** Il plugin gira dentro il processo di NINA e
   condivide il suo AssemblyLoadContext: una dipendenza in conflitto (Newtonsoft.Json, Serilog,
   client HTTP di terze parti) causa fallimenti difficili da diagnosticare. Usare solo la BCL di
   .NET 8 (`System.Text.Json`, `HttpClient`, `System.Threading.Channels`, `System.Security.Cryptography`).
   Ogni dipendenza aggiuntiva richiede una riga di giustificazione in `docs/dependencies.md`.
5. **Lingua.** Codice, identificatori, commenti e log in inglese. UI del plugin in inglese
   (NINA è in inglese). Documentazione di progetto in italiano.
6. **Il sito è un repo separato dal plugin.** Le modifiche lato sito vanno nel repo `web` e seguono
   le regole di `PROJECT.md` / `AGENTS.md` (Next.js 16, `params` e `searchParams` sono `Promise`,
   leggere `node_modules/next/dist/docs/` prima di toccare routing).

### Nomi canonici da usare ovunque

| Elemento | Valore |
|---|---|
| Nome visualizzato plugin | `My Universe Uplink` |
| Namespace root | `RobertPanai.MyUniverse.Uplink` |
| GUID plugin | da generare una sola volta in Fase 0 e **mai più cambiare** (identifica le impostazioni per profilo) |
| Prefisso endpoint sito | `/api/uplink/...` |
| Nome env token sito | `UPLINK_INGEST_TOKENS` |
| Versione schema payload | `schemaVersion: 1` |

---

## 1. Contesto reale del sito (vincoli non negoziabili)

Verificato leggendo il codebase, non assunto:

- **Non esiste alcuna route API.** `src/app/` contiene solo pagine; `src/app/admin/route.ts` è un
  semplice redirect. Tutta l'infrastruttura di ingest va creata da zero.
- **La dashboard sessioni legge un CSV statico.** `src/lib/session-dashboard.ts` legge
  `integration.csv` in root (11.994 righe) tramite `readFile`, lo parsa a mano e aggrega a ogni
  richiesta. Il percorso è sovrascrivibile con `SESSION_INTEGRATION_CSV`.
- **Le colonne del CSV sono già esattamente i metadati NINA** che il plugin dovrà inviare:
  `Target Name, Target RA, Target Dec, Target Rotation, Camera Name, Telescope Name, Filter,
  Duration, Gain, Offset, Binning, Captured Timestamp (UTC)`.
  → **Il plugin è la versione live di un export che oggi è manuale.** Questo è il valore principale.
- **Il tipo interno è `SessionFrame`** (`subject, filter, exposureSeconds, cameraName,
  telescopeName, capturedAt, date`): il modello dati lato sito deve restare un superset di questo,
  così la pagina non va riscritta.
- **I contenuti galleria sono git-backed**: `public/images/gallery/{categoria}/{slug}/object.md`
  con `integration_total` e `integration_filters` scritti a mano (oggi tramite l'app Electron
  `tools/gallery-uploader`, che committa sul repo con `git` via `execFile`).
  → Un commit git **per frame** è fuori discussione (500 frame/notte = 500 build Vercel).
  I frame vanno in uno store runtime; il git resta per i contenuti editoriali.
- **Deploy target: Vercel Hobby.** Function runtime Node.js (Fluid Compute), body fino a 100 MB,
  timeout 300 s. Nessun database provisionato oggi.
- **Immagini opzionalmente su CDN** via `CDN_IMAGES_BASE` (R2). `next.config.ts` autorizza già
  `**.r2.dev` e `**.cloudflare.com` in `images.remotePatterns`.

---

## 2. Requisiti funzionali

### Plugin (lato Windows / NINA)

| ID | Requisito | Fase |
|---|---|---|
| FR-01 | Il plugin si carica in NINA 3.x, espone nome/versione/autore/descrizione e una pagina Options. | 1 |
| FR-02 | Configurazione **per profilo NINA**: URL base API, token, abilitazione, tipi immagine, opzioni thumbnail. | 3 |
| FR-03 | Il token API è cifrato a riposo con DPAPI (`DataProtectionScope.CurrentUser`) e mai loggato. | 3 |
| FR-04 | Il plugin si sottoscrive a `ImageSaved` e costruisce un record frame per ogni immagine salvata. | 3 |
| FR-05 | Filtro per tipo immagine: LIGHT abilitato di default; SNAPSHOT/FLAT/DARK/BIAS opzionali. | 3 |
| FR-06 | Raccolta metadati: target (nome, RA, Dec, rotazione), attrezzatura (camera, pixel size, telescopio, focale, f/, scala immagine), esposizione (filtro, durata, gain, offset, binning, temperatura sensore), qualità (HFR, HFR StDev, star count, mean, median, stdev, RMS guida), contesto (altezza, azimut, airmass, meteo/SQM se disponibili). | 3 |
| FR-07 | Generazione thumbnail JPEG (lato lungo configurabile, default 400 px, qualità 70) con stretch applicato, opzionale e con policy di campionamento. | 3 |
| FR-08 | Invio asincrono via coda in background: `ImageSaved` non deve mai bloccare NINA né la sequenza. | 3 |
| FR-09 | Coda **persistente su disco**: sopravvive a chiusura di NINA, crash, riavvio del PC. | 4 |
| FR-10 | Retry con backoff esponenziale + jitter, rispetto di `Retry-After`, classificazione errori retriabili/non retriabili, dead-letter dopo N tentativi. | 4 |
| FR-11 | Modalità offline: se la rete manca, i frame restano in coda e vengono inviati alla riconnessione, entro limiti di età/dimensione configurabili. | 4 |
| FR-12 | Idempotenza: ogni frame ha un `frameId` deterministico; reinvii non duplicano dati lato sito. | 2 |
| FR-13 | Pulsanti UI: *Test connection*, *Send last image*, *Flush queue now*, *Clear dead letters*, *Open log folder*. | 3-4 |
| FR-14 | Pannello di stato nella UI: coda (pending/inflight/dead), ultimo invio riuscito, ultimo errore, frame inviati nella notte corrente. | 4 |
| FR-15 | Istruzione Advanced Sequencer `Send Site Event` (session start/end, target start/end, nota libera). | 6 |
| FR-16 | Logging su file dedicato del plugin + integrazione con il logger di NINA. | 3 |
| FR-17 | Negoziazione capacità: all'avvio e ogni N ore il plugin chiama `/api/uplink/health` e adatta batch size, thumbnail on/off, limiti. | 4 |

### Sito (lato Next.js / Vercel)

| ID | Requisito | Fase |
|---|---|---|
| FR-20 | `POST /api/uplink/frames` accetta un batch di frame autenticato, idempotente, e risponde con esito per frame. | 1 |
| FR-21 | `GET /api/uplink/health` restituisce versione schema e capacità del server (batch max, thumbnail sì/no, limiti byte). | 1 |
| FR-22 | Autenticazione a token opaco per dispositivo, revocabile, confrontato in modo timing-safe. | 1 |
| FR-23 | Persistenza dei frame in uno store durevole con deduplica su `frameId` e su chiave naturale. | 1 |
| FR-24 | Import una-tantum di `integration.csv` nello store, con id sintetici e deduplica verso i frame futuri. | 5 |
| FR-25 | `session-dashboard.ts` legge dallo store con fallback al CSV; la pagina `/sessioni-astronomiche` non cambia contratto. | 5 |
| FR-26 | Thumbnail salvate in blob storage privato; esposte solo tramite route del sito. | 5 |
| FR-27 | Sezione/box "ultima ripresa" sul sito (ultimo frame + target + filtro + ora), aggiornata a ogni ingest. | 5 |
| FR-28 | `POST /api/uplink/events` per eventi di sessione dall'Advanced Sequencer. | 6 |
| FR-29 | Compattazione periodica (cron) dei batch in rollup per notte, per mantenere veloce la dashboard. | 5 |
| FR-30 | (Opzionale) Calcolo automatico di `integration_total` / `integration_filters` per soggetto galleria a partire dai frame, proposto come PR GitHub. | 8 |

---

## 3. Requisiti non funzionali

| ID | Requisito | Criterio di verifica |
|---|---|---|
| NFR-01 | **Zero impatto sulla sequenza.** Il tempo speso nell'handler `ImageSaved` sul thread chiamante < 50 ms p95 (solo copia metadati + enqueue). Thumbnail e rete avvengono nel worker. | Misura con `Stopwatch` loggata a livello Debug. |
| NFR-02 | **Nessuna perdita dati.** Un frame accettato dalla coda o viene consegnato o finisce in dead-letter ispezionabile. Mai scartato silenziosamente. | Test: kill del processo tra enqueue e invio → al riavvio il frame è ancora in coda. |
| NFR-03 | **Nessun `async void`** fuori dagli event handler WPF; ogni operazione async accetta un `CancellationToken`. | Analyzer + code review. |
| NFR-04 | **Memoria contenuta**: la coda non tiene i pixel in RAM; le thumbnail vanno su disco nello spool. Picco < 50 MB per il plugin. | Profilazione manuale su una notte simulata. |
| NFR-05 | **Sicurezza**: token mai in chiaro su disco, mai nei log, mai nei messaggi di errore UI; HTTPS obbligatorio (rifiuto di URL `http://` non-loopback). | Test unitario sulla validazione URL + grep sui log. |
| NFR-06 | **Osservabilità**: ogni frame ha un ciclo di vita tracciabile nel log tramite `frameId`. | Log strutturato. |
| NFR-07 | **Costo Vercel Hobby**: < 1 invocazione ogni 10 frame in condizioni normali (batching), payload compresso. | Batch size default 25 + gzip. |
| NFR-08 | **Testabilità**: il 100% della logica di payload, dedup, backoff e coda è in un progetto **senza riferimenti a NINA**, testato con xUnit. | Copertura misurata sul progetto Core. |
| NFR-09 | **Compatibilità**: build su .NET 8 (`net8.0-windows7.0`), target NINA 3.x; punti di contatto con l'API NINA isolati dietro interfacce. | Vedi §15. |
| NFR-10 | **Idempotenza end-to-end**: reinviare 1000 volte lo stesso batch lascia lo store identico. | Test di integrazione lato sito. |

---

## 4. Architettura proposta

### 4.1 Vista d'insieme

```
   NINA (processo Windows, .NET 8)
   ┌───────────────────────────────────────────────────────────────┐
   │ MyUniverseUplinkPlugin : PluginBase, IPluginManifest          │
   │   Initialize() / Teardown()                                   │
   │                                                               │
   │  IImageSaveMediator.ImageSaved                                │
   │        │  (thread di NINA, < 50 ms)                           │
   │        ▼                                                      │
   │  ImageSavedListener ──> FrameRecordFactory ──> UploadQueue    │
   │        (adapter NINA)      (Core, puro)        (Core, puro)   │
   │                                                    │          │
   │                                             journal su disco  │
   │                                                    │          │
   │                                              QueueWorker      │
   │                                              (Channel<T>)     │
   │                                                    │          │
   │                                        BatchBuilder + Retry   │
   │                                                    │          │
   │                                            UplinkHttpClient   │
   └────────────────────────────────────────────────────┼──────────┘
                                                        │ HTTPS
                                                        ▼
   Vercel (Next.js 16, Node runtime)
   ┌───────────────────────────────────────────────────────────────┐
   │ POST /api/uplink/frames   ← auth Bearer, idempotente          │
   │ GET  /api/uplink/health   ← capability negotiation            │
   │ POST /api/uplink/events   ← eventi sequencer (Fase 6)         │
   │        │                                                      │
   │        ├─► frame store (batch immutabili + rollup per notte)  │
   │        ├─► blob privato per le thumbnail                      │
   │        └─► updateTag("uplink-frames") → invalida la dashboard │
   │                                                               │
   │ /sessioni-astronomiche  → session-dashboard.ts                │
   │        └─ store remoto  ⊕  integration.csv (fallback storico) │
   └───────────────────────────────────────────────────────────────┘
```

### 4.2 Decisioni tecniche motivate (ADR sintetici)

**ADR-1 — Due progetti: `Core` senza NINA, `Plugin` sottile.**
I tipi NINA non sono facilmente istanziabili né mockabili in test. Tutta la logica interessante
(costruzione payload, hash, coda, backoff, serializzazione, batching) vive in
`RobertPanai.MyUniverse.Uplink.Core` (`net8.0`, zero riferimenti NINA) ed è coperta da test.
Il progetto `Plugin` (`net8.0-windows7.0`) contiene solo adapter che traducono i tipi NINA nei DTO
del Core. *Conseguenza:* un aggiornamento di NINA tocca pochi file di adapter.
*Alternativa scartata:* progetto unico come Lightbucket — semplice ma non testabile.

**ADR-2 — Coda persistente file-based, non SQLite (per la v1).**
Serve durabilità (NFR-02) ma non query. Un journal append-only (`queue.ndjson`) + spool file per le
thumbnail dà atomicità sufficiente con `File.Move` e nessuna dipendenza nativa. SQLite implicherebbe
`Microsoft.Data.Sqlite` + binari nativi caricati nel processo NINA (che già usa SQLite per conto suo):
rischio di conflitto sproporzionato al beneficio. *Upgrade path:* se il volume supera ~50k frame in
coda, l'interfaccia `IFrameQueue` permette di sostituire l'implementazione senza toccare il resto.

**ADR-3 — Il server prima del plugin.**
L'endpoint è sviluppabile e testabile su macOS con `curl`; il plugin no. Costruire prima il
contratto server permette anche di scrivere un *mock server* locale per lo sviluppo del plugin.

**ADR-4 — Batch di frame, non un POST per frame.**
Una notte tipo genera centinaia di frame. Batch da 25 con gzip riduce invocazioni Vercel, tempo di
handshake TLS e consumo di banda al sito remoto (che spesso ha upload lento). Il batch resta piccolo
abbastanza da non superare i limiti e da rendere il retry economico.
*Vincolo:* il server deve rispondere **per frame**, non per batch, così un frame malformato non
blocca gli altri (HTTP 207-like nel corpo, status HTTP 200).

**ADR-5 — Thumbnail base64 dentro il JSON, ma campionate.**
Coerente con Lightbucket e semplicissimo da gestire in una route handler Next.js. Una thumbnail
400px JPEG q70 pesa ~25-40 KB; con base64 ~35-55 KB. Inviarne una per **ogni** frame significa
~20 MB/notte: accettabile ma inutile. Policy di default: **prima immagine per (target, filtro, notte)
+ una ogni 15 minuti + sempre l'ultima**. Tutto configurabile, e disattivabile con un flag.
*Alternativa scartata:* multipart/form-data — più efficiente in byte ma più codice su entrambi i lati
e nessun beneficio reale a questi volumi.

**ADR-6 — Store lato sito: blob immutabili + rollup, non commit git.**
Raccomandazione primaria: **Vercel Blob** (privato) con questo schema:
- `frames/raw/{nightId}/{batchId}.ndjson` — scritture immutabili, nessuna race condition;
- `frames/rollup/{nightId}.ndjson` — prodotto da un cron di compattazione, deduplicato;
- `frames/index.json` — elenco notti + conteggi + hash, letto dalla dashboard;
- `thumbs/{frameId}.jpg` — thumbnail.
Motivazione: zero provisioning, zero costo fisso su Hobby, scritture concorrenti sicure per
costruzione, e il modello "append immutabile + compattazione" è esattamente ciò che serve a un log
di frame. La dashboard legge `index.json` + i rollup necessari e aggrega in memoria come fa oggi con
il CSV (già lo fa su 12k righe senza problemi).
*Alternativa:* **Neon Postgres** dal Marketplace Vercel se in futuro servono query per target/notte,
grafici temporali complessi o una API pubblica. Decisione da confermare (vedi §18 domanda Q3).
*Scartata:* commit git per frame (rebuild continui), KV/Redis come store primario (non adatto a
storico crescente).

**ADR-7 — Token opachi multipli, uno per dispositivo.**
`UPLINK_INGEST_TOKENS` contiene una lista `deviceId:sha256(token)` separata da virgole. Il server
confronta l'hash in modo timing-safe. Revocare un PC = rimuovere una voce. Nessun segreto in chiaro
nell'env, nessun database di utenti da gestire.

**ADR-8 — Invalidazione cache esplicita.**
La pagina sessioni diventa cacheable e viene invalidata dall'ingest con il meccanismo di tag di
Next.js 16 (`cacheTag` / `updateTag`). Leggere `node_modules/next/dist/docs/` prima di implementare:
le API di caching sono cambiate rispetto alle versioni note ai modelli.

---

## 5. Struttura di cartelle e classi

### 5.1 Repository plugin (`nina-my-universe-uplink`)

```
nina-my-universe-uplink/
├─ MyUniverseUplink.sln
├─ docs/
│  ├─ nina-api-notes.md          ← Fase 0: firme API NINA verificate, con versione package
│  ├─ dependencies.md            ← giustificazione di ogni dipendenza NuGet
│  └─ manual-test-plan.md        ← §15
├─ src/
│  ├─ Uplink.Core/                          (net8.0 — NESSUN riferimento a NINA)
│  │  ├─ Model/
│  │  │  ├─ FrameRecord.cs                  DTO principale (§8)
│  │  │  ├─ TargetInfo.cs  EquipmentInfo.cs  ExposureInfo.cs
│  │  │  ├─ QualityInfo.cs  EnvironmentInfo.cs  ThumbnailData.cs
│  │  │  ├─ DeviceInfo.cs  FrameBatch.cs  BatchResponse.cs
│  │  │  └─ ServerCapabilities.cs
│  │  ├─ Identity/
│  │  │  ├─ FrameIdFactory.cs                hash deterministico (§11)
│  │  │  └─ NightIdCalculator.cs             notte astronomica (shift -12 h su ora locale)
│  │  ├─ Queue/
│  │  │  ├─ IFrameQueue.cs
│  │  │  ├─ FileFrameQueue.cs                journal NDJSON + spool thumbnail
│  │  │  ├─ QueueEntry.cs                    stato: Pending | InFlight | Dead
│  │  │  └─ DeadLetterStore.cs
│  │  ├─ Transport/
│  │  │  ├─ IUplinkClient.cs
│  │  │  ├─ UplinkHttpClient.cs              HttpClient + gzip + timeout + UA
│  │  │  ├─ RetryPolicy.cs                   backoff esponenziale + jitter + Retry-After
│  │  │  ├─ ErrorClassifier.cs               retriabile / permanente
│  │  │  └─ UplinkJson.cs                    JsonSerializerOptions condivise
│  │  ├─ Pipeline/
│  │  │  ├─ UploadWorker.cs                  consumer Channel<T>, batching, drain
│  │  │  ├─ ThumbnailPolicy.cs               campionamento (§7.4)
│  │  │  └─ UplinkState.cs                   stato osservabile per la UI
│  │  ├─ Configuration/
│  │  │  ├─ UplinkSettings.cs                POCO immutabile
│  │  │  └─ SettingsValidator.cs             URL https, token non vuoto, limiti
│  │  ├─ Security/
│  │  │  ├─ ISecretProtector.cs
│  │  │  └─ DpapiSecretProtector.cs          (Windows-only, dietro interfaccia)
│  │  └─ Logging/
│  │     ├─ IUplinkLog.cs
│  │     └─ FileUplinkLog.cs                 file rolling nel folder plugin
│  │
│  └─ Uplink.Plugin/                         (net8.0-windows7.0 — riferimenti NINA)
│     ├─ MyUniverseUplinkPlugin.cs           PluginBase, IPluginManifest, [Export]
│     ├─ Adapters/
│     │  ├─ ImageSavedListener.cs            sottoscrizione/desottoscrizione evento
│     │  ├─ NinaFrameMapper.cs               ImageSavedEventArgs → FrameRecord
│     │  ├─ NinaThumbnailRenderer.cs         debayer/stretch/resize/encode JPEG
│     │  ├─ ProfileSettingsStore.cs          PluginOptionsAccessor per profilo
│     │  └─ NinaLogBridge.cs                 IUplinkLog → logger NINA
│     ├─ ViewModels/
│     │  └─ UplinkOptionsViewModel.cs        comandi UI, stato, test connection
│     ├─ Sequencer/
│     │  └─ SendSiteEventItem.cs             istruzione Advanced Sequencer (Fase 6)
│     ├─ Options.xaml                        DataTemplate della pagina Options
│     └─ Properties/AssemblyInfo.cs          metadati manifest plugin
│
└─ tests/
   └─ Uplink.Core.Tests/                     xUnit
      ├─ FrameIdFactoryTests.cs
      ├─ FileFrameQueueTests.cs
      ├─ RetryPolicyTests.cs
      ├─ ThumbnailPolicyTests.cs
      ├─ UploadWorkerTests.cs                con IUplinkClient fake
      ├─ NightIdCalculatorTests.cs
      └─ PayloadContractTests.cs             snapshot JSON vs. schema del sito
```

### 5.2 Repository sito (`web`) — file nuovi e modificati

```
src/app/api/uplink/
  frames/route.ts        POST ingest batch
  health/route.ts        GET capabilities
  events/route.ts        POST eventi sequencer            (Fase 6)
  thumb/[frameId]/route.ts   GET thumbnail privata        (Fase 5)
  compact/route.ts       POST/GET invocata dal cron       (Fase 5)

src/lib/uplink/
  auth.ts                parsing UPLINK_INGEST_TOKENS, confronto timing-safe
  schema.ts              validazione + normalizzazione payload (schemaVersion 1)
  frame-store.ts         interfaccia store + implementazione blob
  frame-dedupe.ts        frameId + chiave naturale
  night.ts               nightId condiviso con il plugin (stessa regola!)
  capabilities.ts        oggetto capacità restituito da /health

src/lib/session-dashboard.ts     MODIFICATO: sorgente = store ⊕ CSV storico
src/app/sessioni-astronomiche/page.tsx   invariato nel contratto
scripts/import-integration-csv.mjs       import una-tantum (Fase 5)
content/site/uplink-targets.json         mappa "nome target NINA" → slug galleria (Fase 8)
docs/uplink-api.md                       contratto API, versionato col codice
```

---

## 6. Ciclo di vita del plugin

| Momento | Cosa succede | Note |
|---|---|---|
| **Costruttore** `[ImportingConstructor]` | Riceve via DI `IProfileService`, `IImageSaveMediator`, `IImageDataFactory` (e in Fase 6 i mediator dei device). Salva i riferimenti. Registra il resource dictionary delle Options se richiesto dal modello NINA. **Nessun I/O, nessuna rete.** | Un costruttore lento rallenta l'avvio di NINA. |
| **`Initialize()`** | 1) Legge le impostazioni del profilo attivo. 2) Apre il log. 3) Istanzia coda persistente e **replay del journal** (i frame rimasti da sessioni precedenti tornano Pending). 4) Avvia `UploadWorker` con un `CancellationTokenSource` del plugin. 5) Sottoscrive `ImageSaved`. 6) Lancia (fire-and-forget tracciato) una chiamata a `/health`. | Se le impostazioni non sono valide, il plugin si carica comunque ma in stato *disabled* con messaggio in UI: mai lanciare eccezioni che impediscano il caricamento. |
| **Cambio profilo NINA** | `IProfileService.ProfileChanged` → ricarica le impostazioni, riapplica lo stato abilitato/disabilitato. La coda **non** viene svuotata: i frame già accodati mantengono l'endpoint con cui sono stati creati. | Ogni `QueueEntry` memorizza l'endpoint e il `deviceId` di destinazione. |
| **Durante l'esercizio** | `ImageSaved` → mapping → enqueue (journal) → il worker fa batching, thumbnail, invio, retry. | Vedi §7 e §10. |
| **`Teardown()`** | 1) Desottoscrive `ImageSaved` (**prima** di tutto). 2) Segnala stop al worker. 3) Attende il drain con timeout **configurabile, default 20 s** (NINA non deve restare appeso). 4) Fa flush del journal e del log. 5) `Dispose` di `HttpClient`, `CancellationTokenSource`, file handle. | Il drain non deve mai bloccare oltre il timeout: dopo il timeout i frame restano su disco e ripartono al prossimo avvio (NFR-02). |
| **Crash di NINA** | Nulla di speciale: il journal è già su disco, il replay all'avvio successivo recupera tutto. | Test esplicito in §15. |

Regola: **niente `async void`**. `Initialize`/`Teardown` restituiscono `Task` (verificare la firma
esatta in `docs/nina-api-notes.md`); il worker è un `Task` di lunga durata tracciato in un campo,
mai un `Task.Run` orfano.

---

## 7. Gestione dell'evento ImageSaved

### 7.1 Percorso rapido (thread di NINA)

L'handler deve fare **solo** questo, in modo sincrono e veloce:

1. Se il plugin è disabilitato o le impostazioni non sono valide → return immediato.
2. Leggere `ImageType`; se non è tra quelli selezionati → return.
3. Se `ImageType == LIGHT` e il target è assente/vuoto e l'opzione "richiedi target" è attiva → return
   (comportamento di Lightbucket: senza target il dato non è utile alla dashboard).
4. **Copiare** in un DTO tutti i valori scalari necessari (mai tenere riferimenti a oggetti immagine
   di NINA: verranno riciclati/liberati).
5. Calcolare `frameId` e `nightId`.
6. Decidere se questo frame merita una thumbnail (`ThumbnailPolicy`), e in caso affermativo
   **mettere in coda anche il percorso del file salvato**, non i pixel.
7. `queue.Enqueue(entry)` → scrittura append sul journal + return.

Se un'eccezione arriva fin qui, va catturata, loggata e ingoiata: un plugin non deve mai far fallire
il salvataggio immagine di NINA.

### 7.2 Percorso lento (worker in background)

Il worker, per ogni entry:

1. Se serve la thumbnail e non è ancora nello spool: carica l'immagine tramite `IImageDataFactory`
   (o rilegge il file salvato), applica debayer se la camera è a colori, applica lo stretch,
   ridimensiona al lato lungo configurato, codifica JPEG, salva nello spool e aggiorna il journal.
   *Se questo passo fallisce, il frame viene inviato comunque senza thumbnail* — i metadati valgono
   più dell'anteprima.
2. Accumula fino a `batchSize` entry (o fino a `maxBatchWaitSeconds`, default 20 s) e costruisce il
   `FrameBatch`.
3. Invia, gestisce la risposta per-frame, marca ack/dead, compatta il journal.

### 7.3 Mappatura dei metadati (da verificare contro l'API reale)

Sorgenti previste, da confermare in Fase 0:
- `ImageSavedEventArgs.MetaData.Target` → nome, coordinate, rotazione;
- `...MetaData.Camera` → nome, gain, offset, binning, temperatura sensore/target, pixel size;
- `...MetaData.Telescope` → nome, focale, apertura, altezza/azimut, airmass;
- `...MetaData.FilterWheel` → filtro;
- `...MetaData.Focuser`, `.Rotator`, `.WeatherData` → contesto opzionale;
- `...StarDetectionAnalysis` → HFR, HFR StDev, star count;
- `...Statistics` → mean, median, stDev, min/max;
- `...MetaData.Image.RecordedRMS` → RMS guida in pixel e arcsec;
- `IProfileService.ActiveProfile.TelescopeSettings/CameraSettings` → fallback per focale, pixel size,
  nomi "editoriali" quando il device non li espone.

Campi calcolati nel Core (non presi da NINA):
- `imageScaleArcsecPerPx = 206.265 × pixelSizeUm × binning ÷ focalLengthMm`;
- `nightId`: data della notte astronomica = data locale dell'istante `capturedAtUtc` convertito
  in ora locale e traslato di −12 h (una notte a cavallo della mezzanotte resta una sola notte).
  **La stessa identica regola va implementata anche lato sito** in `src/lib/uplink/night.ts`, con un
  test che confronta i due risultati su casi noti (fuso Europe/Rome, DST inclusa).

### 7.4 Policy thumbnail (default)

Una thumbnail viene generata se **almeno una** condizione è vera:
- è il primo frame della combinazione (nightId, target, filtro);
- sono passati ≥ 15 minuti dall'ultima thumbnail inviata;
- è un frame marcato manualmente con *Send last image*.
Inoltre: mai più di `maxThumbnailsPerNight` (default 60). Tutto configurabile; opzione
"thumbnail: sempre / campionate / mai".

---

## 8. Modello del payload inviato al sito

### 8.1 Richiesta — `POST /api/uplink/frames`

Header: `Authorization: Bearer <token>`, `Content-Type: application/json`,
`Content-Encoding: gzip`, `X-Uplink-Device: <deviceId>`, `Idempotency-Key: <batchId>`,
`User-Agent: MyUniverseUplink/<versione> (NINA <versione>)`.

```jsonc
{
  "schemaVersion": 1,
  "batchId": "01JBQ...",            // ULID, stabile fra i retry dello stesso batch
  "device": {
    "deviceId": "osservatorio-casa",
    "hostName": "ASTRO-PC",
    "pluginVersion": "1.0.0",
    "ninaVersion": "3.2.0.1001",
    "profileId": "5f2c...",         // GUID profilo NINA
    "profileName": "Osservatorio"
  },
  "frames": [
    {
      "frameId": "9f2c1a...",       // 32 hex, deterministico (§11)
      "imageType": "LIGHT",         // LIGHT | FLAT | DARK | BIAS | SNAPSHOT
      "capturedAtUtc": "2025-11-07T09:16:58.892Z",   // inizio esposizione
      "savedAtUtc":    "2025-11-07T09:32:10.004Z",
      "nightId": "2025-11-06",
      "sequenceTitle": "Witch Head — Ha",
      "target": {
        "name": "WitchHead",
        "raHours": 5.0814,
        "decDegrees": -7.5733,
        "raText": "5h4m53s",
        "decText": "-7° 34' 24\"",
        "rotationDegrees": 90.0
      },
      "equipment": {
        "cameraName": "QHY268M",
        "pixelSizeUm": 3.76,
        "telescopeName": "TS 70mm f5 quadruplet",
        "focalLengthMm": 350,
        "focalRatio": 5.0,
        "mountName": "EQ6-R Pro",
        "imageScaleArcsecPerPx": 2.216
      },
      "exposure": {
        "filter": "Ha",
        "durationSeconds": 900,
        "gain": 56,
        "offset": 26,
        "binning": "1x1",
        "sensorTemperatureC": -10.0
      },
      "quality": {
        "hfr": 2.31, "hfrStDev": 0.42, "starCount": 812,
        "mean": 1234.5, "median": 1200.0, "stDev": 210.4,
        "rmsTotalArcsec": 0.62, "rmsRaArcsec": 0.44, "rmsDecArcsec": 0.43,
        "altitudeDeg": 53.2, "azimuthDeg": 190.1, "airmass": 1.24
      },
      "environment": {
        "skyQualityMpsas": 20.8, "temperatureC": 4.2,
        "humidityPercent": 78, "cloudCoverPercent": 5
      },
      "source": { "fileName": "WitchHead_Ha_900s_0012.fits" },
      "thumbnail": {
        "format": "jpeg", "width": 400, "height": 267,
        "byteLength": 28714,
        "sha256": "ab12...",
        "base64": "/9j/4AAQ..."
      }
    }
  ]
}
```

Regole di contratto:
- **Tutti i campi tranne `schemaVersion`, `batchId`, `device`, `frames[].frameId`,
  `frames[].imageType`, `frames[].capturedAtUtc` sono opzionali.** Il server deve accettare un frame
  minimale (tolerant reader), perché non ogni setup espone tutto.
- Numeri come numeri JSON, mai stringhe. Date sempre ISO-8601 UTC con `Z`.
- `raText`/`decText` sono **derivati**, mantenuti solo per continuità con `integration.csv`; le
  coordinate autorevoli sono `raHours` / `decDegrees`.
- Nessun percorso assoluto del filesystem nel payload (privacy): solo `fileName`.
- `thumbnail` assente se non campionata.

### 8.2 Risposta

```jsonc
{
  "schemaVersion": 1,
  "batchId": "01JBQ...",
  "serverTimeUtc": "2025-11-07T09:32:11.512Z",
  "results": [
    { "frameId": "9f2c1a...", "status": "stored" },
    { "frameId": "7d3e88...", "status": "duplicate" },
    { "frameId": "0a91bb...", "status": "rejected", "reason": "capturedAtUtc missing" }
  ],
  "accepted": 1, "duplicates": 1, "rejected": 1
}
```

- HTTP **200** se il batch è stato processato, anche con frame rifiutati: `stored`, `duplicate` e
  `rejected` sono tutti **acknowledgment definitivi** → il plugin rimuove l'entry dalla coda
  (i `rejected` finiscono anche in dead-letter locale per ispezione).
- HTTP **401/403** → il plugin ferma l'invio, va in stato *auth error* e lo mostra in UI: non ha
  senso ritentare con un token sbagliato.
- HTTP **413** → il plugin dimezza il batch size e ritenta.
- HTTP **429** con `Retry-After` → attesa rispettosa.
- HTTP **5xx** o errore di rete → retry con backoff.

### 8.3 `GET /api/uplink/health`

```jsonc
{
  "schemaVersion": 1,
  "serverVersion": "1.0.0",
  "maxFramesPerBatch": 50,
  "maxBodyBytes": 4000000,
  "acceptsThumbnails": true,
  "maxThumbnailBytes": 200000,
  "authenticated": true,
  "deviceId": "osservatorio-casa"
}
```

Il plugin adatta batch size e thumbnail alle capacità dichiarate. Il pulsante *Test connection*
chiama esattamente questo endpoint e mostra il risultato tradotto in linguaggio umano.

---

## 9. Autenticazione e protezione delle credenziali

**Lato sito.**
- Env `UPLINK_INGEST_TOKENS` = `deviceId:sha256hex,deviceId2:sha256hex` (nessun segreto in chiaro).
- La route calcola SHA-256 del bearer ricevuto e lo confronta con `crypto.timingSafeEqual` contro
  ogni voce; a token valido associa il `deviceId` usato nei log e nei metadati del frame.
- Rifiuto immediato se: header assente, body > limite, `Content-Type` errato, `schemaVersion`
  sconosciuta.
- Rate limit semplice per token (es. 120 richieste / 5 min) — sufficiente contro loop impazziti;
  non è un endpoint pubblico né indicizzato.
- **Non usare** il token in query string, mai loggarlo, nemmeno troncato.
- Generazione token: uno script `scripts/uplink-token.mjs` che stampa token + hash da incollare in
  `vercel env`.

**Lato plugin.**
- Il token viene inserito in un `PasswordBox` nella pagina Options.
- Persistenza: `ProtectedData.Protect(bytes, optionalEntropy, DataProtectionScope.CurrentUser)`,
  risultato in Base64 salvato nelle impostazioni per profilo. Come Lightbucket e Ground Station.
- *Limite noto e da documentare all'utente:* DPAPI `CurrentUser` lega il segreto all'account Windows
  e alla macchina. Copiare il profilo su un altro PC richiede reinserire il token. È il comportamento
  desiderato (il token è per dispositivo).
- `ISecretProtector` è un'interfaccia: nei test si usa un'implementazione in memoria, così il Core
  resta cross-platform e testabile su CI Linux.
- La UI mostra sempre e solo `••••••••` più le ultime 4 cifre; il log scrive `token=<redacted>`.
- Validazione URL: solo `https://`, oppure `http://localhost` / `http://127.0.0.1` per lo sviluppo
  contro il mock server. Qualsiasi altro `http://` viene rifiutato con messaggio esplicito.

---

## 10. Coda, retry, timeout, modalità offline

### 10.1 Struttura su disco

```
%LOCALAPPDATA%\NINA\Plugins\MyUniverseUplink\
  settings\                 (le impostazioni vere stanno nel profilo NINA; qui solo cache locale)
  queue\
    queue.ndjson            journal append-only: una riga per evento (enqueue / ack / dead)
    queue.ndjson.compact    file temporaneo di compattazione
    spool\<frameId>.jpg     thumbnail in attesa di invio
  dead\
    <frameId>.json          payload completo dei frame non consegnabili + motivo + timestamp
  logs\
    uplink-YYYYMMDD.log
```

- **Write-ahead:** la riga `enqueue` viene scritta e `Flush()`ata **prima** che l'handler ritorni.
- **Compattazione:** quando il journal supera N righe (default 5.000) o all'avvio, viene riscritto
  tenendo solo le entry non ackate, con `File.Move` atomico.
- **Replay all'avvio:** entry `InFlight` senza ack tornano `Pending` (possibile duplicato lato
  server — risolto dall'idempotenza, §11).

### 10.2 Worker

- Un solo consumer (`System.Threading.Channels.Channel<QueueEntry>` unbounded in RAM, alimentato dal
  journal): l'ordine è cronologico e la concorrenza è 1 → niente race, niente interleaving strano nei
  log, carico di rete prevedibile.
- Batching: accumula fino a `batchSize` (default 25, adattato da `/health`) oppure fino a
  `maxBatchWait` (default 20 s) dall'arrivo del primo elemento del batch.
- Cancellazione cooperativa: ogni `await` riceve il token del plugin.

### 10.3 Timeout

| Operazione | Timeout | Nota |
|---|---|---|
| Handshake+richiesta HTTP | 30 s (configurabile 10-120) | `HttpClient.Timeout` + `CancellationTokenSource` linkato |
| Generazione thumbnail | 15 s | oltre → invio senza thumbnail |
| Drain in `Teardown` | 20 s | oltre → i frame restano su disco |
| Health check | 10 s | fallimento non blocca l'ingest |

`HttpClient` unico e riusato per tutto il ciclo di vita del plugin, con
`SocketsHttpHandler { PooledConnectionLifetime = 5 min, AutomaticDecompression = GZip }`.
**Mai** un `HttpClient` per richiesta (port exhaustion) né `HttpClient` statico senza
`PooledConnectionLifetime` (DNS stale).

### 10.4 Retry e backoff

- Classificazione (`ErrorClassifier`):
  - **Retriabile:** `HttpRequestException`, `TaskCanceledException` da timeout, DNS, 408, 429,
    500, 502, 503, 504.
  - **Permanente:** 400, 401, 403, 404, 422 → nessun retry, entry in dead-letter (401/403 sospendono
    anche l'intero worker con stato *auth error*).
- Backoff: `delay = min(maxDelay, base × 2^(attempt-1)) × (0,5 + rnd/2)` con `base = 5 s`,
  `maxDelay = 15 min`, `maxAttempts = 8` (≈ 45 min di tentativi). `Retry-After` ha sempre la
  precedenza.
- Il retry avviene **sul batch**, non sul singolo frame; se un batch fallisce ripetutamente, viene
  spezzato a metà (bisezione) per isolare l'eventuale frame problematico prima di dichiararlo dead.
- Dopo `maxAttempts`: entry → `dead\`, contatore in UI, log a livello Error. Nessuna perdita.

### 10.5 Modalità offline

- Non c'è un "modo offline" separato: **la coda persistente è la modalità offline.** Se la rete manca
  (caso realistico: osservatorio senza connessione, hotspot instabile), i frame si accumulano e
  partono alla prima richiesta riuscita.
- Circuit breaker leggero: dopo 3 fallimenti di rete consecutivi, il worker passa a un polling lento
  (1 tentativo ogni 5 min) invece di consumare batteria/CPU; torna al ritmo normale al primo successo.
- Limiti di guardia configurabili: `maxQueueEntries` (default 50.000) e `maxQueueAgeDays`
  (default 30). Al superamento: le entry più vecchie vanno in dead-letter (mai eliminate in silenzio)
  e la UI mostra un avviso.
- Le thumbnail nello spool hanno una politica più aggressiva (`maxSpoolMB`, default 500): se lo spool
  supera il limite, i frame più vecchi perdono la thumbnail ma **conservano i metadati**.

---

## 11. Gestione dei duplicati

Tre livelli, indipendenti, tutti necessari:

**1. `frameId` deterministico (lato plugin).**
```
frameId = SHA256( profileId + "|" + cameraName + "|" +
                  capturedAtUtc.ToString("O") + "|" + fileName )  →  primi 32 hex
```
Motivazione delle componenti: `capturedAtUtc` da solo può ripetersi tra rig diversi; `fileName`
distingue frame con lo stesso timestamp arrotondato; `profileId` separa setup diversi.
Lo stesso frame ricalcolato dopo un riavvio produce lo stesso id → i replay del journal non
duplicano nulla.

**2. Chiave naturale (lato sito).**
```
naturalKey = normalize(targetName) + "|" + normalize(filter) + "|" +
             durationSeconds + "|" + capturedAtUtc (troncato al secondo) + "|" + normalize(cameraName)
```
Serve per deduplicare tra **sorgenti diverse**: i frame storici importati da `integration.csv`
non hanno `fileName`, quindi avranno un `frameId` sintetico (`csv:<hash riga>`) ma la stessa chiave
naturale dei frame che il plugin invierebbe per le stesse riprese. Regola: se esiste già un frame con
la stessa chiave naturale, il nuovo record **arricchisce** quello esistente (aggiunge HFR, thumbnail,
ecc.) invece di crearne uno nuovo, e la risposta è `duplicate`.

**3. `Idempotency-Key` di batch.**
Il `batchId` (ULID generato una volta e riusato in tutti i retry) permette al server di riconoscere
un batch già processato e restituire la stessa risposta senza riscrivere nulla. Utile quando la
risposta si perde ma il server ha già scritto.

**Nota sul comportamento di NINA:** alcune configurazioni possono emettere `ImageSaved` più volte per
la stessa immagine (es. salvataggi multi-formato). Il `frameId` basato su `fileName` distinguerebbe
`.fits` e `.tif` come frame diversi: per questo il plugin deve **ignorare l'estensione** nel calcolo
(usare `Path.GetFileNameWithoutExtension`) e, opzionalmente, filtrare per estensione configurata.

---

## 12. Configurazione WPF (pagina Options)

Struttura della pagina, in un unico `DataTemplate` con sezioni espandibili:

**Connection**
- `Enabled` (toggle grande in cima)
- `API base URL` (TextBox, validazione https)
- `Device ID` (TextBox, default = hostname normalizzato)
- `API token` (PasswordBox + pulsante *Paste*)
- Pulsante **Test connection** → chiama `/health`, mostra ✓/✗ con messaggio leggibile e capacità
  ricevute (batch max, thumbnail sì/no)

**What to send**
- Checkbox per tipo immagine: LIGHT (default on), SNAPSHOT, FLAT, DARK, BIAS
- `Only when a target is set` (default on)
- `Send image statistics` (HFR, star count, …) — default on
- `Send guiding RMS` — default on
- `Send weather / SQM` — default on
- Filtro per estensione file (default: tutte)

**Thumbnails**
- Modalità: *Always / Sampled / Never* (default Sampled)
- Lato lungo (default 400 px), qualità JPEG (default 70)
- Intervallo campionamento (default 15 min), massimo per notte (default 60)
- Anteprima dal vivo dell'ultima thumbnail generata (immagine + peso in KB)

**Delivery**
- Batch size (default 25), attesa massima batch (20 s), timeout richiesta (30 s)
- Tentativi massimi (8), limiti coda (entry / giorni / MB spool)

**Status & maintenance** (sola lettura + azioni)
- Stato: `Idle / Sending / Offline / Auth error / Disabled`
- Contatori: pending, in flight, dead, inviati questa notte, ultimo successo, ultimo errore
- Pulsanti: **Send last image**, **Flush queue now**, **Retry dead letters**, **Clear dead letters**,
  **Open log folder**

Note implementative:
- Il ViewModel espone `ICommand` (`AsyncRelayCommand` scritto a mano nel Core/Plugin per evitare
  dipendenze) e `INotifyPropertyChanged`; i comandi async **non** sono `async void`.
- Gli aggiornamenti di stato dal worker arrivano via evento e vanno marshalati sul dispatcher WPF.
- Ogni impostazione è salvata **per profilo** tramite `PluginOptionsAccessor` con chiavi stabili e
  prefissate (`uplink.baseUrl`, `uplink.enabled`, …). Le chiavi non si rinominano mai; per cambiare
  semantica si introduce una nuova chiave e si migra al primo avvio (`uplink.settingsVersion`).
- La UI non deve mai bloccarsi su I/O: *Test connection* e *Send last image* mostrano uno spinner e
  sono cancellabili.

---

## 13. Integrazione con l'Advanced Sequencer (Fase 6, opzionale)

Un'unica istruzione, semplice e utile: **`Send Site Event`**.

- Deriva da `SequenceItem`, esportata con i metadati di categoria (`My Universe Uplink`) e icona.
- Proprietà configurabili: `EventType` (`SessionStart`, `SessionEnd`, `TargetStart`, `TargetEnd`,
  `Note`), `Message` (testo libero con token), `IncludeEquipmentSnapshot` (bool).
- Token sostituibili nel messaggio: `$$TARGET$$`, `$$FILTER$$`, `$$CAMERA$$`, `$$TELESCOPE$$`,
  `$$TEMPERATURE$$`, `$$MOON_ILLUMINATION$$`, `$$DATE$$`. La lista esatta va documentata nella UI.
- `Execute()` **non** fa la POST direttamente: accoda un `EventRecord` nella stessa coda persistente
  (destinazione `/api/uplink/events`). Così un evento non fa fallire una sequenza per un problema di
  rete, ed eredita retry e offline. `Execute()` ritorna appena l'evento è nel journal.
- `Validate()` segnala in UI se il plugin è disabilitato o non configurato, così l'utente lo vede in
  fase di composizione della sequenza e non alle 2 di notte.

Utilità lato sito: la pagina sessioni può mostrare le notti come **sessioni reali** (inizio/fine,
target ripresi, note) invece che come semplice aggregazione di timestamp.

*Non implementare in Fase 6:* trigger, condizioni, istruzioni HTTP generiche (quello è il mestiere di
Ground Station; qui sarebbe complessità senza scopo).

---

## 14. Logging e diagnostica

- **Due destinazioni:** il logger di NINA (livello Info/Warning/Error, per far apparire i problemi
  dove l'utente li cerca) e un file dedicato del plugin (livello Debug incluso, rolling giornaliero,
  retention 14 giorni).
- **Log strutturato leggibile:** `2025-11-07T09:32:11Z INF batch=01JBQ frames=25 accepted=24 dup=1 ms=812`
  e `... frameId=9f2c1a target=WitchHead filter=Ha state=enqueued`.
- **Ogni frame è tracciabile** dal suo `frameId` in tutte le transizioni: `enqueued → thumb-ok →
  sent → acked` oppure `retry(n) → dead`.
- **Mai loggare:** token, URL completo con eventuali segreti, percorsi assoluti dell'utente
  (loggare solo il nome file), payload base64 (loggare `byteLength` e `sha256`).
- **Diagnostica esportabile:** pulsante *Open log folder* e comando *Export diagnostics* che produce
  uno zip con log, impostazioni **redatte**, conteggi coda e le ultime 10 risposte del server: è
  quello che servirà per capire un problema a distanza.
- **Lato sito:** ogni ingest logga `deviceId`, `batchId`, conteggi e durata; mai il contenuto.
  Vercel Logs è sufficiente in Fase 1; un contatore persistente (ultimo ingest, frame totali) va
  esposto in `/api/uplink/health` per un check a colpo d'occhio.

---

## 15. Strategia di test

**Livello 1 — Unit test (xUnit, progetto `Uplink.Core.Tests`, gira anche su macOS/Linux/CI).**
- `FrameIdFactory`: determinismo, insensibilità all'estensione, sensibilità a target/timestamp.
- `NightIdCalculator`: mezzanotte, cambio DST, riprese pomeridiane/mattutine, fusi diversi.
- `FileFrameQueue`: enqueue/ack/dead, compattazione, replay dopo "crash" simulato (riapertura del
  file a metà scrittura), concorrenza produttore/consumatore.
- `RetryPolicy`: crescita del backoff, jitter entro i limiti, rispetto di `Retry-After`, tetto
  massimo, numero di tentativi.
- `ErrorClassifier`: tabella completa dei codici HTTP.
- `ThumbnailPolicy`: campionamento, cambio target/filtro, limite per notte.
- `UploadWorker` con `IUplinkClient` fake: batching, gestione risposta mista
  (stored/duplicate/rejected), bisezione del batch, drain con cancellazione.
- `PayloadContractTests`: serializza un `FrameBatch` di riferimento e confronta con uno **snapshot
  JSON committato**; lo stesso file JSON è usato come fixture dai test lato sito → il contratto non
  può divergere senza che un test fallisca. *Questo è il test più importante del progetto.*

**Livello 2 — Test di integrazione lato sito (repo `web`).**
- Test della route `frames` con: batch valido, token errato, batch duplicato, frame malformato,
  batch oltre il limite, `schemaVersion` sconosciuta.
- Test di deduplica CSV ↔ plugin sulla chiave naturale.
- Test che `getSessionDashboard` produca gli stessi aggregati leggendo dallo store o dal CSV con gli
  stessi dati (garanzia di non regressione della pagina esistente).

**Livello 3 — Mock server locale.**
Uno script `tools/mock-uplink-server.mjs` (o `next dev` del sito vero) con modalità selezionabili:
`ok`, `slow`, `500`, `429`, `401`, `413`, `flaky`. Il plugin viene sviluppato contro questo prima di
toccare la produzione.

**Livello 4 — Test manuali in NINA** (documentati in `docs/manual-test-plan.md`, con checklist):
1. Caricamento del plugin, pagina Options visibile, impostazioni persistite dopo riavvio.
2. Cambio profilo → impostazioni indipendenti.
3. Sequenza reale o **NINA Simulator camera** con 20 LIGHT: 20 frame arrivano al sito, 0 duplicati.
4. Disconnessione di rete a metà sequenza → coda cresce → riconnessione → tutto consegnato.
5. Kill del processo NINA con coda piena → riavvio → replay completo, nessuna perdita, nessun
   duplicato lato sito.
6. Token errato → stato *auth error*, nessun retry infinito, messaggio chiaro.
7. Chiusura di NINA durante l'invio → chiusura entro il timeout di drain, nessun freeze della UI.
8. Verifica che la sequenza non rallenti: confronto dei tempi di download/salvataggio con plugin
   attivo e disattivo.

**Definition of Done trasversale:** nessuna fase è conclusa senza i test di livello 1 verdi e la
voce corrispondente della checklist manuale spuntata.

---

## 16. Compatibilità con le versioni di NINA

- **Target: NINA 3.x** (la linea attuale con plugin .NET 8). Dichiarare
  `MinimumApplicationVersion` nel manifest sulla 3.1 o superiore, allineandosi alla versione del
  package `NINA.Plugin` scelta (Ground Station usa la linea `3.2.0.900x`: verificare in Fase 0
  qual è l'ultima stabile e usarla).
- **TFM:** `net8.0-windows7.0`, `<UseWPF>true</UseWPF>`, `<Platforms>x64</Platforms>`.
  I riferimenti agli assembly NINA vanno con `<Private>false</Private>` /
  `ExcludeAssets="runtime"`: gli assembly li fornisce NINA, il plugin non deve copiarli.
- **NINA 2.x: non supportato.** Sarebbe .NET Framework 4.8 e un'altra API: è esattamente la parte
  legacy che vogliamo evitare.
- **NINA 4 / future:** tutti i punti di contatto con l'API NINA stanno in `Uplink.Plugin/Adapters/`
  e nel plugin entry point. Un aggiornamento maggiore richiede di aggiornare quei file e
  `docs/nina-api-notes.md`, non il Core. Aggiungere in `docs/` una tabella "API NINA usate" con
  versione e file che le usa: è la mappa per il porting.
- **.NET runtime:** dipende da quello di NINA; non impostare `SelfContained`, non forzare
  `RuntimeIdentifier` diversi da `win-x64`.

---

## 17. Build, packaging e installazione

**Build.**
- `dotnet build -c Release` produce `Uplink.Plugin.dll` (+ `Uplink.Core.dll`).
- Versionamento semantico da una sola sorgente: `<Version>` in `Directory.Build.props`, propagata
  ad `AssemblyInfo` e al manifest.
- Analizzatori attivi: `TreatWarningsAsErrors` sul Core, nullable abilitato ovunque.

**Installazione locale (sviluppo).**
- Copiare la cartella di output in `%LOCALAPPDATA%\NINA\Plugins\<versione-api>\MyUniverseUplink\`
  (verificare il percorso esatto in Fase 0: dipende dalla versione dell'API plugin).
- Script `scripts/install-local.ps1` che builda, chiude NINA se aperto, copia e riapre.

**Packaging per la distribuzione.**
- ZIP con DLL + `manifest.json` conforme allo schema dei manifest NINA (nome, identificatore GUID,
  versione, autore, descrizione, homepage, changelog, checksum SHA-256 dell'archivio).
- Pubblicazione su GitHub Releases; se un giorno si vuole comparire nel plugin browser di NINA,
  aprire una PR sul repository dei manifest della community (`nina-plugin-manifests`).
- GitHub Actions: build + test su push, e su tag `v*` build Release + creazione della Release con
  ZIP e checksum. Runner `windows-latest`.
- **Firma del codice:** non necessaria per l'uso personale; se in futuro il plugin va distribuito,
  valutare un certificato per evitare avvisi SmartScreen.

**Lato sito.**
- Nessun packaging particolare: le route sono parte del deploy Vercel esistente.
- Variabili d'ambiente da configurare prima del primo deploy: `UPLINK_INGEST_TOKENS`, l'eventuale
  token del blob storage, `UPLINK_SCHEMA_VERSION` (facoltativa).
- Documentare in `docs/uplink-api.md` il contratto e in `README.md` il flusso di generazione token.

---

## 18. Rischi tecnici e possibili evoluzioni

### Rischi (con mitigazione)

| Rischio | Impatto | Mitigazione |
|---|---|---|
| **API NINA diversa da quella assunta** | Alto, blocca la Fase 3 | Fase 0 obbligatoria con verifica sul package reale + `nina-api-notes.md`; Core indipendente da NINA |
| **Il plugin rallenta o blocca la sequenza** | Alto (rovina una notte) | Handler < 50 ms, tutto il resto nel worker, try/catch totale nell'handler, misura in log |
| **Conflitti di assembly nel processo NINA** | Alto, errori oscuri | Solo BCL, nessuna dipendenza NuGet non giustificata (ADR-1/regola 4) |
| **Perdita di frame per crash/kill** | Medio | Journal write-ahead + replay + dead-letter (NFR-02) |
| **Duplicati sul sito** | Medio (statistiche sbagliate) | Tripla difesa §11 + test dedicati |
| **Rete assente all'osservatorio** | Medio | La coda persistente *è* la modalità offline; circuit breaker |
| **Crescita dello storage thumbnail** | Basso-medio | Campionamento di default, retention configurabile, blob privato |
| **Limiti Vercel Hobby (invocazioni/banda)** | Basso | Batching + gzip; ordine di grandezza: ~20 invocazioni per notte |
| **DPAPI rende il token non portabile** | Basso | Documentato; il token è per dispositivo per design |
| **Divergenza silenziosa del contratto plugin/sito** | Medio | Snapshot JSON condiviso come fixture nei test dei due repo (§15) |
| **Ora di sistema sbagliata sul PC di ripresa** | Medio (notti sbagliate) | Il server registra anche `serverTimeUtc` e segnala uno skew > 5 min nella risposta e nei log |
| **Nome target incoerente tra notti** (`WitchHead` vs `Witch Head`) | Medio (aggregazioni frammentate) | Normalizzazione lato sito + tabella alias in `content/site/uplink-targets.json` |

### Evoluzioni possibili (dopo la v1)

1. **Auto-integrazione della galleria:** calcolo di `integration_total` e `integration_filters` per
   soggetto dai frame reali, con PR GitHub automatica sul repo del sito (chiude il cerchio con
   `object.md` e con l'uploader Electron esistente).
2. **Pagina "osservatorio in diretta":** ultima thumbnail, target corrente, HFR e RMS della notte in
   corso, con aggiornamento incrementale.
3. **Qualità nel tempo:** grafici HFR/RMS/SQM per notte e per telescopio — i dati saranno già tutti
   nello store.
4. **Metadati FITS avanzati** (già in `PROJECT.md` come sviluppo futuro): lettura di header extra e
   invio nel campo `fits` opzionale.
5. **Notifiche:** un ntfy/Telegram opzionale quando una sessione inizia o fallisce (senza replicare
   Ground Station: solo eventi generati dal nostro plugin).
6. **Progresso per target:** integrazione pianificata vs. accumulata, mostrata sul sito.
7. **Multi-rig:** più `deviceId` che scrivono nello stesso store, con filtro per rig nella dashboard.
8. **Retrofit dell'uploader macOS:** far leggere allo strumento Electron gli stessi dati dallo store
   invece che dal CSV.

---

## 19. Roadmap per fasi

Ogni fase è indipendentemente rilasciabile e ha una Definition of Done verificabile.
**Stimare in sessioni di lavoro, non in giorni.**

### Fase 0 — Ricognizione API e scheletro (nessuna funzionalità)
Attività: generare il GUID del plugin; creare la solution con i tre progetti; verificare sul package
`NINA.Plugin` le firme di `PluginBase`, `IPluginManifest`, `IImageSaveMediator`,
`ImageSavedEventArgs`, `PluginOptionsAccessor`, `SequenceItem`; scrivere `docs/nina-api-notes.md`;
far comparire in NINA un plugin "hello world" con pagina Options vuota.
**DoD:** il plugin appare nella lista plugin di NINA, si abilita, non fa nulla, non rompe nulla;
`nina-api-notes.md` contiene le firme copiate dalla fonte con numero di versione.

### Fase 1 — Sito: contratto e ingest minimo
Attività: `POST /api/uplink/frames` + `GET /api/uplink/health`; auth a token hashati; validazione
`schemaVersion 1`; store blob con batch immutabili; deduplica su `frameId`; `docs/uplink-api.md`;
fixture JSON di riferimento; test di integrazione.
**DoD:** `curl` con il batch di esempio di §8.1 restituisce `accepted: 1`; ripetendolo restituisce
`duplicate: 1`; token errato → 401; i frame sono visibili nello store.

### Fase 2 — Core del plugin, senza NINA
Attività: modello `FrameRecord`, `FrameIdFactory`, `NightIdCalculator`, `FileFrameQueue`,
`RetryPolicy`, `ErrorClassifier`, `UplinkHttpClient`, `UploadWorker`, `ThumbnailPolicy`,
`UplinkSettings` + validatore; suite xUnit completa; mock server locale.
**DoD:** una console di test (o un test di integrazione) accoda 100 frame finti, li invia al sito
in dev, sopravvive a interruzioni simulate e non produce duplicati; test verdi anche su CI Linux.

### Fase 3 — Plugin NINA funzionante end-to-end
Attività: lifecycle `Initialize`/`Teardown`; `ImageSavedListener` + `NinaFrameMapper`;
`NinaThumbnailRenderer`; `ProfileSettingsStore` con DPAPI; pagina Options WPF (sezioni Connection /
What to send / Thumbnails); pulsanti *Test connection* e *Send last image*; logging.
**DoD:** una sequenza con la camera simulatore di NINA produce frame reali sul sito, con thumbnail;
le impostazioni sopravvivono al riavvio e sono indipendenti per profilo; test manuali 1-3 spuntati.

### Fase 4 — Robustezza
Attività: persistenza completa della coda con replay; backoff, bisezione batch, dead-letter,
circuit breaker; limiti di guardia; sezione Status della UI; negoziazione capacità con `/health`;
export diagnostica.
**DoD:** test manuali 4-8 spuntati (rete staccata, kill del processo, token errato, chiusura durante
l'invio) senza perdita né duplicati; nessun freeze della UI di NINA.

### Fase 5 — Sito: la dashboard usa i dati veri
Attività: `frame-store.ts` come sorgente di `session-dashboard.ts` con fallback CSV; import
una-tantum di `integration.csv` con id sintetici e chiave naturale; cron di compattazione;
route thumbnail privata; box "ultima ripresa"; invalidazione cache su ingest.
**DoD:** `/sessioni-astronomiche` mostra gli stessi totali di oggi **più** i frame nuovi arrivati dal
plugin; nessun duplicato tra storico e nuovo; la pagina resta veloce con lo storico completo.

### Fase 6 — Advanced Sequencer ed eventi
Attività: istruzione `Send Site Event`; `POST /api/uplink/events`; rappresentazione delle sessioni
(inizio/fine/note) nella pagina sessioni.
**DoD:** una sequenza con Session Start/End produce una sessione visibile sul sito; un evento inviato
offline arriva alla riconnessione.

### Fase 7 — Packaging e rilascio
Attività: manifest, GitHub Actions, script di installazione, README con guida utente (generazione
token, configurazione, troubleshooting), changelog.
**DoD:** da un tag git si ottiene uno ZIP installabile; installandolo su un PC pulito il plugin
funziona seguendo solo il README.

### Fase 8 — (Opzionale) Auto-integrazione della galleria
Attività: `content/site/uplink-targets.json` (alias target → slug); calcolo di `integration_total` e
`integration_filters`; apertura di una PR sul repo del sito con la modifica di `object.md`.
**DoD:** per un soggetto esistente, il sistema propone una PR con i totali corretti calcolati dai
frame; l'utente approva o rifiuta.

---

## 20. Informazioni che devi ancora specificare

Rispondi a queste domande: le risposte cambiano decisioni concrete del piano.
Dove c'è un default, il piano procede con quello se non specifichi altro.

**Sito e infrastruttura**
- **Q1.** Il sito è già deployato su Vercel? Con quale dominio (`robertpanai-universe.com` è ancora
  quello previsto)? Serve un ambiente di staging separato per testare l'ingest?
- **Q2.** Il repo `web` è pubblico o privato su GitHub? (Impatta su come gestire token e su Fase 8.)
- **Q3.** **Store dei frame: Vercel Blob (raccomandato, zero setup) o Neon Postgres dal Marketplace?**
  Scegli Postgres se prevedi grafici temporali complessi o una API pubblica; altrimenti Blob.
- **Q4.** Hai già configurato Cloudflare R2 / `CDN_IMAGES_BASE`? Se sì, preferisci che anche le
  thumbnail dei frame finiscano su R2 invece che su Vercel Blob?

**Dati e privacy**
- **Q5.** I dati dei frame (target, orari, qualità) devono essere **pubblici** sul sito o visibili
  solo a te dietro autenticazione? La pagina sessioni oggi è pubblica: confermi che lo resti anche
  con i dati live?
- **Q6.** Le thumbnail devono essere visibili al pubblico ("ultima ripresa dall'osservatorio") o
  restare private e servire solo a te come controllo?
- **Q7.** Vuoi che il sito riveli la posizione dell'osservatorio (le coordinate arrivano dai metadati
  meteo/telescopio)? Default del piano: **non inviare mai latitudine/longitudine del sito**.
- **Q8.** `integration.csv` deve restare la fonte di verità storica in git (con import una-tantum) o
  vuoi migrarlo completamente nello store e smettere di aggiornarlo?

**Setup di ripresa**
- **Q9.** Quale versione di NINA usi oggi (esatta)? Hai già altri plugin che ascoltano `ImageSaved`?
- **Q10.** Quanti frame per notte, tipicamente? E qual è il tempo di posa più corto che usi?
  (Determina batch size e policy thumbnail.)
- **Q11.** Usi più profili NINA / più rig? Vuoi distinguerli sul sito (filtro "telescopio" esiste già)?
- **Q12.** Il PC di ripresa ha connessione stabile, intermittente o assente durante la notte?
- **Q13.** Salvi in FITS, XISF o entrambi? Un solo formato per frame o salvataggi multipli?
  (Serve per la regola anti-duplicato di §11.)
- **Q14.** Vuoi inviare anche FLAT/DARK/BIAS (utile per un log di calibrazione) o solo LIGHT?

**Prodotto**
- **Q15.** Cosa vuoi vedere sul sito per prima cosa: statistiche più ricche nella dashboard esistente,
  una pagina "sessione della notte", o il box "ultima ripresa"? (Determina la priorità della Fase 5.)
- **Q16.** Vuoi che i totali di integrazione di `object.md` vengano calcolati automaticamente
  (Fase 8) o preferisci mantenerli manuali tramite l'uploader Electron?
- **Q17.** Come si chiamano i tuoi target in NINA rispetto agli slug della galleria? Servono esempi
  reali (es. `WitchHead` → `nebulose/witch-head`?) per progettare la tabella alias.
- **Q18.** Il plugin sarà open source e pubblicato nel plugin browser di NINA, o resta a uso
  personale? (Se pubblico: serve documentazione utente più curata e va evitato qualsiasi
  hardcoding del tuo dominio.)
- **Q19.** UI del plugin in inglese (default del piano, coerente con NINA) o in italiano?
- **Q20.** Vuoi che gli eventi di sessione (Fase 6) generino anche notifiche verso di te
  (Telegram/ntfy), o il sito è l'unico destinatario?
