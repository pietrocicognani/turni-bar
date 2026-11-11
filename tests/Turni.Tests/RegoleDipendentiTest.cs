using Turni.Web;

namespace Turni.Tests;

public class RegoleDipendentiTest
{
    private static readonly Dipendente Luca = new(2, "Luca Fabbri", "Barista", 40);

    private static DateOnly D(string data) => DateOnly.Parse(data);

    private static NuovoTurno Turno(string inizio, string fine) => new(9, DateTime.Parse(inizio), DateTime.Parse(fine));

    [Fact]
    public void DatiValidi_Accettati()
    {
        var esito = RegoleDipendenti.Verifica(new DatiDipendente("Marco Valli", "Sala", 24), [Luca]);

        Assert.True(esito.Valido);
    }

    [Theory]
    [InlineData("M", "Sala", 24, "da 2 a 60 caratteri")]
    [InlineData("   ", "Sala", 24, "da 2 a 60 caratteri")]
    [InlineData("Marco Valli", "", 24, "Indica il ruolo")]
    [InlineData("Marco Valli", "Sala", 0, "da 1 a 40")]
    [InlineData("Marco Valli", "Sala", 41, "da 1 a 40")]
    public void DatiNonValidi_Rifiutati(string nome, string ruolo, int ore, string messaggio)
    {
        var esito = RegoleDipendenti.Verifica(new DatiDipendente(nome, ruolo, ore), []);

        Assert.Contains(messaggio, Assert.Single(esito.Errori));
    }

    [Fact]
    public void CampiMancanti_TuttiGliErroriInsieme()
    {
        var esito = RegoleDipendenti.Verifica(new DatiDipendente(null, null, 0), []);

        Assert.Equal(3, esito.Errori.Count);
    }

    [Fact]
    public void NomeGiaInSquadra_Rifiutato_SenzaGuardareMaiuscoleESpazi()
    {
        var esito = RegoleDipendenti.Verifica(new DatiDipendente("  luca FABBRI ", "Sala", 20), [Luca]);

        Assert.Contains("già Luca Fabbri", Assert.Single(esito.Errori), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Licenziamento_Valido()
    {
        Assert.True(RegoleDipendenti.VerificaLicenziamento(Luca, D("2030-01-10")).Valido);
    }

    [Fact]
    public void Licenziamento_DiChiHaGiaLasciato_Rifiutato()
    {
        var uscito = Luca with { Cessazione = D("2030-01-05") };

        var esito = RegoleDipendenti.VerificaLicenziamento(uscito, D("2030-01-10"));

        Assert.Contains("ha già lasciato", Assert.Single(esito.Errori));
    }

    [Fact]
    public void Licenziamento_PrimaDellAssunzione_Rifiutato()
    {
        var nuovo = Luca with { Assunzione = D("2030-01-07") };

        var esito = RegoleDipendenti.VerificaLicenziamento(nuovo, D("2030-01-06"));

        Assert.Contains("prima dell'assunzione", Assert.Single(esito.Errori));
    }

    [Fact]
    public void Licenziamento_IlGiornoStessoDellAssunzione_Valido()
    {
        var nuovo = Luca with { Assunzione = D("2030-01-07") };

        Assert.True(RegoleDipendenti.VerificaLicenziamento(nuovo, D("2030-01-07")).Valido);
    }

    // Le date del rapporto valgono anche per i turni (controllo in RegoleTurni)

    [Fact]
    public void Turno_PrimaDellAssunzione_Rifiutato()
    {
        var nuovo = Luca with { Assunzione = D("2030-01-08") };

        var esito = RegoleTurni.Verifica(Turno("2030-01-07 07:00", "2030-01-07 13:00"), nuovo, []);

        Assert.Contains("è in squadra dal 8/1/2030", Assert.Single(esito.Errori));
    }

    [Fact]
    public void Turno_DopoLUltimoGiorno_Rifiutato()
    {
        var uscito = Luca with { Cessazione = D("2030-01-08") };

        var esito = RegoleTurni.Verifica(Turno("2030-01-09 07:00", "2030-01-09 13:00"), uscito, []);

        Assert.Contains("non è più in squadra", Assert.Single(esito.Errori));
    }

    [Fact]
    public void TurnoDiNotte_CheIniziaLUltimoGiorno_Valido()
    {
        // conta il giorno in cui inizia: la chiusura del locale dopo mezzanotte è ancora lavoro dell'ultimo giorno
        var uscito = Luca with { Cessazione = D("2030-01-08") };

        Assert.True(RegoleTurni.Verifica(Turno("2030-01-08 19:00", "2030-01-09 02:00"), uscito, []).Valido);
    }

    [Theory]
    [InlineData("2030-01-06", false)] // la domenica prima dell'assunzione
    [InlineData("2030-01-07", true)]
    [InlineData("2030-01-10", true)]
    [InlineData("2030-01-11", false)] // il giorno dopo l'ultimo
    public void InServizioIl_ConsideraEntrambeLeDate(string giorno, bool atteso)
    {
        var dipendente = Luca with { Assunzione = D("2030-01-07"), Cessazione = D("2030-01-10") };

        Assert.Equal(atteso, dipendente.InServizioIl(D(giorno)));
    }

    [Theory]
    [InlineData("2029-12-31", false)]
    [InlineData("2030-01-07", true)]  // assunto mercoledì 9: la settimana lo mostra
    [InlineData("2030-01-14", true)]  // ultimo giorno lunedì 14: la settimana lo mostra
    [InlineData("2030-01-21", false)]
    public void InServizioNellaSettimana_BastaUnGiorno(string lunedi, bool atteso)
    {
        var dipendente = Luca with { Assunzione = D("2030-01-09"), Cessazione = D("2030-01-14") };

        Assert.Equal(atteso, dipendente.InServizioNellaSettimana(D(lunedi)));
    }
}
