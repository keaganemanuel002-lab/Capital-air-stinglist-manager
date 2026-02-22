using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public sealed class TechnicianApiHostService : IDisposable
{
    private static readonly Lazy<TechnicianApiHostService> LazyInstance = new(() => new TechnicianApiHostService());
    private static readonly string[] RequiredVerificationMarkers =
    {
        "[Verification:Vehicle]",
        "[Verification:Registration]",
        "[Verification:VIN]",
        "[Verification:TrackingUnit]",
        "[Verification:SerialIccid]"
    };

    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private WebApplication? _app;
    private int _port;
    private string _apiKey = string.Empty;
    private readonly ConcurrentDictionary<string, TechnicianSession> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;

    public static TechnicianApiHostService Instance => LazyInstance.Value;
    public bool IsRunning => _app is not null;
    public int Port => _port;
    public string ApiKey => _apiKey;
    public string PortalPath => "/technician";
    public event Action<string>? TechnicianNotification;

    private TechnicianApiHostService()
    {
    }

    public async Task<(bool started, string message)> StartAsync(AppSettings settings)
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (_disposed)
                return (false, "Technician API host is disposed.");

            if (!settings.TechnicianApiEnabled)
                return (false, "Technician API is disabled in Settings.");

            if (_app is not null)
                return (true, $"Technician API already running on port {_port}.");

            var requestedPort = NormalizePort(settings.TechnicianApiPort);
            settings.TechnicianApiPort = requestedPort;

            if (string.IsNullOrWhiteSpace(settings.TechnicianApiKey))
            {
                settings.TechnicianApiKey = GenerateApiKey();
                new SettingsService().Save(settings);
            }
            _port = requestedPort;
            _apiKey = settings.TechnicianApiKey!.Trim();
            _sessions.Clear();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ApplicationName = typeof(TechnicianApiHostService).Assembly.FullName,
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

            var app = builder.Build();

            app.MapGet("/", () => Results.Redirect("/technician", permanent: false));
            app.MapGet("/technician", () => Results.Content(BuildPortalHtml(), "text/html; charset=utf-8"));

            app.Use(async (ctx, next) =>
            {
                var isTechApi = ctx.Request.Path.Value?.StartsWith("/api/tech", StringComparison.OrdinalIgnoreCase) == true;
                if (isTechApi)
                {
                    var isLoginRoute = string.Equals(ctx.Request.Path.Value, "/api/tech/auth/login", StringComparison.OrdinalIgnoreCase);
                    if (!isLoginRoute)
                    {
                        CleanupExpiredSessions();

                        var providedKey = ExtractApiKey(ctx.Request);
                        if (IsApiKeyValid(providedKey, _apiKey))
                        {
                            await next();
                            return;
                        }

                        var sessionToken = ExtractSessionToken(ctx.Request);
                        if (TryGetSession(sessionToken, out var session))
                        {
                            ctx.Items["TechSessionName"] = session.Name;
                            await next();
                            return;
                        }

                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsJsonAsync(new { ok = false, message = "Unauthorized. Please log in again." });
                        return;
                    }
                }

                await next();
            });

            MapApiEndpoints(app);

            await app.StartAsync();
            _app = app;

            return (true, $"Technician API running on port {_port}. Open /technician on the field device.");
        }
        catch (Exception ex)
        {
            _app = null;
            return (false, $"Failed to start Technician API: {ex.Message}");
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (_app is null)
                return;

            await _app.StopAsync();
            _app = null;
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = StopAsync();
        _startStopLock.Dispose();
    }

    public static IReadOnlyList<string> GetSuggestedPortalUrls(int port)
    {
        var normalizedPort = NormalizePort(port);
        var urls = new List<string> { $"http://localhost:{normalizedPort}/technician" };

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(addr.Address))
                    continue;

                var url = $"http://{addr.Address}:{normalizedPort}/technician";
                if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    urls.Add(url);
            }
        }

        return urls;
    }

    private void MapApiEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tech/auth/login", async (HttpRequest request) =>
        {
            TechnicianLoginRequest? payload;
            try
            {
                payload = await request.ReadFromJsonAsync<TechnicianLoginRequest>();
            }
            catch
            {
                payload = null;
            }

            if (payload is null)
                return Results.BadRequest(new { ok = false, message = "Invalid login payload." });

            var technicianName = string.IsNullOrWhiteSpace(payload.TechnicianName)
                ? string.Empty
                : payload.TechnicianName.Trim();
            if (string.IsNullOrWhiteSpace(technicianName))
                return Results.BadRequest(new { ok = false, message = "Technician name is required." });

            var authResult = new AuthService().Login(technicianName, payload.Pin);
            if (!authResult.Ok || authResult.User is null)
                return Results.Unauthorized();

            if (!AuthService.CanAccessTechnicianApp(authResult.User.Role))
            {
                return Results.Json(
                    new { ok = false, message = "Access denied. Only Admin or Tech users can sign in to technician app." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var effectiveName = authResult.User.Username;

            CleanupExpiredSessions();
            var token = GenerateSessionToken();
            var session = new TechnicianSession
            {
                Name = effectiveName,
                ExpiresUtc = DateTime.UtcNow.AddHours(12)
            };
            _sessions[token] = session;

            return Results.Ok(new
            {
                ok = true,
                token,
                technicianName = session.Name,
                role = authResult.User.Role,
                expiresUtc = session.ExpiresUtc
            });
        });

        app.MapGet("/api/tech/health", () => Results.Ok(new
        {
            ok = true,
            utc = DateTime.UtcNow
        }));

        app.MapGet("/api/tech/job-cards/open", () =>
        {
            using var db = new AppDbContext();
            var rows = db.JobCards
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Open)
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new TechnicianJobCardDto
                {
                    Id = j.Id,
                    QuoteId = j.QuoteId,
                    JobCardReference = JobCardReferenceFormatter.Format(j.Type, j.JobCardNumber),
                    QuoteReference = j.QuoteId == null ? "-" : null,
                    Type = j.Type.ToString(),
                    Status = j.Status.ToString(),
                    Company = j.Company,
                    Registration = j.Registration,
                    FleetNumber = j.FleetNumber,
                    Make = j.Make,
                    Model = j.Model,
                    Colour = j.Colour,
                    VinNumber = j.VinNumber,
                    GridLocation = j.GridLocation,
                    TrackingUnitMake = j.TrackingUnitMake,
                    Imei = j.Imei,
                    SerialNumber = j.SerialNumber,
                    Iccid = j.Iccid,
                    SimNumber = j.SimNumber,
                    ScheduledFor = j.ScheduledFor,
                    CreatedAt = j.CreatedAt,
                    CompletedAt = j.CompletedAt
                })
                .ToList();

            var quoteLookup = db.Quotes
                .AsNoTracking()
                .Select(q => new { q.Id, q.QuoteNumber })
                .ToDictionary(x => x.Id, x => QuoteReferenceFormatter.Format(x.QuoteNumber));

            foreach (var row in rows)
            {
                if (row.QuoteReference == "-" || !row.QuoteId.HasValue)
                    continue;

                if (quoteLookup.TryGetValue(row.QuoteId.Value, out var reference))
                    row.QuoteReference = reference;
                else
                    row.QuoteReference = "-";
            }

            return Results.Ok(rows);
        });

        app.MapGet("/api/tech/job-cards/completed", () =>
        {
            using var db = new AppDbContext();
            var rows = db.JobCards
                .AsNoTracking()
                .Where(j => j.Status == JobStatus.Completed)
                .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
                .Select(j => new TechnicianJobCardDto
                {
                    Id = j.Id,
                    QuoteId = j.QuoteId,
                    JobCardReference = JobCardReferenceFormatter.Format(j.Type, j.JobCardNumber),
                    QuoteReference = j.QuoteId == null ? "-" : null,
                    Type = j.Type.ToString(),
                    Status = j.Status.ToString(),
                    Company = j.Company,
                    Registration = j.Registration,
                    FleetNumber = j.FleetNumber,
                    Make = j.Make,
                    Model = j.Model,
                    Colour = j.Colour,
                    VinNumber = j.VinNumber,
                    GridLocation = j.GridLocation,
                    TrackingUnitMake = j.TrackingUnitMake,
                    Imei = j.Imei,
                    SerialNumber = j.SerialNumber,
                    Iccid = j.Iccid,
                    SimNumber = j.SimNumber,
                    ScheduledFor = j.ScheduledFor,
                    CreatedAt = j.CreatedAt,
                    CompletedAt = j.CompletedAt
                })
                .ToList();

            var quoteLookup = db.Quotes
                .AsNoTracking()
                .Select(q => new { q.Id, q.QuoteNumber })
                .ToDictionary(x => x.Id, x => QuoteReferenceFormatter.Format(x.QuoteNumber));

            foreach (var row in rows)
            {
                if (row.QuoteReference == "-" || !row.QuoteId.HasValue)
                    continue;

                if (quoteLookup.TryGetValue(row.QuoteId.Value, out var reference))
                    row.QuoteReference = reference;
                else
                    row.QuoteReference = "-";
            }

            return Results.Ok(rows);
        });

        app.MapGet("/api/tech/job-cards/{jobCardId:int}/photos", (int jobCardId) =>
        {
            using var db = new AppDbContext();
            var exists = db.JobCards.AsNoTracking().Any(j => j.Id == jobCardId);
            if (!exists)
                return Results.NotFound(new { ok = false, message = "Job card not found." });

            var photos = db.Attachments
                .AsNoTracking()
                .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                            && a.OwnerId == jobCardId
                            && a.Kind == AttachmentKind.JobPhoto)
                .OrderByDescending(a => a.AddedAt)
                .Select(a => new TechnicianPhotoDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    Notes = a.Notes,
                    AddedBy = a.AddedBy,
                    AddedAt = a.AddedAt
                })
                .ToList();

            return Results.Ok(photos);
        });

        app.MapGet("/api/tech/job-cards/{jobCardId:int}/verification-state", (int jobCardId) =>
        {
            using var db = new AppDbContext();
            var exists = db.JobCards.AsNoTracking().Any(j => j.Id == jobCardId);
            if (!exists)
                return Results.NotFound(new { ok = false, message = "Job card not found." });

            var uploadedVerificationTags = db.Attachments
                .AsNoTracking()
                .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                            && a.OwnerId == jobCardId
                            && a.Kind == AttachmentKind.JobPhoto
                            && a.Notes != null)
                .Select(a => ExtractVerificationTag(a.Notes))
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new
            {
                jobCardId,
                uploadedVerificationTags
            });
        });

        app.MapPost("/api/tech/job-cards/{jobCardId:int}/photos", async (HttpRequest request, HttpContext httpContext, int jobCardId) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { ok = false, message = "Content type must be multipart/form-data." });

            using var db = new AppDbContext();
            var job = db.JobCards.AsNoTracking().FirstOrDefault(j => j.Id == jobCardId);
            if (job is null)
                return Results.NotFound(new { ok = false, message = "Job card not found." });
            if (job.Status != JobStatus.Open)
                return Results.BadRequest(new { ok = false, message = "Photos can only be uploaded while the job card is Open." });

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length <= 0)
                return Results.BadRequest(new { ok = false, message = "Photo file is required." });

            var notes = form.TryGetValue("notes", out var notesValue) ? notesValue.ToString() : null;
            var gridLocation = form.TryGetValue("gridLocation", out var gridLocationValue)
                ? gridLocationValue.ToString()
                : null;
            var isFinalInBatch = form.TryGetValue("isFinalInBatch", out var finalInBatchValue)
                && bool.TryParse(finalInBatchValue.ToString(), out var parsedFinalInBatch)
                && parsedFinalInBatch;
            var technicianName = form.TryGetValue("technicianName", out var technicianValue)
                ? technicianValue.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(technicianName) && httpContext.Items.TryGetValue("TechSessionName", out var sessionNameObj))
                technicianName = sessionNameObj as string;

            var actor = string.IsNullOrWhiteSpace(technicianName) ? "FieldTech" : $"FieldTech:{technicianName.Trim()}";
            var technicianDisplay = string.IsNullOrWhiteSpace(technicianName) ? "Technician" : technicianName.Trim();
            var verificationMarker = ExtractVerificationMarker(notes);
            var verificationLabel = DescribeVerificationMarker(verificationMarker);
            var jobReference = JobCardReferenceFormatter.Format(job.Type, job.JobCardNumber);

            var duplicateByVerification = false;
            if (!string.IsNullOrWhiteSpace(verificationMarker))
            {
                var existingNotes = db.Attachments
                    .AsNoTracking()
                    .Where(a =>
                        a.OwnerType == AttachmentOwnerType.JobCard
                        && a.OwnerId == jobCardId
                        && a.Kind == AttachmentKind.JobPhoto
                        && a.Notes != null)
                    .Select(a => a.Notes!)
                    .ToList();

                duplicateByVerification = existingNotes.Any(note =>
                    note.Contains(verificationMarker, StringComparison.OrdinalIgnoreCase));
            }

            if (duplicateByVerification)
            {
                var completion = TryAutoCompleteJobCardAfterUpload(jobCardId, actor, gridLocation);
                NotifyTechnicianUpload(technicianDisplay, verificationLabel, jobReference, true, isFinalInBatch);

                var duplicateMessage = completion.message switch
                {
                    null or "" => $"Duplicate {verificationLabel} photo skipped.",
                    _ => $"Duplicate {verificationLabel} photo skipped. {completion.message}"
                };

                return Results.Ok(new
                {
                    ok = true,
                    duplicate = true,
                    skipped = true,
                    message = duplicateMessage,
                    completed = completion.completed
                });
            }

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            var safeReference = JobCardReferenceFormatter.Format(job.Type, job.JobCardNumber).Replace(" ", "-", StringComparison.Ordinal);
            var preferredName = $"{safeReference}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");

            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await file.CopyToAsync(stream);
                }

                var attachment = new AttachmentStorageService().AddAttachment(
                    actor,
                    AttachmentOwnerType.JobCard,
                    jobCardId,
                    AttachmentKind.JobPhoto,
                    tempPath,
                    notes,
                    preferredName);

                var completion = TryAutoCompleteJobCardAfterUpload(jobCardId, actor, gridLocation);
                NotifyTechnicianUpload(technicianDisplay, verificationLabel, jobReference, false, isFinalInBatch);
                var message = completion.message switch
                {
                    null or "" => "Photo uploaded to job card.",
                    _ => $"Photo uploaded to job card. {completion.message}"
                };

                return Results.Ok(new
                {
                    ok = true,
                    attachmentId = attachment.Id,
                    fileName = attachment.FileName,
                    addedAt = attachment.AddedAt,
                    message,
                    completed = completion.completed
                });
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }
            }
        });
    }

    private static (bool completed, string? message) TryAutoCompleteJobCardAfterUpload(int jobCardId, string actor, string? gridLocation)
    {
        using (var db = new AppDbContext())
        {
            var job = db.JobCards.FirstOrDefault(j => j.Id == jobCardId);
            if (job == null)
                return (false, null);

            var normalizedGridLocation = string.IsNullOrWhiteSpace(gridLocation) ? null : gridLocation.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedGridLocation)
                && !string.Equals(job.GridLocation, normalizedGridLocation, StringComparison.Ordinal))
            {
                job.GridLocation = normalizedGridLocation;
                db.SaveChanges();
            }

            if (job.Status != JobStatus.Open)
                return (job.Status == JobStatus.Completed, null);
        }

        if (!HasAllVerificationPhotos(jobCardId))
            return (false, "Waiting for all required verification photos.");

        var settings = new SettingsService().Load();
        var workflow = new WorkflowService();
        var result = workflow.CompleteJobCard(jobCardId, actor, settings.WialonApiToken);
        return result.ok
            ? (true, "Job card was moved to Completed.")
            : (false, $"Auto-complete failed: {result.message}");
    }

    private static bool HasAllVerificationPhotos(int jobCardId)
    {
        using var db = new AppDbContext();
        var notes = db.Attachments
            .AsNoTracking()
            .Where(a => a.OwnerType == AttachmentOwnerType.JobCard
                        && a.OwnerId == jobCardId
                        && a.Kind == AttachmentKind.JobPhoto
                        && a.Notes != null)
            .Select(a => a.Notes!)
            .ToList();

        if (notes.Count == 0)
            return false;

        foreach (var marker in RequiredVerificationMarkers)
        {
            var markerFound = notes.Any(note => note.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (!markerFound)
                return false;
        }

        return true;
    }

    private void NotifyTechnicianUpload(
        string technicianDisplay,
        string verificationLabel,
        string jobReference,
        bool duplicateSkipped,
        bool isFinalInBatch)
    {
        if (duplicateSkipped)
        {
            TechnicianNotification?.Invoke(
                $"Technician {technicianDisplay} re-submitted {verificationLabel} photo for {jobReference}. Duplicate skipped.");
        }
        else
        {
            TechnicianNotification?.Invoke(
                $"Technician {technicianDisplay} uploaded {verificationLabel} photo for {jobReference}.");
        }

        if (isFinalInBatch)
        {
            TechnicianNotification?.Invoke(
                $"Technician {technicianDisplay} pressed Save for {jobReference}.");
        }
    }

    private static string? ExtractVerificationMarker(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var markerStart = notes.IndexOf("[Verification:", StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0)
            return null;

        var markerEnd = notes.IndexOf(']', markerStart);
        if (markerEnd <= markerStart)
            return null;

        return notes.Substring(markerStart, markerEnd - markerStart + 1).Trim();
    }

    private static string? ExtractVerificationTag(string? notes)
    {
        var marker = ExtractVerificationMarker(notes);
        if (string.IsNullOrWhiteSpace(marker))
            return null;

        const string prefix = "[Verification:";
        if (!marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !marker.EndsWith("]", StringComparison.Ordinal))
        {
            return null;
        }

        var inner = marker.Substring(prefix.Length, marker.Length - prefix.Length - 1).Trim();
        return string.IsNullOrWhiteSpace(inner) ? null : inner;
    }

    private static string DescribeVerificationMarker(string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker))
            return "verification";

        return marker.Trim().ToLowerInvariant() switch
        {
            "[verification:vehicle]" => "Vehicle",
            "[verification:registration]" => "Registration",
            "[verification:vin]" => "VIN",
            "[verification:trackingunit]" => "Tracking Unit",
            "[verification:serialiccid]" => "Serial/ICCID",
            _ => "verification"
        };
    }

    private static string? ExtractApiKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Tech-Key", out var headerValue))
            return headerValue.ToString();

        if (request.Query.TryGetValue("apiKey", out var queryValue))
            return queryValue.ToString();

        return null;
    }

    private static bool IsApiKeyValid(string? provided, string expected)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expected))
            return false;

        var left = Encoding.UTF8.GetBytes(provided.Trim());
        var right = Encoding.UTF8.GetBytes(expected.Trim());
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? ExtractSessionToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var raw = authHeader.ToString().Trim();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var bearer = raw["Bearer ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(bearer))
                    return bearer;
            }
        }

        if (request.Headers.TryGetValue("X-Tech-Session", out var sessionHeader))
            return sessionHeader.ToString();

        if (request.Query.TryGetValue("session", out var sessionQuery))
            return sessionQuery.ToString();

        return null;
    }

    private bool TryGetSession(string? token, out TechnicianSession session)
    {
        session = default!;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!_sessions.TryGetValue(token.Trim(), out var found))
            return false;

        if (found.ExpiresUtc <= DateTime.UtcNow)
        {
            _sessions.TryRemove(token.Trim(), out _);
            return false;
        }

        session = found;
        return true;
    }

    private void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresUtc <= now)
                _sessions.TryRemove(pair.Key, out _);
        }
    }

    private static int NormalizePort(int value)
    {
        if (value is < 1024 or > 65535)
            return 5075;
        return value;
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);

        Span<byte> encoded = stackalloc byte[Base64.GetMaxEncodedToUtf8Length(bytes.Length)];
        Base64.EncodeToUtf8(bytes, encoded, out _, out var written);
        var key = Encoding.UTF8.GetString(encoded[..written]);
        return key.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateSessionToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        Span<byte> encoded = stackalloc byte[Base64.GetMaxEncodedToUtf8Length(bytes.Length)];
        Base64.EncodeToUtf8(bytes, encoded, out _, out var written);
        var token = Encoding.UTF8.GetString(encoded[..written]);
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildPortalHtml()
    {
        return """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Field Technician</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; background: #f3f6fb; margin: 0; color: #0f172a; }
    .wrap { max-width: 760px; margin: 0 auto; padding: 12px; }
    .card { background: #fff; border: 1px solid #dbe2ee; border-radius: 10px; padding: 12px; margin-bottom: 12px; }
    h1 { margin: 0 0 6px; font-size: 22px; }
    .muted { color: #64748b; font-size: 12px; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; }
    input, button, textarea { font: inherit; padding: 10px; border-radius: 8px; border: 1px solid #c6d0e0; }
    input, textarea { flex: 1; min-width: 130px; }
    button { background: #2563eb; color: #fff; border: none; font-weight: 600; }
    button.secondary { background: #64748b; }
    .job { border: 1px solid #dbe2ee; border-radius: 10px; padding: 10px; margin-top: 10px; background: #fbfdff; }
    .job-title { font-size: 16px; font-weight: 700; margin-bottom: 6px; }
    .meta { display: grid; grid-template-columns: 120px 1fr; gap: 2px 10px; font-size: 13px; }
    .ok { color: #166534; font-weight: 600; }
    .err { color: #b91c1c; font-weight: 600; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="card">
      <h1>Field Technician</h1>
      <div class="muted">View open job cards and upload photos from Android.</div>
      <div class="row" style="margin-top:10px">
        <input id="apiBase" placeholder="API Base URL" />
        <input id="apiKey" placeholder="Technician API Key" />
      </div>
      <div class="row" style="margin-top:8px">
        <input id="techName" placeholder="Technician name" />
        <button id="refreshBtn" type="button">Refresh Open Job Cards</button>
      </div>
      <div id="status" class="muted" style="margin-top:8px"></div>
    </div>

    <div id="jobs"></div>
  </div>

  <script>
    const apiBaseInput = document.getElementById('apiBase');
    const apiKeyInput = document.getElementById('apiKey');
    const techNameInput = document.getElementById('techName');
    const statusEl = document.getElementById('status');
    const jobsEl = document.getElementById('jobs');
    const refreshBtn = document.getElementById('refreshBtn');

    apiBaseInput.value = localStorage.getItem('tech_api_base') || window.location.origin;
    apiKeyInput.value = localStorage.getItem('tech_api_key') || '';
    techNameInput.value = localStorage.getItem('tech_name') || '';

    function setStatus(message, isError) {
      statusEl.textContent = message || '';
      statusEl.className = isError ? 'err' : 'muted';
    }

    function savePrefs() {
      localStorage.setItem('tech_api_base', apiBaseInput.value.trim());
      localStorage.setItem('tech_api_key', apiKeyInput.value.trim());
      localStorage.setItem('tech_name', techNameInput.value.trim());
    }

    async function loadJobs() {
      savePrefs();
      const apiBase = apiBaseInput.value.trim().replace(/\/+$/, '');
      const apiKey = apiKeyInput.value.trim();
      if (!apiBase || !apiKey) {
        setStatus('Enter API Base URL and API key.', true);
        return;
      }

      setStatus('Loading open job cards...', false);
      jobsEl.innerHTML = '';

      try {
        const resp = await fetch(`${apiBase}/api/tech/job-cards/open`, {
          headers: { 'X-Tech-Key': apiKey }
        });

        if (!resp.ok) {
          const text = await resp.text();
          throw new Error(`HTTP ${resp.status}: ${text}`);
        }

        const jobs = await resp.json();
        setStatus(`Loaded ${jobs.length} open job card(s).`, false);
        renderJobs(jobs, apiBase, apiKey);
      } catch (err) {
        setStatus(`Load failed: ${err.message}`, true);
      }
    }

    function renderJobs(jobs, apiBase, apiKey) {
      jobsEl.innerHTML = '';

      if (!Array.isArray(jobs) || jobs.length === 0) {
        jobsEl.innerHTML = '<div class="card">No open job cards found.</div>';
        return;
      }

      for (const job of jobs) {
        const card = document.createElement('div');
        card.className = 'card job';
        card.innerHTML = `
          <div class="job-title">${job.JobCardReference || '#'} - ${job.Company || ''}</div>
          <div class="meta">
            <div class="muted">Type</div><div>${job.Type || '-'}</div>
            <div class="muted">Quote</div><div>${job.QuoteReference || '-'}</div>
            <div class="muted">Reg</div><div>${job.Registration || '-'}</div>
            <div class="muted">Fleet</div><div>${job.FleetNumber || '-'}</div>
            <div class="muted">Vehicle</div><div>${(job.Make || '-') + ' ' + (job.Model || '-')}</div>
            <div class="muted">IMEI</div><div>${job.Imei || '-'}</div>
            <div class="muted">ICCID</div><div>${job.Iccid || '-'}</div>
          </div>
          <div class="row" style="margin-top:10px">
            <input type="file" accept="image/*" capture="environment" id="photo-${job.Id}">
            <textarea id="notes-${job.Id}" rows="2" placeholder="Photo notes (optional)"></textarea>
          </div>
          <div class="row" style="margin-top:8px">
            <button type="button" id="upload-${job.Id}">Upload Photo</button>
            <span id="msg-${job.Id}" class="muted"></span>
          </div>
        `;
        jobsEl.appendChild(card);

        const uploadBtn = card.querySelector(`#upload-${job.Id}`);
        uploadBtn.addEventListener('click', () => uploadPhoto(job.Id, apiBase, apiKey));
      }
    }

    async function uploadPhoto(jobCardId, apiBase, apiKey) {
      const fileInput = document.getElementById(`photo-${jobCardId}`);
      const notesInput = document.getElementById(`notes-${jobCardId}`);
      const msg = document.getElementById(`msg-${jobCardId}`);
      const technicianName = techNameInput.value.trim();

      if (!fileInput.files || fileInput.files.length === 0) {
        msg.textContent = 'Select a photo first.';
        msg.className = 'err';
        return;
      }

      const form = new FormData();
      form.append('photo', fileInput.files[0]);
      form.append('notes', notesInput.value || '');
      form.append('technicianName', technicianName || 'FieldTech');

      msg.textContent = 'Uploading...';
      msg.className = 'muted';

      try {
        const resp = await fetch(`${apiBase}/api/tech/job-cards/${jobCardId}/photos`, {
          method: 'POST',
          headers: { 'X-Tech-Key': apiKey },
          body: form
        });

        if (!resp.ok) {
          const text = await resp.text();
          throw new Error(`HTTP ${resp.status}: ${text}`);
        }

        const payload = await resp.json();
        msg.textContent = payload.message || 'Uploaded.';
        msg.className = 'ok';
        fileInput.value = '';
      } catch (err) {
        msg.textContent = `Upload failed: ${err.message}`;
        msg.className = 'err';
      }
    }

    refreshBtn.addEventListener('click', loadJobs);
    loadJobs();
  </script>
</body>
</html>
""";
    }

    private sealed class TechnicianLoginRequest
    {
        public string? TechnicianName { get; set; }
        public string? Pin { get; set; }
    }

    private readonly record struct TechnicianSession
    {
        public string Name { get; init; }
        public DateTime ExpiresUtc { get; init; }
    }

    private sealed class TechnicianJobCardDto
    {
        public int Id { get; set; }
        public int? QuoteId { get; set; }
        public string JobCardReference { get; set; } = string.Empty;
        public string? QuoteReference { get; set; } = "-";
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Registration { get; set; } = string.Empty;
        public string? FleetNumber { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? Colour { get; set; }
        public string? VinNumber { get; set; }
        public string? GridLocation { get; set; }
        public string? TrackingUnitMake { get; set; }
        public string? Imei { get; set; }
        public string? SerialNumber { get; set; }
        public string? Iccid { get; set; }
        public string? SimNumber { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    private sealed class TechnicianPhotoDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string AddedBy { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }
    }
}
