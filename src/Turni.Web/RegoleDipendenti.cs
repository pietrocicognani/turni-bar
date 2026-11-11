using System.Globalization;

namespace Turni.Web;

/// <summary>
/// Le regole per assumere, modificare e licenziare. Come RegoleTurni: solo calcoli, niente file né HTTP.
/// </summary>
public static class RegoleDipendenti
{
    public const int LunghezzaMassimaNome = 60;
    public const int LunghezzaMassimaRuolo = 30;

    /// <summary>Tempo pieno nel CCNL Pubblici esercizi.</summary>
    public const int OreContrattoMassime = 40;

    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    /// <param name="colleghi">Gli altri dipendenti ancora in squadra (senza quello che si sta modificando).</param>
    public static EsitoVerifica Verifica(DatiDipendente dati, IEnumerable<Dipendente> colleghi)
    {
        var errori = new List<string>();
        var nome = dati.Nome?.Trim() ?? "";
        var ruolo = dati.Ruolo?.Trim() ?? "";

        if (nome.Length < 2 || nome.Length > LunghezzaMassimaNome)
        {
            errori.Add($"Il nome deve avere da 2 a {LunghezzaMassimaNome} caratteri.");
        }
        if (ruolo.Length == 0 || ruolo.Length > LunghezzaMassimaRuolo)
        {
            errori.Add($"Indica il ruolo (al massimo {LunghezzaMassimaRuolo} caratteri).");
        }
        if (dati.OreSettimanali < 1 || dati.OreSettimanali > OreContrattoMassime)
        {
            errori.Add($"Le ore del contratto vanno da 1 a {OreContrattoMassime} a settimana.");
        }

        // Nella tabella si vede solo il nome: due "Marco" in squadra non si distinguerebbero
        if (nome.Length > 0 && colleghi.Any(d => string.Equals(d.Nome, nome, StringComparison.OrdinalIgnoreCase)))
        {
            errori.Add($"C'è già {nome} in squadra: aggiungi l'iniziale del cognome per distinguerli.");
        }

        return new EsitoVerifica(errori, []);
    }

    public static EsitoVerifica VerificaLicenziamento(Dipendente dipendente, DateOnly ultimoGiorno)
    {
        var errori = new List<string>();

        if (dipendente.Cessazione is { } cessazione)
        {
            errori.Add($"{dipendente.Nome} ha già lasciato la squadra: l'ultimo giorno era il {Data(cessazione)}.");
        }
        else if (dipendente.Assunzione is { } assunzione && ultimoGiorno < assunzione)
        {
            errori.Add($"L'ultimo giorno non può essere prima dell'assunzione ({Data(assunzione)}).");
        }

        return new EsitoVerifica(errori, []);
    }

    public static string Data(DateOnly giorno) => giorno.ToString("d/M/yyyy", Italiano);
}
