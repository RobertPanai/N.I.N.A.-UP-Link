# Uplink multi-sito: principi di design

Stato: vincolo di progetto deciso da Robert il 19/10/2026. Il sito `robertpanai.com` è la prima
implementazione del ricevitore, ma il plugin e il contratto devono funzionare con qualsiasi sito che
implementi lo stesso contratto.

## Regole

1. **Il plugin non conosce il sito.** Riceve solo: `baseUrl`, `token`, `deviceId`, impostazioni.
   Nessun dominio, nome di sito, nome di pagina o percorso di galleria è cablato nel codice o nei log.
2. **Il contratto è neutro.** Nomi di campi e endpoint non contengono termini del sito di Robert
   (`sessioni`, `galleria`, `robertpanai`). Il prefisso è `/api/uplink/v1/…` su ogni sito.
3. **Il ricevitore è un modulo.** Tutta la logica (auth, validazione, dedup, `nightId`, store)
   vive in `src/lib/uplink/` senza import dalle pagine del sito. Le pagine sono un consumatore
   separato. Il modulo deve poter essere estratto in un pacchetto (`@uplink/receiver`) più avanti.
4. **Lo store è un'interfaccia.** `UplinkStore` con implementazione Postgres e in memoria per test.
   Il sito sceglie l'implementazione via configurazione, non via codice.
5. **Thumbnail via adapter.** `BlobAdapter` con implementazione Vercel Blob; altri siti possono
   usare S3/R2 o disattivare le thumbnail (`acceptsThumbnails: false` in `/health`).
6. **Token per dispositivo, non per sito.** Un dispositivo può inviare a più siti con token diversi.
   Il plugin supporta **un** endpoint per profilo NINA nella v1; la lista di endpoint è v1.1.
7. **Fixture neutre.** Le fixture in `contract/` usano dati generici (target di esempio, nessun
   nome di osservatorio o di sito). I test di ogni implementazione le verificano.
8. **Configurazione del sito in un solo file.** `uplink.config.ts` con: nome del sito, origine
   consentita, limiti (batch, byte, thumbnail), adapter scelti, versione del contratto.
9. **Nessun testo editoriale nel codice.** Messaggi di errore e UI tecnica restano neutri; il tono
   del sito resta nei contenuti del sito.

## Conseguenze per la v2

- Endpoint: `/api/uplink/v1/frames`, `/api/uplink/v1/health` (il prefisso `/api/nina/v1` del branch
  `test/nina-uplink` è da abbandonare).
- Tabella: `uplink_frames` con colonne neutre; i dati di pagina (`sessioni`) stanno in una vista o
  in un adattatore del sito, non nello schema.
- Pagine e box "ultima ripresa" sono codice del sito di Robert, non del modulo.
- Il plugin 0.x di test non è prodotto: si riparte dal contratto v1.
