using System.Text;
using AutoKorsak.Api;
using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o =>
{
    foreach (var c in Storage.Json.Converters) o.SerializerOptions.Converters.Add(c);
});
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = LegacyXmlImporter.MaxBytes + 64 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = LegacyXmlImporter.MaxBytes + 128 * 1024);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new Storage(builder.Configuration["Storage:DatabasePath"] ?? "data/autokorsak.db"));
builder.Services.AddSingleton<TournamentService>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();

var app = builder.Build();

app.UseMiddleware<ApiErrorMiddleware>();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ---------- tournaments ----------
var tournaments = api.MapGroup("/tournaments");
tournaments.MapGet("/", (TournamentService s) => s.List());
tournaments.MapPost("/", (TournamentService s, SettingsInput input) => s.Create(input));
tournaments.MapPost("/import", async (TournamentService s, HttpRequest request) =>
{
    if (!request.HasFormContentType) throw new DomainException("xml.fileRequired");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? throw new DomainException("xml.fileRequired");
    if (file.Length > LegacyXmlImporter.MaxBytes)
        throw new DomainException("xml.tooLarge", new Dictionary<string, object?> { ["maxMb"] = LegacyXmlImporter.MaxBytes / 1024 / 1024 });
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    return s.Import(ms.ToArray());
}).DisableAntiforgery();
tournaments.MapGet("/{id:guid}", (TournamentService s, Guid id) => s.Get(id));
tournaments.MapDelete("/{id:guid}", (TournamentService s, Guid id) => { s.Delete(id); return Results.NoContent(); });
tournaments.MapPut("/{id:guid}/settings", (TournamentService s, Guid id, SettingsInput input) =>
    s.Mutate(id, t => { TournamentEditor.UpdateSettings(t, input); return null; }));
tournaments.MapPut("/{id:guid}/round", (TournamentService s, Guid id, RoundStateInput input) =>
    s.Mutate(id, t => { TournamentEditor.SetCurrentRound(t, input.CurrentRound, input.IncludeCurrentRound); return null; }));
tournaments.MapGet("/{id:guid}/standings", (TournamentService s, Guid id) => StandingsCalculator.Calculate(s.Load(id)));
tournaments.MapGet("/{id:guid}/standings/preparation", (TournamentService s, Guid id) => StandingsCalculator.CalculatePreparation(s.Load(id)));

// ---------- pairing stages and generation (spec 0001) ----------
tournaments.MapPost("/{id:guid}/stages", (TournamentService s, Guid id, StageChangeInput input) =>
    s.Mutate(id, t => PairingSchedule.ChangeSystem(t, input.StartRound, input.System, input.ConfirmDeleteUnplayed)));
tournaments.MapPost("/{id:guid}/rounds/{round:int}/generate", (TournamentService s, IRandomSource random, Guid id, int round, GenerateInput input) =>
    s.Mutate(id, t => PairingSchedule.GenerateRoundRobin(t, round, input.AllRounds, input.Source, input.ConfirmReplace, random)));
tournaments.MapPost("/{id:guid}/rounds/{round:int}/clear-round-robin", (TournamentService s, Guid id, int round, ClearInput input) =>
    s.Mutate(id, t => PairingSchedule.ClearRoundRobin(t, round, input.Confirm)));
tournaments.MapPut("/{id:guid}/starting-numbers", (TournamentService s, Guid id, StartingNumbersInput input) =>
    s.Mutate(id, t => new { saved = TournamentEditor.SetStartingNumbers(t, input.Entries ?? []) }));

// ---------- participants ----------
tournaments.MapPost("/{id:guid}/participants", (TournamentService s, Guid id, ParticipantInput input) =>
{
    var entry = input.CatalogEntryId is { } catalogId
        ? s.FindCatalogEntry(catalogId) ?? throw new DomainException("catalog.entryNotFound")
        : null;
    return s.Mutate(id, t =>
    {
        var p = TournamentEditor.AddParticipant(t, input);
        if (entry is not null) p.EgdPin = entry.EgdPin;
        return new { participantId = p.Id };
    });
});
tournaments.MapPut("/{id:guid}/participants/{pid:int}", (TournamentService s, Guid id, int pid, ParticipantInput input) =>
    s.Mutate(id, t => { TournamentEditor.UpdateParticipant(t, pid, input); return null; }));
tournaments.MapDelete("/{id:guid}/participants/{pid:int}", (TournamentService s, Guid id, int pid) =>
    s.Mutate(id, t => { TournamentEditor.DeleteParticipant(t, pid); return null; }));
tournaments.MapPost("/{id:guid}/participants/finalize-preliminary", (TournamentService s, Guid id) =>
    s.Mutate(id, t => TournamentEditor.FinalizeAllPreliminary(t)));
tournaments.MapPost("/{id:guid}/participants/delete-preliminary", (TournamentService s, Guid id) =>
    s.Mutate(id, t => TournamentEditor.DeleteAllPreliminary(t)));

