# My Universe Uplink — Piano razionalizzato (v2)

> Versione 2.0 — 2026-10-19. Sostituisce operativamente `ninauplinkplan.md` (v1.0, 2026-09-10),
> che resta il riferimento di dettaglio per tutto ciò che qui non è cambiato.
> Stato: **in attesa del via di Robert**. Nessuna riga di codice è stata scritta.

---

## 0. Cosa cambia rispetto alla v1

La v1 era pensata per un prodotto distribuito. Il caso reale è un solo osservatorio, un solo PC
di ripresa, un solo sito. La v2 tiene i vincoli che proteggono la notte di ripresa e i dati, e
toglie i meccanismi che servono solo a scale o scenari che non abbiamo.

| Tema | v1 | v2 | Perché |
|---|---|---|---|
| Store frame | Vercel Blob: batch immutabili + rollup + `index.json` + cron di compattazione | **Postgres (Neon, Vercel Marketplace, piano free)** per i frame, **Vercel Blob** solo per le thumbnail | Deduplica con vincoli `UNIQUE`, niente compattazione, niente cron, query dirette per la dashboard |
| Duplicati | 3 livelli (frameId, chiave naturale, Idempotency-Key di batch) | 2 livelli: `frameId` e chiave naturale | La risposta è per frame, quindi la chiave di batch non aggiunge nulla |
| `nightId` | calcolato da plugin e sito, con test incrociato | calcolato **solo dal sito** (Europe/Rome, −12 h); il plugin lo stima solo per campionare le thumbnail | Una sola fonte di verità |
| Coda plugin | journal NDJSON append-only + compattazione + replay | **un file per frame**: `pending\{frameId}.json`, `dead\{frameId}.json`, scrittura atomica (temp + move) | Replay = elenco della cartella; niente compattazione |
| Retry | backoff + jitter + Retry-After + bisezione + circuit breaker | backoff + jitter + Retry-After; 413 dimezza il batch; 401/403 sospende | La risposta per frame rende inutile la bisezione; il tetto del backoff (15 min) fa da circuit breaker |
| `/health` | negoziazione periodica delle capacità | solo per *Test connection* e verifica token; il plugin usa default fissi | Meno stato, meno casi |
| Mock server | script Node separato | **fault injection sul sito in Preview** (header `X-Uplink-Simulate`), disattivata in produzione | Il PC non raggiunge servizi locali del container; la preview sì |
| Opzioni UI | 5 sezioni, ~25 impostazioni | 4 sezioni, ~12 impostazioni | Il resto ha default sensati |
| Report HTML | assente | **mantenuto** come uscita di debug locale, attivabile dalle opzioni | Richiesta esplicita |
| Sequencer (`Send Site Event`) | Fase 6 | **v1.1**, dopo il collaudo | Non serve per vedere i frame sul sito |
| Auto-integrazione galleria | Fase 8 | **v1.2** | Idem |
| Packaging/GitHub Actions/manifest | Fase 7 | **v1.1**; per ora script di installazione locale | Uso personale |
| Domande Q1–Q20 | tutte aperte | decise in §1; restano solo quelle in §11 | Robert: "vedi tu" |

Restano **invariati** (v1 §0, §3, §6, §7.1, §9, §14): handler `ImageSaved` < 50 ms, nessuna perdita
di frame, nessun `async void`, solo BCL .NET 8 nel plugin, token cifrato con DPAPI e mai loggato,
nessun percorso assoluto né coordinate dell'osservatorio nel payload, API NINA mai inventate.

---

## 1. Decisioni prese

