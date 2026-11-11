# Turni bar

Applicazione web per organizzare i turni settimanali dello staff di un bar: una tabella con dipendenti e giorni, turni da aggiungere ed eliminare, ore della settimana confrontate con quelle del contratto.

Il server controlla le regole del lavoro su turni, comprese quelle di legge (D.Lgs. 66/2003): almeno 11 ore di riposo tra un turno e l'altro e massimo 48 ore a settimana. L'idea viene dalle stagioni al banco in riviera, dove i turni serali finiscono alle 2 e quelli del mattino iniziano alle 7.

**Frontend:** HTML, CSS e JavaScript senza framework · **Backend:** C# / ASP.NET Core Minimal API · **Test:** xUnit

## Avvio

Serve il [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/Turni.Web
```

Poi apri http://localhost:5090. Al primo avvio viene creato `src/Turni.Web/data/turni.json` con una squadra e una settimana di esempio.

## Test

```
dotnet test tests/Turni.Tests
```

Ci sono test sulle regole da sole e test sull'API intera con `WebApplicationFactory`, compreso un caso con 10 richieste contemporanee.

## API

| Metodo | Percorso | Risposta |
|---|---|---|
| `GET` | `/api/dipendenti` | `200` con la squadra |
| `GET` | `/api/turni?settimana=2026-11-04` | `200` con i turni e le ore della settimana che contiene quel giorno |
| `POST` | `/api/turni` | `201` + `Location`, `404` dipendente inesistente, `422` regola violata |
| `DELETE` | `/api/turni/{id}` | `204`, oppure `404` |

```json
POST /api/turni
{ "dipendenteId": 2, "inizio": "2026-11-06T19:00:00", "fine": "2026-11-07T02:00:00" }
```

## Scelte

- **Le regole stanno sul server.** Il browser mostra gli errori, ma decide il server: chi chiama l'API direttamente non può saltare i controlli.
- **422 per le regole violate**, 400 solo per le richieste scritte male.
- **Errore e avviso sono diversi.** Superare le 48 ore è vietato; superare le ore del contratto si può, ma sono straordinari e l'app lo segnala.
- **Controllo e salvataggio nello stesso `lock`**, così due richieste insieme non possono inserire due turni sovrapposti.
- **Niente `innerHTML` con i dati del server**: il DOM si costruisce con `createElement` e `textContent`.
- **Turni oltre la mezzanotte**: un turno dalle 19 alle 2 conta nella settimana in cui inizia.
