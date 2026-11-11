using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Turni.Web;

namespace Turni.Tests;

/// <summary>
/// Avvia l'applicazione vera in memoria e la chiama via HTTP, come farebbe il browser.
/// Ogni test ha il suo file di dati in una cartella temporanea.
/// </summary>
public sealed class ApiTest : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "turni-test-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;

    public ApiTest()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Turni:FileDati", Path.Combine(_cartella, "turni.json"));
            host.UseSetting("Turni:TurniDiEsempio", "false");
        });
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        if (Directory.Exists(_cartella))
        {
            Directory.Delete(_cartella, recursive: true);
        }
    }

    private record RispostaCreazione(Turno Turno, List<string> Avvisi);

    private record RispostaErrore(List<string> Errori);

    private record RispostaLicenziamento(Dipendente Dipendente, int TurniCancellati);

    private Task<HttpResponseMessage> Crea(int dipendente, string inizio, string fine) =>
        _client.PostAsJsonAsync("/api/turni", new { dipendenteId = dipendente, inizio, fine });

    private Task<HttpResponseMessage> Assumi(string nome, string assunzione = "2030-01-09") =>
        _client.PostAsJsonAsync("/api/dipendenti", new { nome, ruolo = "Sala", oreSettimanali = 24, assunzione });

    private Task<HttpResponseMessage> Licenzia(int id, string ultimoGiorno) =>
        _client.PostAsJsonAsync($"/api/dipendenti/{id}/licenziamento", new { ultimoGiorno });

    [Fact]
    public async Task LaHomeServeLaPaginaHtml()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains("<title>Turni della squadra</title>", html);
    }

    [Fact]
    public async Task Dipendenti_RestituisceLaSquadra()
    {
        var squadra = await _client.GetFromJsonAsync<List<Dipendente>>("/api/dipendenti");

        Assert.Equal(5, squadra!.Count);
        Assert.Contains(squadra, d => d.Nome == "Elena Ricci" && d.OreSettimanali == 20);
    }

    [Fact]
    public async Task CreaTurno_201_PoiCompareNellaSettimana()
    {
        var risposta = await Crea(1, "2030-01-08T07:00:00", "2030-01-08T13:00:00");

        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
        var creato = await risposta.Content.ReadFromJsonAsync<RispostaCreazione>();
        Assert.Equal($"/api/turni/{creato!.Turno.Id}", risposta.Headers.Location!.ToString());

        // chiedo la settimana passando un giorno qualsiasi (giovedì): il server trova il lunedì
        var settimana = await _client.GetFromJsonAsync<Settimana>("/api/turni?settimana=2030-01-10");
        Assert.Equal(new DateOnly(2030, 1, 7), settimana!.Lunedi);
        Assert.Equal(6, Assert.Single(settimana.Turni).Ore);
        Assert.Equal(6, settimana.Ore.Single(o => o.DipendenteId == 1).Ore);
    }

    [Fact]
    public async Task TurnoControLeRegole_422ConIlMotivo()
    {
        await Crea(2, "2030-01-11T19:00:00", "2030-01-12T02:00:00");

        var risposta = await Crea(2, "2030-01-12T10:00:00", "2030-01-12T16:00:00");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, risposta.StatusCode);
        var errore = await risposta.Content.ReadFromJsonAsync<RispostaErrore>();
        Assert.Contains("riposo", Assert.Single(errore!.Errori));
    }

    [Fact]
    public async Task Straordinari_201ConAvviso()
    {
        for (int giorno = 7; giorno <= 9; giorno++)
        {
            await Crea(5, $"2030-01-{giorno:00}T08:00:00", $"2030-01-{giorno:00}T14:00:00");
        }

        var risposta = await Crea(5, "2030-01-10T08:00:00", "2030-01-10T14:00:00");

        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
        var creato = await risposta.Content.ReadFromJsonAsync<RispostaCreazione>();
        Assert.Contains("straordinari", Assert.Single(creato!.Avvisi));
    }

    [Fact]
    public async Task DipendenteInesistente_404()
    {
        var risposta = await Crea(99, "2030-01-08T07:00:00", "2030-01-08T13:00:00");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task Elimina_204_PoiNonEsistePiu()
    {
        var creato = await (await Crea(3, "2030-01-08T13:00:00", "2030-01-08T19:00:00"))
            .Content.ReadFromJsonAsync<RispostaCreazione>();

        var prima = await _client.DeleteAsync($"/api/turni/{creato!.Turno.Id}");
        var seconda = await _client.DeleteAsync($"/api/turni/{creato.Turno.Id}");

        Assert.Equal(HttpStatusCode.NoContent, prima.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, seconda.StatusCode);
    }

    [Fact]
    public async Task IDatiRestanoDopoIlRiavvio()
    {
        await Crea(4, "2030-01-08T11:00:00", "2030-01-08T19:00:00");

        // un secondo server che legge lo stesso file, come dopo un riavvio
        using var riavviata = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("Turni:FileDati", Path.Combine(_cartella, "turni.json")));
        var settimana = await riavviata.CreateClient().GetFromJsonAsync<Settimana>("/api/turni?settimana=2030-01-08");

        Assert.Single(settimana!.Turni);
    }

    [Fact]
    public async Task RichiesteContemporanee_SoloUnTurnoPassa()
    {
        // 10 richieste insieme per lo stesso orario: il lock deve farne passare una sola
        var richieste = Enumerable.Range(0, 10)
            .Select(_ => Crea(1, "2030-01-09T07:00:00", "2030-01-09T13:00:00"));

        var risposte = await Task.WhenAll(richieste);

        Assert.Equal(1, risposte.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, risposte.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
    }

    // ---------- Squadra: assumere, modificare, licenziare ----------

    [Fact]
    public async Task Assumi_201_CompareDallaSettimanaDellAssunzione()
    {
        var risposta = await Assumi("Marco Valli");

        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
        var assunto = await risposta.Content.ReadFromJsonAsync<Dipendente>();
        Assert.Equal(6, assunto!.Id); // dopo i 5 della squadra iniziale
        Assert.Equal("/api/dipendenti/6", risposta.Headers.Location!.ToString());
        Assert.Equal(new DateOnly(2030, 1, 9), assunto.Assunzione);

        var prima = await _client.GetFromJsonAsync<Settimana>("/api/turni?settimana=2029-12-31");
        var sua = await _client.GetFromJsonAsync<Settimana>("/api/turni?settimana=2030-01-07");
        Assert.DoesNotContain(prima!.Dipendenti, d => d.Id == 6);
        Assert.Contains(sua!.Dipendenti, d => d.Id == 6);
        Assert.Contains(sua.Ore, o => o.DipendenteId == 6 && o.Contratto == 24);
    }

    [Fact]
    public async Task Assumi_DatiNonValidi_422()
    {
        var risposta = await _client.PostAsJsonAsync("/api/dipendenti", new { nome = "", ruolo = "Sala", oreSettimanali = 60 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, risposta.StatusCode);
        var errore = await risposta.Content.ReadFromJsonAsync<RispostaErrore>();
        Assert.Equal(2, errore!.Errori.Count);
    }

    [Fact]
    public async Task Assumi_NomeGiaInSquadra_422()
    {
        var risposta = await Assumi("giulia neri");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, risposta.StatusCode);
    }

    [Fact]
    public async Task TurnoPrimaDellAssunzione_422()
    {
        await Assumi("Marco Valli", assunzione: "2030-01-09");

        var risposta = await Crea(6, "2030-01-08T13:00:00", "2030-01-08T19:00:00");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, risposta.StatusCode);
    }

    [Fact]
    public async Task Modifica_200_CambiaNomeRuoloEOre()
    {
        var risposta = await _client.PutAsJsonAsync("/api/dipendenti/5",
            new { nome = "Elena Ricci", ruolo = "Barista", oreSettimanali = 30 });

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var squadra = await _client.GetFromJsonAsync<List<Dipendente>>("/api/dipendenti");
        var elena = squadra!.Single(d => d.Id == 5);
        Assert.Equal(("Barista", 30), (elena.Ruolo, elena.OreSettimanali));
    }

    [Fact]
    public async Task Modifica_NonPuoPrendereIlNomeDiUnCollega_422()
    {
        var risposta = await _client.PutAsJsonAsync("/api/dipendenti/5",
            new { nome = "Luca Fabbri", ruolo = "Cassa", oreSettimanali = 20 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, risposta.StatusCode);
    }

    [Fact]
    public async Task Modifica_DipendenteInesistente_404()
    {
        var risposta = await _client.PutAsJsonAsync("/api/dipendenti/99",
            new { nome = "Nessuno", ruolo = "Sala", oreSettimanali = 20 });

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task Licenzia_CancellaSoloITurniDopoLUltimoGiorno()
    {
        await Crea(3, "2030-01-08T13:00:00", "2030-01-08T19:00:00"); // martedì: resta
        await Crea(3, "2030-01-10T13:00:00", "2030-01-10T19:00:00"); // giovedì: cancellato
        await Crea(3, "2030-01-15T13:00:00", "2030-01-15T19:00:00"); // settimana dopo: cancellato

        var risposta = await Licenzia(3, "2030-01-08");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var esito = await risposta.Content.ReadFromJsonAsync<RispostaLicenziamento>();
        Assert.Equal(2, esito!.TurniCancellati);
        Assert.Equal(new DateOnly(2030, 1, 8), esito.Dipendente.Cessazione);

        // la sua ultima settimana resta nello storico, con il turno di martedì
        var ultima = await _client.GetFromJsonAsync<Settimana>("/api/turni?settimana=2030-01-07");
        Assert.Contains(ultima!.Dipendenti, d => d.Id == 3);
        Assert.Equal(new DateTime(2030, 1, 8, 13, 0, 0), Assert.Single(ultima.Turni).Inizio);

        // dalla settimana dopo non compare più
        var dopo = await _client.GetFromJsonAsync<Settimana>("/api/turni?settimana=2030-01-14");
        Assert.DoesNotContain(dopo!.Dipendenti, d => d.Id == 3);
    }

    [Fact]
    public async Task DopoIlLicenziamento_NienteNuoviTurni_NeSecondoLicenziamento()
    {
        await Licenzia(4, "2030-01-08");

        var turno = await Crea(4, "2030-01-09T11:00:00", "2030-01-09T19:00:00");
        var ancora = await Licenzia(4, "2030-01-20");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, turno.StatusCode);
        Assert.Contains("non è più in squadra", Assert.Single((await turno.Content.ReadFromJsonAsync<RispostaErrore>())!.Errori));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ancora.StatusCode);
    }

    [Fact]
    public async Task Licenzia_DipendenteInesistente_404()
    {
        var risposta = await Licenzia(99, "2030-01-08");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task ChiHaLasciato_NonBloccaIlSuoNomePerUnNuovoAssunto()
    {
        await Licenzia(1, "2026-01-01"); // Giulia Neri non lavora più qui da tempo

        var risposta = await Assumi("Giulia Neri");

        Assert.Equal(HttpStatusCode.Created, risposta.StatusCode);
    }

    [Fact]
    public async Task DopoIlRiavvio_GliIdDeiDipendentiNonSiRipetono()
    {
        await Assumi("Marco Valli");

        using var riavviata = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("Turni:FileDati", Path.Combine(_cartella, "turni.json")));
        var risposta = await riavviata.CreateClient().PostAsJsonAsync("/api/dipendenti",
            new { nome = "Anna Conti", ruolo = "Cucina", oreSettimanali = 40 });

        var assunta = await risposta.Content.ReadFromJsonAsync<Dipendente>();
        Assert.Equal(7, assunta!.Id);
    }
}
