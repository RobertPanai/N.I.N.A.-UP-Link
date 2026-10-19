# N.I.N.A. My Universe Uplink

Plugin per [N.I.N.A.](https://nighttime-imaging.eu/) 3.2 che, a ogni frame salvato, invia al tuo sito i dati della ripresa e una miniatura. Può anche pubblicare sul sito una o più dirette (per esempio una camera all-sky).

Nasce per il sito [Robert Panai - My Universe](https://github.com/RobertPanai/sito), ma il contratto dei dati è neutro (`contract/`) e si può usare con qualsiasi server che lo implementi.

Versione attuale: **0.3.0**. Testato con N.I.N.A. 3.2.0.9001.

---

## Cosa fa

### Invio dei frame

Ogni volta che N.I.N.A. salva un'immagine, il plugin:

1. legge i dati del frame: soggetto, filtro, esposizione, HFR, stelle, camera, telescopio, data della ripresa;
2. prepara una miniatura JPEG stirata (lato lungo e qualità regolabili, di norma 400 px e qualità 70, circa 8 KB);
3. mette il tutto in una coda su disco;
4. la coda invia i frame al sito a lotti, con compressione gzip.

Il sito li raccoglie nella pagina delle sessioni astronomiche: ultimi frame ricevuti, notti, integrazione totale per soggetto.

Il plugin è pensato per non disturbare mai la ripresa:

- **non blocca N.I.N.A.**: l'invio gira in background, ogni errore è intercettato;
- **non perde frame**: se la rete cade o il sito non risponde, i frame restano nella coda su disco e vengono rinviati al riavvio, con attese crescenti e rispetto di `Retry-After`;
- **non duplica**: ogni frame ha un identificativo stabile e il sito ignora i doppioni;
- **non invia l'immagine originale**: solo metadati e miniatura.

Per impostazione predefinita invia i frame **LIGHT con un soggetto**. Si possono includere anche altri tipi (DARK, FLAT, BIAS, SNAPSHOT) dalle opzioni.

### Dirette

Dalle opzioni si possono aggiungere link a dirette pubbliche (fino a 12), ciascuna con titolo e descrizione. Il sito mostra la sezione **Dirette** solo se almeno una è attiva: spegnendo l'interruttore, o svuotando la lista, la sezione sparisce dal sito.

Se il link porta a una singola immagine aggiornata dalla camera, il sito la mostra nella pagina e la ricarica a intervalli.

### Pannello Activity

Nelle opzioni c'è un elenco in tempo reale di tutto quello che fa il plugin, nello stile di un bot di stato: frame ricevuto, miniatura pronta, frame in coda, lotto inviato, coda vuota, errori, sincronizzazione delle dirette. Le stesse righe finiscono in `logs\activity.log`. Token e link delle dirette non vengono mai scritti nei log.

### Report HTML

Opzionale: una pagina HTML locale con lo stato dell'invio, salvata nella cartella `report`.

---

## Requisiti

- Windows con **N.I.N.A. 3.2** (il plugin usa .NET 8, già incluso in N.I.N.A.)
- Un server che accetti il contratto del plugin (vedi sotto). Per il sito di Robert: un deployment di `RobertPanai/sito` con il database attivo
- Per compilare da sorgente: **.NET SDK 8.0** (versione fissata in `global.json`)

---

## Installazione

### 1. Compilare il plugin

Da PowerShell, nella cartella del repository:

```powershell
dotnet build MyUniverseUplink.sln -c Release
dotnet test  MyUniverseUplink.sln -c Release   # facoltativo: 244 test
```

Il risultato è in `src\Uplink.Plugin\bin\Release\net8.0-windows7.0\`.

### 2. Copiare i file in N.I.N.A.

Chiudi N.I.N.A. e copia questi quattro file:

- `MyUniverseUplink.dll`
- `MyUniverseUplink.Core.dll`
- `MyUniverseUplink.pdb`
- `MyUniverseUplink.Core.pdb`

nella cartella:

```
%LOCALAPPDATA%\NINA\Plugins\3.0.0\My Universe Uplink\
```

Creala se non esiste. Riavvia N.I.N.A.: il plugin compare in **Opzioni → Plugin → My Universe Uplink**.

### 3. Creare il token sul sito

Il sito riconosce ogni dispositivo da un identificativo (`deviceId`) e da un token segreto. Nel repository del sito:

```bash
node scripts/uplink-token.mjs <deviceId>
```

Lo script stampa il token da incollare in N.I.N.A. e la riga da aggiungere alla variabile d'ambiente `UPLINK_INGEST_TOKENS` del sito, nella forma `deviceId:sha256`. Sul server resta solo l'impronta, mai il token. Dopo la modifica, ridistribuisci il sito.

Per le dirette, i domini ammessi si dichiarano in `UPLINK_STREAM_ALLOWED_HOSTS` (elenco separato da virgole, per esempio `astro.scopelink.io`).

### 4. Configurare il plugin

In **Opzioni → Plugin → My Universe Uplink**:

| Campo | Cosa mettere |
|---|---|
| Server URL | indirizzo https del sito (http solo per localhost) |
| Device ID | lo stesso `deviceId` usato per creare il token |
| Token | il token generato al punto 3; viene cifrato con DPAPI e legato al tuo utente Windows |
| Test connection | verifica che sito e token siano corretti |

Poi scegli i tipi di frame da inviare e le impostazioni delle miniature, e premi Salva. Le impostazioni valgono per il profilo di N.I.N.A. attivo.

### 5. Aggiungere una diretta (facoltativo)

Nella sezione **Live streams** attiva l'interruttore, premi **Add**, inserisci titolo, descrizione e link https, poi **Sync now**. Lo stato sotto la lista dice se la pubblicazione è riuscita.

### Configurazione senza interfaccia (`bootstrap.json`)

Per installare il plugin su più computer o in un ambiente di test, scrivi un file `bootstrap.json` in `%LOCALAPPDATA%\NINA\Plugins\MyUniverseUplink\`. Al primo avvio il plugin lo applica al profilo attivo e lo cancella:

```json
{
  "enabled": true,
  "serverUrl": "https://tuo-sito.example",
  "deviceId": "osservatorio",
  "token": "…",
  "imageTypes": ["LIGHT"],
  "thumbnailMode": "Always",
  "thumbnailLongEdge": 400,
  "thumbnailQuality": 70,
  "batchSize": 25,
  "streamsEnabled": true,
  "streams": [
    { "title": "Camera all-sky", "description": "Cielo dal vivo", "url": "https://…", "enabled": true }
  ]
}
```

---

## Dove stanno i dati

```
%LOCALAPPDATA%\NINA\Plugins\MyUniverseUplink\
  pending\   frame in attesa di invio
  dead\      frame rifiutati in modo permanente dal server (da guardare a mano)
  thumbs\    miniature
  logs\      uplink.log e activity.log
  report\    report HTML
```

Se un frame finisce in `dead`, il motivo è in `logs\uplink.log`.

---

## Sviluppo

```
src/Uplink.Core/     logica indipendente da N.I.N.A.: modelli, coda, invio, ripetizioni, dirette
src/Uplink.Plugin/   integrazione con N.I.N.A.: ascolto di ImageSaved, opzioni WPF, impostazioni per profilo
tests/               test xUnit
contract/            fixture JSON del contratto (identiche a quelle del repository del sito)
docs/                piano di progetto e note sull'API di N.I.N.A.
scripts/             strumenti per il collaudo automatico su un PC con N.I.N.A.
```

Regola di contratto: ogni modifica al formato dei dati cambia prima le fixture in `contract/`, poi entrambi i repository. Il formato è descritto in `docs/` e, lato server, in `docs/uplink-api.md` del repository del sito.

## Limiti noti

- Manca un pacchetto di installazione: per ora si compila e si copia a mano (previsto nella versione 1.1).
- Le dirette si possono incorporare nel sito solo se il link è un'immagine, oppure una pagina che non vieta l'inserimento in altri siti.
- Il plugin è stato provato con il simulatore di N.I.N.A.; il collaudo con una sessione reale è ancora da fare.