| # | Decisione |
|---|---|
| D1 | Frame in **Neon Postgres** (un database per Preview, uno per Production più avanti). Thumbnail in **Vercel Blob** pubblico, percorso non indovinabile. |
| D2 | **Tutto visibile sul sito**: statistiche, elenco frame per notte, box "ultima ripresa" con thumbnail. Privati restano solo token e ingest. Mai lat/long dell'osservatorio, mai percorsi file. |
| D3 | Contratto: `POST /api/uplink/frames` JSON gzip a batch (default 25) con esito per frame; `GET /api/uplink/health`. `schemaVersion: 1`. Fixture condivisa come fonte di verità. |
| D4 | Token per dispositivo: env `UPLINK_INGEST_TOKENS = deviceId:sha256hex[,…]`, confronto timing-safe. |
| D5 | Plugin in un **repo nuovo** `nina-my-universe-uplink`, con copia di lavoro sul PC in `Documents\nina-my-universe-uplink` e remote GitHub: il sorgente non deve più andare perso come per la 0.1.0. |
| D6 | Il plugin 0.1.0 (`Plugins\3.0.0\My Universe Uplink`, `Plugins\3.2.0\MyUniverseUplink*`) e `HelloNina` vengono spostati in `_to_delete\` prima di installare il nuovo: stesso nome, rischio di conflitti. Nuovo GUID. |
| D7 | Il branch `test/nina-uplink` del sito resta com'è, come riferimento. Si lavora su **`feat/uplink`** da `main`. Pezzi riusabili: confronto timing-safe, controllo JPEG, normalizzatori, stile della pagina. |
| D8 | Target NINA: **3.2.0.9001**, package `NINA.Plugin 3.2.0.9001`, `net8.0-windows7.0`, cartella `Plugins\3.2.0\`. Tipi inviati di default: solo LIGHT. FITS e XISF entrambi accettati, estensione ignorata nel `frameId`. |
| D9 | Thumbnail: modalità *Sampled* (prima per target/filtro/notte, poi ogni 15 min, più *Send last image*), lato lungo 400 px, JPEG q70, max 60 per notte. |
| D10 | Lingua: codice, log e UI del plugin in inglese; documentazione in italiano; testi editoriali del sito con robertizzazione + Humanizer (regole 8 e 11 di `PROJECT.md`). |

---

## 2. Architettura v1 del prodotto

```
NINA 3.2 (Windows)                                Vercel (Next.js 16)
┌──────────────────────────────────────┐          ┌──────────────────────────────────────┐
│ ImageSaved ─► FrameMapper ─► Spool   │  HTTPS   │ POST /api/uplink/frames  (Bearer)    │
│   (<50 ms)        │       pending\   │  gzip    │ GET  /api/uplink/health              │
│                   │          │       │ ───────► │        │                             │
│                   ▼          ▼       │          │        ├─► Neon: uplink_frames       │
│           HtmlReport    UploadWorker │          │        ├─► Blob: thumbs/*.jpg        │
│           (debug)      retry/backoff │          │        └─► revalidate tag            │
└──────────────────────────────────────┘          │ /sessioni-astronomiche ◄─ DB (+CSV)  │
                                                  └──────────────────────────────────────┘
```

---

## 3. Contratto API (sintesi)

Il dettaglio dei campi resta quello di v1 §8.1–8.3, con queste modifiche:

- niente `Idempotency-Key`; niente `nightId` nel payload (lo calcola il sito);
- obbligatori: `schemaVersion`, `batchId`, `device.deviceId`, `frames[].frameId`,
  `frames[].imageType`, `frames[].capturedAtUtc`; tutto il resto opzionale (tolerant reader);
- risposta HTTP 200 con `results[]` (`stored | duplicate | rejected`), 401/403 token, 413 troppo
  grande, 415/400 formato, 429 con `Retry-After`, 5xx errore server;
- `/health` restituisce `schemaVersion`, `serverVersion`, `authenticated`, `deviceId`,
  `maxFramesPerBatch`, `maxBodyBytes`, `acceptsThumbnails`, `serverTimeUtc`;
- **solo in Preview** (`UPLINK_FAULT_INJECTION=1`): header `X-Uplink-Simulate: 500|429|401|413|slow`
  forza la risposta, per collaudare i retry del plugin senza staccare la rete.

Fonte di verità: `contract/batch-v1.request.json`, `contract/batch-v1.response.json`,
`contract/health-v1.json`, **identici nei due repo**. Un test per lato li verifica.

---

## 4. Sito (`RobertPanai/sito`, branch `feat/uplink`)

```
src/app/api/uplink/frames/route.ts     POST ingest batch
src/app/api/uplink/health/route.ts     GET health
src/lib/uplink/auth.ts                 parsing UPLINK_INGEST_TOKENS, timing-safe
src/lib/uplink/schema.ts               validazione/normalizzazione schemaVersion 1
src/lib/uplink/night.ts                nightId Europe/Rome −12 h
src/lib/uplink/db.ts                   client Neon + query (interfaccia testabile)
src/lib/uplink/thumbs.ts               upload Blob
src/lib/uplink/fault.ts                fault injection (solo Preview)
db/migrations/001_uplink_frames.sql    schema
scripts/uplink-token.mjs               genera token + hash
scripts/import-integration-csv.mjs     import una tantum del CSV
src/lib/session-dashboard.ts           MODIFICATO: legge dal DB, fallback CSV
src/app/sessioni-astronomiche/…        box "ultima ripresa" + frame della notte
contract/*.json                        fixture condivise
docs/uplink-api.md                     contratto
tests/uplink/*.test.ts                 vitest + PGlite (Postgres in-process)
```

Tabella `uplink_frames`: `frame_id` PK, `natural_key` UNIQUE, `source` (`plugin|csv`),
`device_id`, `profile_id`, `image_type`, `captured_at`, `night_id`, `target_name`, `filter`,
`duration_s`, `camera_name`, `telescope_name`, `gain`, `offset`, `binning`, `hfr`, `star_count`,
`rms_total_arcsec`, `thumb_url`, `received_at`, `payload jsonb` (tutto il resto).
Regole: conflitto su `frame_id` → `duplicate`; conflitto su `natural_key` con riga `csv` →
arricchimento della riga esistente e `duplicate`.

Prima di scrivere route e cache: leggere `node_modules/next/dist/docs/` (Next 16, `AGENTS.md`).
Dopo ogni modifica: `graphify update .`. Non toccare `AGENTS.md`.

---

## 5. Plugin (`nina-my-universe-uplink`)

```
src/Uplink.Core/            net8.0, nessun riferimento NINA
  Model/        FrameRecord, FrameBatch, BatchResponse, HealthResponse
  Identity/     FrameIdFactory (SHA-256 profilo|camera|capturedAt|nome senza estensione)
  Spool/        FileSpoolQueue (pending\, dead\, scrittura atomica)
  Transport/    UplinkClient (HttpClient unico, gzip, timeout), RetryPolicy, ErrorClassifier
  Pipeline/     UploadWorker (Channel<T>, batch 25 / 20 s, drain 20 s), ThumbnailPolicy, UplinkState
  Settings/     UplinkSettings, SettingsValidator (https o localhost)
  Report/       HtmlReportWriter (debug locale)
src/Uplink.Plugin/          net8.0-windows7.0, WPF
  MyUniverseUplinkPlugin.cs, ImageSavedListener, NinaFrameMapper, NinaThumbnailRenderer,
  ProfileSettingsStore (PluginOptionsAccessor + DPAPI), NinaLogBridge,
  OptionsViewModel + Options.xaml
tests/Uplink.Core.Tests/    xUnit
contract/                   fixture identiche al sito
docs/nina-api-notes.md      firme copiate dagli XML/assembly del package locale
scripts/build.ps1, install-local.ps1, test.ps1   (log su file)
```

Opzioni (4 sezioni): **Connection** (Enabled, URL, Device ID, Token, *Test connection*,
header extra per il bypass della protezione Preview); **What to send** (tipi immagine, solo con
target); **Thumbnails** (Always/Sampled/Never, lato lungo, qualità); **Status** (stato, pending,
dead, inviati stanotte, ultimo errore, *Send last image*, *Flush now*, *Retry dead*,
*Open folder*, *HTML report on/off*).

Cartella dati: `%LOCALAPPDATA%\NINA\Plugins\MyUniverseUplink\` con `pending\`, `dead\`, `thumbs\`,
`logs\`, `report\`.

---

## 6. Vincoli d'ambiente verificati (19/10/2026)

| Vincolo | Conseguenza |
|---|---|
| Il container cloud **non** raggiunge NuGet né i download .NET; nemmeno la shell Linux sul PC | Il plugin si compila e si testa **su Windows**, in PowerShell, guidato da me con il controllo del computer. I log di build/test vanno su file in una cartella collegata e li leggo da lì. |
| Il PC ha la cache NuGet completa di `nina.*` 3.2.0.9001 e il lab `nina-plugin-lab` con HelloNina già compilato | Build offline possibile; gli XML di documentazione del package bastano per `nina-api-notes.md`. |
| Né il container né la shell sul PC raggiungono `*.vercel.app` | I test contro la preview passano da Windows: il plugin stesso, `Invoke-RestMethod` in PowerShell, il browser integrato. |
| Il container raggiunge npm | `npm install`, build, vitest con PGlite e `next dev` + curl **in locale** funzionano qui. |
| Il PC è il PC dell'osservatorio (`serversat`) | Test solo con un **profilo NINA dedicato con dispositivi simulatori**. Nessuna azione su montatura, cupola o camera reali. Mai staccare la rete del PC (interromperebbe anche il collegamento con me). |
| Le preview Vercel hanno di default la Deployment Protection | Si usa la *Protection Bypass for Automation*: il plugin manda l'header `x-vercel-protection-bypass` (opzione "header extra"). |

---

## 7. Milestone

Ogni milestone ha una Definition of Done (DoD). Non si passa alla successiva senza.

### M0 — Preparazione (solo orchestratore, niente subagenti)
- Branch `feat/uplink` da `main`; `npm install`; `npm run build` di partenza verde.
- Repo plugin: cartella sul PC, `git init`, remote GitHub; scheletro con i tre progetti.
- `docs/nina-api-notes.md` dalle firme reali: `PluginBase`, `IPluginManifest`,
  `IImageSaveMediator`/`ImageSavedEventArgs`, `PluginOptionsAccessor`, `IProfileService`,
  `IImageDataFactory`/rendering immagine, logger.
- Fixture `contract/*.json` scritte e committate in entrambi i repo.
- Vercel: database Neon (Preview), Blob store, env `UPLINK_INGEST_TOKENS`,
  `UPLINK_FAULT_INJECTION=1` (solo Preview), secret di bypass.
- NINA: profilo "Uplink Test" con camera/montatura/ruota filtri simulate; vecchi plugin in `_to_delete\`.

**DoD:** plugin vuoto "My Universe Uplink" compilato su Windows, visibile in NINA, nessun errore nel
log; la preview del branch risponde; fixture identiche nei due repo.

### M1 — Sito: ingest
Pacchetti WP-S1, WP-S2. **DoD:** vitest verde in locale; sulla preview, da PowerShell con la
fixture: `accepted: 1`, ripetuto → `duplicates: 1`, token errato → 401, `/health` autenticato.

### M2 — Plugin end-to-end
Pacchetti WP-P1…WP-P4. **DoD:** `dotnet test` verde su Windows; con la camera simulata, una
sequenza di 20 LIGHT da 2 s produce 20 righe nel DB Preview e le thumbnail campionate; il report
HTML locale si aggiorna; impostazioni persistite dopo riavvio e indipendenti per profilo.

### M3 — Sito: visualizzazione
Pacchetti WP-S3, WP-S4. **DoD:** CSV importato nel DB Preview senza duplicati; `/sessioni-astronomiche`
in preview mostra i totali di prima **più** i frame nuovi, il box "ultima ripresa" e i frame della
notte con thumbnail; `npm run build` verde.

### M4 — Collaudo e robustezza
Pacchetto WP-P5 + scenari §8.3. **DoD:** tutti gli scenari spuntati, nessuna perdita né duplicato,
nessun blocco della UI di NINA; resoconto con log e screenshot.

### Dopo la v1
v1.1: `Send Site Event` + `/api/uplink/events`, packaging e GitHub Actions, deploy in produzione
con database Production. v1.2: auto-integrazione della galleria (`integration_total`).

---

## 8. Test

### 8.1 Automatici
- **Sito (container, vitest + PGlite):** auth, schema e tolerant reader, deduplica `frame_id` e
  chiave naturale, `nightId` (mezzanotte, DST), import CSV, aggregati dashboard identici CSV↔DB,
  fixture di contratto, fault injection spenta fuori Preview.
- **Plugin (Windows, xUnit):** `FrameIdFactory`, `FileSpoolQueue` (crash a metà scrittura, replay),
  `RetryPolicy`, `ErrorClassifier`, `ThumbnailPolicy`, `UploadWorker` con client finto, contratto
  JSON contro la fixture, `HtmlReportWriter`.

### 8.2 Locale (container)
`next dev` + `curl` con la fixture prima di ogni push.

### 8.3 Preview + NINA (manuali, guidati da me sul PC)

| # | Scenario | Atteso |
|---|---|---|
| T1 | Plugin caricato, Options visibili, riavvio NINA | impostazioni conservate |
| T2 | Cambio profilo | impostazioni indipendenti |
| T3 | 20 LIGHT simulati da 2 s | 20 frame sul sito, 0 duplicati, thumbnail campionate, report HTML aggiornato |
| T4 | URL irraggiungibile a metà sequenza, poi ripristinato | `pending` cresce, poi si svuota; nessuna perdita |
| T5 | `Stop-Process` di NINA con coda piena, riavvio | replay completo, nessun duplicato |
| T6 | Token errato | stato *Auth error*, nessun retry infinito, messaggio chiaro |
| T7 | Chiusura di NINA durante l'invio | chiusura entro 20 s, UI mai bloccata |
| T8 | Fault injection 500/429/413 | backoff, rispetto di `Retry-After`, batch dimezzato |
| T9 | Tempi di salvataggio con plugin attivo e spento | differenza trascurabile (handler < 50 ms nel log) |
| T10 | Pagina in preview nel browser | dati e thumbnail visibili, totali coerenti |

---

## 9. Organizzazione del lavoro con subagenti

**Ruoli.**
- **Orchestratore (io, Opus):** M0; scrive i brief dei pacchetti; revisiona ogni diff; esegue build,
  test e commit; sincronizza il sorgente del plugin sul PC; guida PowerShell, NINA e browser.
  Il controllo del computer resta solo all'orchestratore.
- **Implementatori (Haiku 5.5):** un pacchetto alla volta per repo, al massimo **due in parallelo**
  (uno sul sito, uno sul plugin), mai due sullo stesso repo.
- **Verificatore (Haiku 5.5, sessione separata):** a fine M1 e M3, confronta contratto, fixture e
  implementazioni senza aver visto il lavoro.

**Ogni brief contiene:** obiettivo, file da creare/modificare, interfacce già decise, fixture,
vincoli (§0 e regole dei repo), test da far passare, file vietati.
**Ciclo C#:** gli implementatori non possono compilare. Io compilo su Windows e rimando gli errori
allo stesso subagente; per questo i pacchetti C# sono piccoli e usano solo le firme di
`nina-api-notes.md`.

| WP | Repo | Contenuto | Dipende da |
|---|---|---|---|
| WP-S1 | sito | `schema.ts`, `auth.ts`, `night.ts`, migrazione SQL, `db.ts`, test | M0 |
| WP-S2 | sito | route `frames` e `health`, `thumbs.ts`, `fault.ts`, `uplink-token.mjs`, `docs/uplink-api.md`, test | WP-S1 |
| WP-P1 | plugin | Core: Model, Identity, Settings, contratto JSON, test | M0 |
| WP-P2 | plugin | Core: Spool, Transport, Pipeline, ThumbnailPolicy, Report, test | WP-P1 |
| WP-P3 | plugin | Plugin: entry, listener, mapper, settings store DPAPI, log bridge | WP-P2, api-notes |
| WP-P4 | plugin | Plugin: thumbnail renderer, Options XAML + ViewModel | WP-P3 |
| WP-S3 | sito | import CSV, `session-dashboard.ts` su DB con fallback | WP-S2 |
| WP-S4 | sito | box "ultima ripresa", frame della notte, cache invalidata all'ingest | WP-S3 |
| WP-P5 | plugin | correzioni dal collaudo M4 | M2 |

Parallelo possibile: WP-S1/S2 insieme a WP-P1/P2; WP-S3/S4 insieme a WP-P3/P4.

---

## 10. Cosa serve da Robert prima del via

1. **Repo GitHub** `RobertPanai/nina-my-universe-uplink` (anche privato), oppure ok a crearlo io dal browser.
2. **Accesso dal browser integrato** a Vercel e GitHub, per creare Neon e Blob, le variabili di
   Preview e il secret di bypass.
3. **Profilo NINA di test** con soli simulatori, oppure ok a crearlo io; conferma che l'osservatorio
   non sta riprendendo durante i test.
4. **Permessi sul PC:** cartella `Documents\nina-my-universe-uplink` (la creo io se mi dai accesso a
   Documenti); spostamento dei vecchi plugin in `_to_delete\`; uso di PowerShell, NINA e browser.

---

## 11. Domande rimandate (non bloccano la v1)

- Dominio e deploy in produzione (Q1) → v1.1.
- Alias nomi target NINA → slug galleria (Q17) → v1.2.
- Notifiche Telegram/ntfy (Q20) → fuori scopo.
- Pubblicazione nel plugin browser di NINA (Q18) → dopo la v1; nessun dominio cablato nel codice.
