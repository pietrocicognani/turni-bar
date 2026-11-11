namespace Turni.Web;

public record Dipendente(int Id, string Nome, string Ruolo, int OreSettimanali);

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

public record Settimana(DateOnly Lunedi, List<Turno> Turni, List<OreDipendente> Ore);
