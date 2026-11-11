using System.Text.Json;

namespace Turni.Web;

/// <summary>
/// I dati stanno in un file JSON. Tutte le operazioni passano dal lock: controllo delle regole
/// e salvataggio avvengono insieme, così due richieste contemporanee non possono
/// inserire due turni che si sovrappongono.
/// </summary>
public class ArchivioTurni
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _percorso;
    private readonly object _lock = new();
    private readonly DatiTurni _dati;

    public ArchivioTurni(string percorso, bool turniDiEsempio)
    {
        _percorso = percorso;
        if (File.Exists(percorso))
        {
            _dati = JsonSerializer.Deserialize<DatiTurni>(File.ReadAllText(percorso), Json)
                ?? throw new InvalidDataException($"Il file {percorso} è vuoto o non valido.");
        }
        else
        {
            _dati = new DatiTurni { Dipendenti = SquadraIniziale() };
        }

        // I file salvati prima delle assunzioni non hanno il contatore: riparto dall'Id più alto
        int idMassimo = _dati.Dipendenti.Select(d => d.Id).DefaultIfEmpty(0).Max();
        _dati.ProssimoIdDipendente = Math.Max(_dati.ProssimoIdDipendente, idMassimo + 1);

        if (!File.Exists(percorso))
        {
            if (turniDiEsempio)
            {
                CreaTurniDiEsempio(Lunedi(DateOnly.FromDateTime(DateTime.Today)));
            }
            Salva();
        }
    }

    public List<Dipendente> Dipendenti()
    {
        lock (_lock)
        {
            return _dati.Dipendenti.OrderBy(d => d.Nome).ToList();
        }
    }

    public Dipendente? TrovaDipendente(int id)
    {
        lock (_lock)
        {
            return _dati.Dipendenti.FirstOrDefault(d => d.Id == id);
        }
    }

    public Settimana Settimana(DateOnly giorno)
    {
        var lunedi = Lunedi(giorno);
        lock (_lock)
        {
            var turni = _dati.Turni
                .Where(t => Lunedi(DateOnly.FromDateTime(t.Inizio)) == lunedi)
                .OrderBy(t => t.Inizio)
                .ToList();
            var inServizio = _dati.Dipendenti
                .Where(d => d.InServizioNellaSettimana(lunedi))
                .OrderBy(d => d.Nome)
                .ToList();
            var ore = inServizio
                .Select(d => new OreDipendente(d.Id, turni.Where(t => t.DipendenteId == d.Id).Sum(t => t.Ore), d.OreSettimanali))
                .ToList();
            return new Settimana(lunedi, turni, ore, inServizio);
        }
    }

    /// <returns>Il nuovo dipendente, oppure null se i dati non sono validi (i motivi sono nell'esito).</returns>
    public (Dipendente? Dipendente, EsitoVerifica Esito) Assumi(DatiDipendente dati, DateOnly oggi)
    {
        lock (_lock)
        {
            var esito = RegoleDipendenti.Verifica(dati, InSquadra(oggi));
            if (!esito.Valido)
            {
                return (null, esito);
            }

            var dipendente = new Dipendente(_dati.ProssimoIdDipendente++, dati.Nome!.Trim(), dati.Ruolo!.Trim(),
                dati.OreSettimanali, dati.Assunzione ?? oggi);
            _dati.Dipendenti.Add(dipendente);
            Salva();
            return (dipendente, esito);
        }
    }

    /// <summary>Cambia nome, ruolo e ore del contratto. Le date del rapporto non si toccano da qui.</summary>
    /// <returns>Dipendente null ed esito null = non esiste; dipendente null ed esito con errori = dati non validi.</returns>
    public (Dipendente? Dipendente, EsitoVerifica? Esito) Modifica(int id, DatiDipendente dati, DateOnly oggi)
    {
        lock (_lock)
        {
            int indice = _dati.Dipendenti.FindIndex(d => d.Id == id);
            if (indice < 0)
            {
                return (null, null);
            }

            var esito = RegoleDipendenti.Verifica(dati, InSquadra(oggi).Where(d => d.Id != id));
            if (!esito.Valido)
            {
                return (null, esito);
            }

            var aggiornato = _dati.Dipendenti[indice] with
            {
                Nome = dati.Nome!.Trim(),
                Ruolo = dati.Ruolo!.Trim(),
                OreSettimanali = dati.OreSettimanali,
            };
            _dati.Dipendenti[indice] = aggiornato;
            Salva();
            return (aggiornato, esito);
        }
    }

    /// <summary>
    /// Chiude il rapporto di lavoro: il dipendente resta nello storico, i turni dopo l'ultimo giorno vengono cancellati.
    /// </summary>
    /// <returns>Dipendente null ed esito null = non esiste; dipendente null ed esito con errori = non si può licenziare.</returns>
    public (Dipendente? Dipendente, int TurniCancellati, EsitoVerifica? Esito) Licenzia(int id, DateOnly ultimoGiorno)
    {
        lock (_lock)
        {
            int indice = _dati.Dipendenti.FindIndex(d => d.Id == id);
            if (indice < 0)
            {
                return (null, 0, null);
            }

            var esito = RegoleDipendenti.VerificaLicenziamento(_dati.Dipendenti[indice], ultimoGiorno);
            if (!esito.Valido)
            {
                return (null, 0, esito);
            }

            var licenziato = _dati.Dipendenti[indice] with { Cessazione = ultimoGiorno };
            _dati.Dipendenti[indice] = licenziato;
            int cancellati = _dati.Turni.RemoveAll(t =>
                t.DipendenteId == id && DateOnly.FromDateTime(t.Inizio) > ultimoGiorno);
            Salva();
            return (licenziato, cancellati, esito);
        }
    }

    /// <summary>Chi oggi è in squadra o ci entrerà: serve per non avere due nomi uguali. Da chiamare dentro il lock.</summary>
    private IEnumerable<Dipendente> InSquadra(DateOnly oggi) =>
        _dati.Dipendenti.Where(d => d.Cessazione is null || d.Cessazione >= oggi);

    /// <returns>Il turno salvato, oppure null se viola una regola (i motivi sono nell'esito).</returns>
    public (Turno? Turno, EsitoVerifica Esito) Aggiungi(NuovoTurno nuovo, Dipendente dipendente)
    {
        lock (_lock)
        {
            // Rileggo il dipendente qui dentro: nel frattempo potrebbe essere stato licenziato
            int idDipendente = dipendente.Id;
            dipendente = _dati.Dipendenti.First(d => d.Id == idDipendente);
            var esito = RegoleTurni.Verifica(nuovo, dipendente, _dati.Turni.Where(t => t.DipendenteId == dipendente.Id));
            if (!esito.Valido)
            {
                return (null, esito);
            }

            var turno = new Turno(_dati.ProssimoId++, dipendente.Id, nuovo.Inizio, nuovo.Fine);
            _dati.Turni.Add(turno);
            Salva();
            return (turno, esito);
        }
    }

    public bool Elimina(int id)
    {
        lock (_lock)
        {
            if (_dati.Turni.RemoveAll(t => t.Id == id) == 0)
            {
                return false;
            }
            Salva();
            return true;
        }
    }

    private static DateOnly Lunedi(DateOnly giorno) => RegoleTurni.Lunedi(giorno);

    private void Salva()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_percorso))!);
        var temporaneo = _percorso + ".tmp";
        File.WriteAllText(temporaneo, JsonSerializer.Serialize(_dati, Json));
        File.Move(temporaneo, _percorso, overwrite: true);
    }

    private static List<Dipendente> SquadraIniziale() =>
    [
        new(1, "Giulia Neri", "Barista", 40),
        new(2, "Luca Fabbri", "Barista", 40),
        new(3, "Sara Gentili", "Sala", 30),
        new(4, "Davide Casadei", "Cucina", 40),
        new(5, "Elena Ricci", "Cassa", 20),
    ];

    /// <summary>Una settimana tipo, così la prima volta la pagina non è vuota.</summary>
    private void CreaTurniDiEsempio(DateOnly lunedi)
    {
        var orari = new (int Dipendente, int[] Giorni, int Inizio, int Ore)[]
        {
            (1, [0, 1, 2, 3, 4], 7, 6),  // Giulia: apertura, 7-13
            (2, [1, 2, 3, 4, 5], 19, 7), // Luca: sera, 19-2
            (3, [2, 3, 4, 5, 6], 13, 6), // Sara: pomeriggio, 13-19
            (4, [0, 1, 2, 3, 4], 11, 8), // Davide: cucina, 11-19
            (5, [4, 5, 6], 17, 4),       // Elena: part-time nel weekend, 17-21
        };

        foreach (var (idDipendente, giorni, inizio, ore) in orari)
        {
            var dipendente = _dati.Dipendenti.First(d => d.Id == idDipendente);
            foreach (int giorno in giorni)
            {
                var start = lunedi.AddDays(giorno).ToDateTime(new TimeOnly(inizio, 0));
                var nuovo = new NuovoTurno(idDipendente, start, start.AddHours(ore));
                var esito = RegoleTurni.Verifica(nuovo, dipendente, _dati.Turni.Where(t => t.DipendenteId == idDipendente));
                if (esito.Valido)
                {
                    _dati.Turni.Add(new Turno(_dati.ProssimoId++, idDipendente, nuovo.Inizio, nuovo.Fine));
                }
            }
        }
    }

    private class DatiTurni
    {
        /// <summary>Contatore dei turni (il nome resta questo per leggere i file già salvati).</summary>
        public int ProssimoId { get; set; } = 1;
        public int ProssimoIdDipendente { get; set; } = 1;
        public List<Dipendente> Dipendenti { get; set; } = [];
        public List<Turno> Turni { get; set; } = [];
    }
}
