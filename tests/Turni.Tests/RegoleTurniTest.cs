using Turni.Web;

namespace Turni.Tests;

public class RegoleTurniTest
{
    private static readonly Dipendente Luca = new(2, "Luca Fabbri", "Barista", 40);
    private static readonly Dipendente Elena = new(5, "Elena Ricci", "Cassa", 20);

    private static DateTime T(string dataOra) => DateTime.Parse(dataOra);

    private static NuovoTurno Nuovo(string inizio, string fine, int dipendente = 2) => new(dipendente, T(inizio), T(fine));

    private static Turno Esistente(string inizio, string fine, int id = 1, int dipendente = 2) => new(id, dipendente, T(inizio), T(fine));

    [Fact]
    public void TurnoNormale_Valido()
    {
        var esito = RegoleTurni.Verifica(Nuovo("2026-11-02 07:00", "2026-11-02 13:00"), Luca, []);

        Assert.True(esito.Valido);
        Assert.Empty(esito.Avvisi);
    }

    [Fact]
    public void TurnoDiNotte_CheFinisceDopoMezzanotte_Valido()
    {
        Assert.True(RegoleTurni.Verifica(Nuovo("2026-11-06 19:00", "2026-11-07 02:00"), Luca, []).Valido);
    }

    [Theory]
    [InlineData("2026-11-02 13:00", "2026-11-02 13:00", "dopo l'inizio")]
    [InlineData("2026-11-02 13:00", "2026-11-02 07:00", "dopo l'inizio")]
    [InlineData("2026-11-02 13:00", "2026-11-02 14:00", "almeno 2 ore")]
    [InlineData("2026-11-02 07:00", "2026-11-02 18:00", "al massimo 10 ore")]
    public void DurataNonValida_Rifiutata(string inizio, string fine, string messaggio)
    {
        var esito = RegoleTurni.Verifica(Nuovo(inizio, fine), Luca, []);

        Assert.False(esito.Valido);
        Assert.Contains(esito.Errori, e => e.Contains(messaggio));
    }

    [Fact]
    public void TurniSovrapposti_Rifiutati()
    {
        var esito = RegoleTurni.Verifica(Nuovo("2026-11-02 12:00", "2026-11-02 18:00"), Luca,
            [Esistente("2026-11-02 07:00", "2026-11-02 13:00")]);

        Assert.Contains("ha già un turno lunedì 2/11 dalle 07:00 alle 13:00", Assert.Single(esito.Errori));
    }

    [Fact]
    public void RiposoTroppoBreve_DopoIlTurnoDiNotte_Rifiutato()
    {
        // Chiude il bar alle 2, riapre alle 10: solo 8 ore di riposo
        var esito = RegoleTurni.Verifica(Nuovo("2026-11-07 10:00", "2026-11-07 16:00"), Luca,
            [Esistente("2026-11-06 19:00", "2026-11-07 02:00")]);

        Assert.Contains("solo 8 ore di riposo", Assert.Single(esito.Errori));
    }

    [Fact]
    public void RiposoTroppoBreve_ControllatoAncheSeIlNuovoTurnoVienePrima()
    {
        var esito = RegoleTurni.Verifica(Nuovo("2026-11-05 18:00", "2026-11-06 01:30"), Luca,
            [Esistente("2026-11-06 07:00", "2026-11-06 13:00")]);

        Assert.Contains("solo 5 ore e 30 minuti di riposo", Assert.Single(esito.Errori));
    }

    [Fact]
    public void RiposoDiEsattamenteUndiciOre_Valido()
    {
        var esito = RegoleTurni.Verifica(Nuovo("2026-11-07 13:00", "2026-11-07 19:00"), Luca,
            [Esistente("2026-11-06 19:00", "2026-11-07 02:00")]);

        Assert.True(esito.Valido);
    }

    [Fact]
    public void OltreLeOreDelContratto_AvvisoMaNonErrore()
    {
        // Elena ha un part-time da 20 ore: 3 turni da 6 + uno da 6 = 24
        Turno[] settimana =
        [
            Esistente("2026-11-02 08:00", "2026-11-02 14:00", 1, 5),
            Esistente("2026-11-03 08:00", "2026-11-03 14:00", 2, 5),
            Esistente("2026-11-04 08:00", "2026-11-04 14:00", 3, 5),
        ];

        var esito = RegoleTurni.Verifica(Nuovo("2026-11-05 08:00", "2026-11-05 14:00", 5), Elena, settimana);

        Assert.True(esito.Valido);
        Assert.Contains("24 ore, oltre le 20 del contratto", Assert.Single(esito.Avvisi));
    }

    [Fact]
    public void OltreLe48OreSettimanali_Rifiutato()
    {
        // 5 turni da 10 ore = 50: il quinto non entra
        var settimana = Enumerable.Range(0, 4)
            .Select(i => new Turno(i + 1, 2, T("2026-11-02 08:00").AddDays(i), T("2026-11-02 18:00").AddDays(i)))
            .ToList();

        var esito = RegoleTurni.Verifica(Nuovo("2026-11-06 08:00", "2026-11-06 18:00"), Luca, settimana);

        Assert.Contains("50 ore", Assert.Single(esito.Errori));
    }

    [Fact]
    public void LeOreDellaSettimanaPrimaNonContano()
    {
        var settimanaScorsa = Enumerable.Range(0, 4)
            .Select(i => new Turno(i + 1, 2, T("2026-10-26 08:00").AddDays(i), T("2026-10-26 18:00").AddDays(i)))
            .ToList();

        Assert.True(RegoleTurni.Verifica(Nuovo("2026-11-02 08:00", "2026-11-02 18:00"), Luca, settimanaScorsa).Valido);
    }

    [Theory]
    [InlineData("2026-11-02", "2026-11-02")] // lunedì
    [InlineData("2026-11-05", "2026-11-02")] // giovedì
    [InlineData("2026-11-08", "2026-11-02")] // domenica: è ancora la settimana del 2
    public void Lunedi_DellaSettimana(string giorno, string lunedi)
    {
        Assert.Equal(DateOnly.Parse(lunedi), RegoleTurni.Lunedi(DateOnly.Parse(giorno)));
    }
}