// ---------- pairings ----------
tournaments.MapPost("/{id:guid}/rounds/{round:int}/pairings", (TournamentService s, Guid id, int round, PairingInput input) =>
    s.Mutate(id, t => new { pairingId = TournamentEditor.AddPairing(t, round, input).Id }));
tournaments.MapPut("/{id:guid}/pairings/{pid:int}", (TournamentService s, Guid id, int pid, PairingInput input, bool? confirmDiscardResult) =>
    s.Mutate(id, t => { TournamentEditor.UpdatePairing(t, pid, input, confirmDiscardResult == true); return null; }));
tournaments.MapDelete("/{id:guid}/pairings/{pid:int}", (TournamentService s, Guid id, int pid, bool? confirmDiscardResult) =>
    s.Mutate(id, t => { TournamentEditor.DeletePairing(t, pid, confirmDiscardResult == true); return null; }));

// ---------- legacy XML export ----------
tournaments.MapGet("/{id:guid}/export-report", (TournamentService s, Guid id) => ExportAnalyzer.Analyze(s.Load(id)));
tournaments.MapGet("/{id:guid}/export.xml", (TournamentService s, Guid id) =>
{
    var t = s.Load(id);
    var bytes = LegacyXmlExporter.ToBytes(LegacyXmlExporter.Export(t));
    return Results.File(bytes, "application/xml; charset=utf-8", SafeFileName(t.Name) + ".xml");
});

// ---------- shared player catalog ----------
api.MapGet("/catalog", (TournamentService s, string? search, int? limit) => s.SearchCatalog(search, limit ?? 20));
tournaments.MapGet("/{id:guid}/catalog-upsert", (TournamentService s, Guid id) => s.PlanUpsert(id));
tournaments.MapPost("/{id:guid}/catalog-upsert", (TournamentService s, Guid id, UpsertRequest request) =>
    s.ApplyUpsert(id, request.Decisions ?? []));

api.MapFallback(() => Results.NotFound(new ApiError("notFound", new Dictionary<string, object?>())));
app.MapFallbackToFile("index.html");

app.Run();

static string SafeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
    var sb = new StringBuilder();
    foreach (var ch in name.Trim()) sb.Append(invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch);
    return sb.Length == 0 ? "tournament" : sb.ToString();
}

namespace AutoKorsak.Api
{
    public sealed record RoundStateInput(int CurrentRound, bool IncludeCurrentRound);

    public sealed record StageChangeInput(int StartRound, TournamentSystem? System, bool ConfirmDeleteUnplayed);

    /// <summary>
    /// Spec 0002: explicit starting-number source. A "roundCount" sent by a 0002 client is ignored: the server derives
    /// the count (spec 0003 §2.2).
    /// </summary>
    public sealed record GenerateInput(bool AllRounds, StartingNumberSource Source, bool ConfirmReplace);

    public sealed record ClearInput(bool Confirm);

    public sealed record StartingNumbersInput(List<StartingNumberEntry>? Entries);

    public sealed record UpsertRequest(List<UpsertDecision>? Decisions);

    public sealed record ApiError(string Code, IReadOnlyDictionary<string, object?> Args);

    /// <summary>Maps domain errors to JSON <c>{ code, args }</c> responses that the UI localizes.</summary>
    public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (DomainException ex)
            {
                var status = ex switch
                {
                    NotFoundException => StatusCodes.Status404NotFound,
                    { IsConflict: true } => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };
                await Write(context, status, new ApiError(ex.Code, ex.Args));
            }
            catch (BadHttpRequestException ex)
            {
                await Write(context, ex.StatusCode, new ApiError(
                    ex.StatusCode == StatusCodes.Status413PayloadTooLarge ? "xml.tooLarge" : "request.invalid",
                    new Dictionary<string, object?> { ["maxMb"] = LegacyXmlImporter.MaxBytes / 1024 / 1024 }));
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                logger.LogError(ex, "Unhandled error");
                await Write(context, StatusCodes.Status500InternalServerError, new ApiError("server.error", new Dictionary<string, object?>()));
            }
        }

        private static Task Write(HttpContext context, int status, ApiError error)
        {
            context.Response.Clear();
            context.Response.StatusCode = status;
            return context.Response.WriteAsJsonAsync(error, Storage.Json);
        }
    }

    /// <summary>
    /// Requires a custom header on state-changing API requests. Browsers cannot add it cross-site without a
    /// CORS preflight (which this server never approves), so other websites cannot drive the local instance.
    /// </summary>
    public sealed class CsrfHeaderMiddleware(RequestDelegate next)
    {
        public const string HeaderName = "X-AutoKorsak-Client";

        public Task InvokeAsync(HttpContext context)
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
                && !request.Headers.ContainsKey(HeaderName))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new ApiError("request.missingClientHeader", new Dictionary<string, object?>()), Storage.Json);
            }
            return next(context);
        }
    }
}

public partial class Program;
