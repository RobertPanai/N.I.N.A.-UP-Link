# N.I.N.A. My Universe Uplink

Plugin per N.I.N.A. 3.2 che invia i metadati dei frame salvati al sito
[robertpanai.com](https://robertpanai.com). Il sito è la vetrina pubblica dei dati.

## Stato

Fase M0 (preparazione). Nessun codice applicativo ancora.

## Struttura

- `docs/plan-v2.md`: piano operativo attuale.
- `docs/plan-v1.md`: piano di dettaglio originale, riferimento per ciò che non cambia.
- `contract/`: fixture JSON del contratto API. Identiche a quelle nel repository del sito (`RobertPanai/sito`, cartella `contract/`).

## Regola di contratto

Ogni modifica al formato dei dati cambia prima le fixture in `contract/`, poi entrambi i repository.
