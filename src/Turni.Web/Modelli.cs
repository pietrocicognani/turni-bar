namespace Turni.Web;

/// <summary>
/// Assunzione e Cessazione delimitano il rapporto di lavoro (null = nessun limite).
/// Chi viene licenziato non si cancella: resta nelle settimane passate, con i suoi turni.
/// </summary>
public record Dipendente(
    int Id,
    string Nome,
    string Ruolo,
    int OreSettimanali,
    DateOnly? Assunzione = null,
    DateOnly? Cessazione = null)
{
    public bool InServizioIl(DateOnly giorno) =>
        (Assunzione is null || Assunzione <= giorno) && (Cessazione is null || Cessazione >= giorno);

    public bool InServizioNellaSettimana(DateOnly lunedi) =>
        (Assunzione is null || Assunzione <= lunedi.AddDays(6)) && (Cessazione is null || Cessazione >= lunedi);
}

/// <summary>Quello che arriva dal browser per assumere o modificare: l'Id lo decide il server.</summary>
public record DatiDipendente(string? Nome, string? Ruolo, int OreSettimanali, DateOnly? Assunzione = null);

/// <summary>Senza data, l'ultimo giorno di lavoro è oggi.</summary>
public record Licenziamento(DateOnly? UltimoGiorno);

/// <summary>Orari "da orologio", senza fuso: un turno alle 18 è alle 18 anche con l'ora legale.</summary>
public record Turno(int Id, int DipendenteId, DateTime Inizio, DateTime Fine)
{
    public double Ore => (Fine - Inizio).TotalHours;
}

/// <summary>Quello che arriva dal browser: l'Id lo decide il server.</summary>
public record NuovoTurno(int DipendenteId, DateTime Inizio, DateTime Fine);

public record EsitoVerifica(List<string> Errori, List<string> Avvisi)
{
    public bool Valido => Errori.Count == 0;
}

public record OreDipendente(int DipendenteId, double Ore, int Contratto);

/// <summary>Dipendenti = chi era in servizio almeno un giorno di quella settimana.</summary>
public record Settimana(DateOnly Lunedi, List<Turno> Turni, List<OreDipendente> Ore, List<Dipendente> Dipendenti);
