using Turni.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(servizi =>
{
    var config = servizi.GetRequiredService<IConfiguration>();
    var percorso = Path.Combine(builder.Environment.ContentRootPath, config["Turni:FileDati"] ?? "data/turni.json");
    return new ArchivioTurni(percorso, config.GetValue("Turni:TurniDiEsempio", true));
});

var app = builder.Build();

app.UseDefaultFiles(); // "/" -> wwwroot/index.html
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/dipendenti", (ArchivioTurni archivio) => archivio.Dipendenti());

// Assunzione: senza data, il dipendente è in squadra da oggi
api.MapPost("/dipendenti", (DatiDipendente dati, ArchivioTurni archivio) =>
{
    var (dipendente, esito) = archivio.Assumi(dati, DateOnly.FromDateTime(DateTime.Today));
    return dipendente is null
        ? Results.UnprocessableEntity(new { errori = esito.Errori })
        : Results.Created($"/api/dipendenti/{dipendente.Id}", dipendente);
});

api.MapPut("/dipendenti/{id:int}", (int id, DatiDipendente dati, ArchivioTurni archivio) =>
{
    var (dipendente, esito) = archivio.Modifica(id, dati, DateOnly.FromDateTime(DateTime.Today));
    if (esito is null)
    {
        return Results.NotFound(new { errori = new[] { $"Il dipendente {id} non esiste." } });
    }
    return dipendente is null
        ? Results.UnprocessableEntity(new { errori = esito.Errori })
        : Results.Ok(dipendente);
});

// Licenziamento: non un DELETE, perché il dipendente resta nello storico delle settimane passate
api.MapPost("/dipendenti/{id:int}/licenziamento", (int id, Licenziamento richiesta, ArchivioTurni archivio) =>
{
    var ultimoGiorno = richiesta.UltimoGiorno ?? DateOnly.FromDateTime(DateTime.Today);
    var (dipendente, turniCancellati, esito) = archivio.Licenzia(id, ultimoGiorno);
    if (esito is null)
    {
        return Results.NotFound(new { errori = new[] { $"Il dipendente {id} non esiste." } });
    }
    return dipendente is null
        ? Results.UnprocessableEntity(new { errori = esito.Errori })
        : Results.Ok(new { dipendente, turniCancellati });
});

// GET /api/turni?settimana=2026-11-04 -> i turni della settimana che contiene quel giorno
api.MapGet("/turni", (DateOnly? settimana, ArchivioTurni archivio) =>
    archivio.Settimana(settimana ?? DateOnly.FromDateTime(DateTime.Today)));

api.MapPost("/turni", (NuovoTurno nuovo, ArchivioTurni archivio) =>
{
    var dipendente = archivio.TrovaDipendente(nuovo.DipendenteId);
    if (dipendente is null)
    {
        return Results.NotFound(new { errori = new[] { $"Il dipendente {nuovo.DipendenteId} non esiste." } });
    }

    var (turno, esito) = archivio.Aggiungi(nuovo, dipendente);
    return turno is null
        ? Results.UnprocessableEntity(new { errori = esito.Errori })  // 422: dati leggibili ma contro le regole
        : Results.Created($"/api/turni/{turno.Id}", new { turno, avvisi = esito.Avvisi });
});

api.MapDelete("/turni/{id:int}", (int id, ArchivioTurni archivio) =>
    archivio.Elimina(id) ? Results.NoContent() : Results.NotFound());

app.Run();

// Serve ai test di integrazione (WebApplicationFactory<Program>)
public partial class Program { }
