using System.Globalization;

namespace Turni.Web;

/// <summary>
/// Le regole per fare i turni di un bar. Solo calcoli, niente file né HTTP: si testano da sole.
/// </summary>
public static class RegoleTurni
{
    public static readonly TimeSpan DurataMinima = TimeSpan.FromHours(2);
    public static readonly TimeSpan DurataMassima = TimeSpan.FromHours(10);

    /// <summary>D.Lgs. 66/2003, art. 7: 11 ore di riposo consecutivo ogni 24.</summary>
    public static readonly TimeSpan RiposoMinimo = TimeSpan.FromHours(11);

    /// <summary>D.Lgs. 66/2003, art. 4: massimo 48 ore a settimana, straordinari compresi.</summary>
    public const double MassimoOreSettimanali = 48;

    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    public static DateOnly Lunedi(DateOnly giorno) =>
        giorno.AddDays(-(((int)giorno.DayOfWeek + 6) % 7)); // DayOfWeek parte dalla domenica = 0

    /// <summary>Un turno conta nella settimana in cui inizia (quello di domenica notte è della domenica).</summary>
    public static double OreNellaSettimana(IEnumerable<Turno> turni, DateOnly lunedi) =>
        turni.Where(t => Lunedi(DateOnly.FromDateTime(t.Inizio)) == lunedi).Sum(t => t.Ore);

    /// <param name="turniDelDipendente">Gli altri turni della stessa persona.</param>
    public static EsitoVerifica Verifica(NuovoTurno nuovo, Dipendente dipendente, IEnumerable<Turno> turniDelDipendente)
    {
        var errori = new List<string>();
        var avvisi = new List<string>();
        var durata = nuovo.Fine - nuovo.Inizio;

        if (durata <= TimeSpan.Zero)
        {
            errori.Add("La fine del turno deve essere dopo l'inizio.");
            return new EsitoVerifica(errori, avvisi);
        }
        if (durata < DurataMinima)
        {
            errori.Add($"Un turno dura almeno {DurataMinima.TotalHours} ore.");
        }
        if (durata > DurataMassima)
        {
            errori.Add($"Un turno dura al massimo {DurataMassima.TotalHours} ore.");
        }

        var altri = turniDelDipendente.ToList();
        foreach (var t in altri)
        {
            // Due intervalli si sovrappongono se ognuno inizia prima che l'altro finisca
            if (t.Inizio < nuovo.Fine && nuovo.Inizio < t.Fine)
            {
                errori.Add($"{dipendente.Nome} ha già un turno {Descrivi(t)}.");
                continue;
            }

            // Pausa tra i due turni, qualunque dei due venga prima
            var pausa = t.Inizio >= nuovo.Fine ? t.Inizio - nuovo.Fine : nuovo.Inizio - t.Fine;
            if (pausa < RiposoMinimo)
            {
                errori.Add($"Tra questo turno e quello {Descrivi(t)} ci sono solo {DurataLeggibile(pausa)} " +
                           $"di riposo: ne servono almeno {RiposoMinimo.TotalHours}.");
            }
        }

        var lunedi = Lunedi(DateOnly.FromDateTime(nuovo.Inizio));
        double ore = OreNellaSettimana(altri, lunedi) + durata.TotalHours;
        if (ore > MassimoOreSettimanali)
        {
            errori.Add(string.Create(Italiano,
                $"Con questo turno {dipendente.Nome} arriverebbe a {ore:0.#} ore in settimana: il massimo di legge è {MassimoOreSettimanali}."));
        }
        else if (ore > dipendente.OreSettimanali)
        {
            // Non è vietato, ma chi fa i turni deve saperlo: sono straordinari da pagare
            avvisi.Add(string.Create(Italiano,
                $"{dipendente.Nome} arriva a {ore:0.#} ore, oltre le {dipendente.OreSettimanali} del contratto: sono straordinari."));
        }

        return new EsitoVerifica(errori, avvisi);
    }

    private static string Descrivi(Turno t) =>
        t.Inizio.ToString("dddd d/M 'dalle' HH:mm", Italiano) + t.Fine.ToString(" 'alle' HH:mm", Italiano);

    private static string DurataLeggibile(TimeSpan durata) =>
        durata.Minutes == 0 ? $"{(int)durata.TotalHours} ore" : $"{(int)durata.TotalHours} ore e {durata.Minutes} minuti";
}
