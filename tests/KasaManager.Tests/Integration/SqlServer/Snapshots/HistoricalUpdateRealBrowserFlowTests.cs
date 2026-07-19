using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Identity;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace KasaManager.Tests.Integration.SqlServer.Snapshots;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class HistoricalUpdateRealBrowserFlowTests
{
    private static readonly DateOnly BrowserDate = new(2070, 7, 14);
    private const string Username = "e2e-historical-user";
    private const string Password = "E2e-Historical-Only-42!";
    private readonly SqlServerIntegrationFixture _fixture;
    private readonly ITestOutputHelper _output;

    public HistoricalUpdateRealBrowserFlowTests(
        SqlServerIntegrationFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [SqlServerFact]
    public async Task HistoricalUpdate_RealBrowserFlow()
    {
        Guid v1Id;
        await using (var context = _fixture.CreateContext())
        {
            await CleanupSeedAsync(context);
            var user = new KasaUser
            {
                Username = Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                DisplayName = "E2E Historical User",
                Role = "Admin"
            };
            context.KasaUsers.Add(user);
            await context.SaveChangesAsync();

            var v1 = NewSnapshot(
                version: 1,
                isActive: false,
                user.Id,
                new KasaRaporData
                {
                    PayloadVersion = 1,
                    ImmutableAudit = Audit(-3000m, -29873.80m),
                    GuneAitEksikFazlaTahsilat = -3000m,
                    GuneAitEksikFazlaHarc = -29873.80m
                });
            var v2 = NewSnapshot(
                version: 2,
                isActive: true,
                user.Id,
                new KasaRaporData
                {
                    PayloadVersion = 2,
                    ImmutableAudit = Audit(0m, 0m),
                    ImmutableAuditDetails = JsonSerializer.SerializeToElement(EmptyDetails()),
                    GuneAitEksikFazlaTahsilat = 0m,
                    GuneAitEksikFazlaHarc = 0m
                });
            v1Id = v1.Id;
            context.CalculatedKasaSnapshots.AddRange(v1, v2);
            await context.SaveChangesAsync();
        }

        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var logs = new ConcurrentQueue<string>();
        using var app = StartTestApplication(port, logs);

        try
        {
            await WaitUntilReadyAsync(baseUrl, app, logs);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();

            await page.GotoAsync($"{baseUrl}/Account/Login");
            await page.Locator("input[name='username']").FillAsync(Username);
            await page.Locator("input[name='password']").FillAsync(Password);
            await page.Locator("button[type='submit']").ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            _output.WriteLine($"LOGIN URL={page.Url}");

            var loadResponse = await page.GotoAsync($"{baseUrl}/KasaPreview/LoadSnapshot?id={v1Id}");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var loadHtml = loadResponse is null ? string.Empty : await loadResponse.TextAsync();
            _output.WriteLine($"LOAD URL={page.Url} STATUS={loadResponse?.Status} TITLE={await page.TitleAsync()}");
            _output.WriteLine($"LOAD RAW loadedSnapshotId={loadHtml.Contains("id=\"loadedSnapshotId\"", StringComparison.Ordinal)} loadedReportBar={loadHtml.Contains("id=\"loadedReportBar\"", StringComparison.Ordinal)}");
            _output.WriteLine($"LOAD BODY={Regex.Replace((await page.Locator("body").InnerTextAsync()), "\\s+", " ")[..Math.Min(500, Regex.Replace((await page.Locator("body").InnerTextAsync()), "\\s+", " ").Length)]}");
            var sourceCount = await page.Locator("#loadedSnapshotId").CountAsync();
            var formCount = await page.Locator("#saveReportForm input[name='LoadedSnapshotId']").CountAsync();
            var diagnosticFormValue = formCount == 1
                ? await page.Locator("#saveReportForm input[name='LoadedSnapshotId']").InputValueAsync()
                : "<missing>";
            _output.WriteLine($"DOM sourceCount={sourceCount} formCount={formCount} formValue={diagnosticFormValue}");
            Assert.Equal(1, formCount);

            if (sourceCount == 0)
            {
                IRequest? blockedSaveRequest = null;
                page.Request += (_, request) =>
                {
                    if (request.Method == "POST"
                        && request.Url.Contains("/KasaPreview/SaveReport", StringComparison.OrdinalIgnoreCase))
                        blockedSaveRequest = request;
                };

                await page.Locator("#btnUpdateSnapshot").ClickAsync();
                await page.WaitForTimeoutAsync(500);
                _output.WriteLine($"POST SaveReport request={(blockedSaveRequest is null ? "<none>" : blockedSaveRequest.PostData)}");

                await using var blockedContext = _fixture.CreateContext();
                var blockedVersions = await blockedContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .Where(snapshot => snapshot.RaporTarihi == BrowserDate
                        && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                    .OrderBy(snapshot => snapshot.Version)
                    .ToArrayAsync();
                foreach (var blockedVersion in blockedVersions)
                {
                    var payload = JsonSerializer.Deserialize<KasaRaporData>(blockedVersion.KasaRaporDataJson!);
                    _output.WriteLine(
                        $"DB Id={blockedVersion.Id} Version={blockedVersion.Version} IsActive={blockedVersion.IsActive} " +
                        $"Tahsilat={payload?.ImmutableAudit?.GuneAitEksikFazlaTahsilat} " +
                        $"Harc={payload?.ImmutableAudit?.GuneAitEksikFazlaHarc}");
                }

                Assert.Null(blockedSaveRequest);
                Assert.Equal(2, blockedVersions.Length);
                return;
            }

            var sourceValue = await page.Locator("#loadedSnapshotId").InputValueAsync();
            var formValue = await page.Locator(
                "#saveReportForm input[name='LoadedSnapshotId']").InputValueAsync();
            Assert.Equal(v1Id.ToString(), sourceValue, ignoreCase: true);
            Assert.Equal(v1Id.ToString(), formValue, ignoreCase: true);

            var displayedAuditElements = await page.Locator("input, span, div, td")
                .EvaluateAllAsync<string[]>("""
                    els => els
                      .map(el => ({
                        tag: el.tagName,
                        id: el.id || '',
                        name: el.getAttribute('name') || '',
                        value: el.value || '',
                        text: (el.textContent || '').trim()
                      }))
                      .filter(x => /-?3[.\s]?000,00|-?29[.\s]?873,80|-3000(?:\.0+)?|-29873\.8/.test(x.value + ' ' + x.text))
                      .slice(0, 30)
                      .map(x => JSON.stringify(x))
                    """);
            foreach (var element in displayedAuditElements)
                _output.WriteLine($"AUDIT-DOM: {element}");

            IRequest? saveRequest = null;
            page.Request += (_, request) =>
            {
                if (request.Method == "POST"
                    && request.Url.Contains("/KasaPreview/SaveReport", StringComparison.OrdinalIgnoreCase))
                {
                    saveRequest = request;
                }
            };

            var response = await page.RunAndWaitForResponseAsync(
                async () => await page.Locator("#btnUpdateSnapshot").ClickAsync(),
                candidate => candidate.Url.Contains(
                    "/KasaPreview/SaveReport", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(saveRequest);
            var rawPayload = saveRequest.PostData ?? string.Empty;
            var postedLoadedId = MultipartValue(rawPayload, "LoadedSnapshotId");
            var postedTahsilat = MultipartValue(rawPayload, "RptEfGuneT");
            var postedHarc = MultipartValue(rawPayload, "RptEfGuneH");
            var postedSourceTahsilat = MultipartValue(rawPayload, "GuneAitEksikFazlaTahsilat");
            var postedSourceHarc = MultipartValue(rawPayload, "GuneAitEksikFazlaHarc");
            var responseJson = await response.TextAsync();

            _output.WriteLine($"POST LoadedSnapshotId={postedLoadedId}");
            _output.WriteLine($"POST RptEfGuneT={postedTahsilat}");
            _output.WriteLine($"POST RptEfGuneH={postedHarc}");
            _output.WriteLine($"POST GuneAitEksikFazlaTahsilat={postedSourceTahsilat ?? "<absent>"}");
            _output.WriteLine($"POST GuneAitEksikFazlaHarc={postedSourceHarc ?? "<absent>"}");
            _output.WriteLine($"RESPONSE {responseJson}");

            Assert.Equal(v1Id.ToString(), postedLoadedId, ignoreCase: true);
            Assert.Equal(-3000m, decimal.Parse(postedTahsilat!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(-29873.80m, decimal.Parse(postedHarc!, System.Globalization.CultureInfo.InvariantCulture));

            using var responseDocument = JsonDocument.Parse(responseJson);
            Assert.True(responseDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(responseDocument.RootElement.GetProperty("createdNewVersion").GetBoolean());
            Assert.Equal(3, responseDocument.RootElement.GetProperty("version").GetInt32());

            await using var inspectContext = _fixture.CreateContext();
            var versions = await inspectContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.RaporTarihi == BrowserDate
                    && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                .OrderBy(snapshot => snapshot.Version)
                .ToArrayAsync();
            Assert.Equal(3, versions.Length);
            Assert.False(versions[0].IsActive);
            Assert.False(versions[1].IsActive);
            Assert.True(versions[2].IsActive);
            foreach (var versionRow in versions)
            {
                var rowPayload = JsonSerializer.Deserialize<KasaRaporData>(versionRow.KasaRaporDataJson!);
                _output.WriteLine(
                    $"DB Id={versionRow.Id} Version={versionRow.Version} IsActive={versionRow.IsActive} " +
                    $"Tahsilat={rowPayload?.ImmutableAudit?.GuneAitEksikFazlaTahsilat} " +
                    $"Harc={rowPayload?.ImmutableAudit?.GuneAitEksikFazlaHarc}");
            }
            var v3Payload = JsonSerializer.Deserialize<KasaRaporData>(
                versions[2].KasaRaporDataJson!);
            Assert.Equal(-3000m, v3Payload!.ImmutableAudit!.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, v3Payload.ImmutableAudit.GuneAitEksikFazlaHarc);

            foreach (var line in logs.Where(line => line.Contains("SAVEREPORT-DIAG:")))
                _output.WriteLine(line);
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync();
            }

            await using var cleanupContext = _fixture.CreateContext();
            await CleanupSeedAsync(cleanupContext);
        }
    }

    private Process StartTestApplication(int port, ConcurrentQueue<string> logs)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var webDirectory = Path.Combine(root, "src", "KasaManager.Web");
        var webDll = Path.Combine(webDirectory, "bin", "Release", "net8.0", "KasaManager.Web.dll");
        var connection = TestDatabaseGuard.BuildTestDatabaseConnectionString(
            _fixture.GetValidatedBase(), _fixture.DatabaseName).ConnectionString;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{webDll}\"")
        {
            WorkingDirectory = webDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["ConnectionStrings__SqlConnection"] = connection;
        startInfo.Environment["LegacyDatabase__Enabled"] = "false";
        startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        startInfo.Environment["Logging__LogLevel__KasaManager.Web.Controllers"] = "Warning";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
        Assert.True(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilReadyAsync(
        string baseUrl,
        Process app,
        ConcurrentQueue<string> logs)
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (app.HasExited)
                throw new InvalidOperationException(
                    $"E2E web host exited early ({app.ExitCode}): {string.Join(Environment.NewLine, logs)}");

            try
            {
                using var response = await client.GetAsync($"{baseUrl}/Account/Login");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // Kestrel is still starting.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("E2E web host did not become ready.");
    }

    private static CalculatedKasaSnapshot NewSnapshot(
        int version,
        bool isActive,
        int userId,
        KasaRaporData payload) => new()
    {
        Id = Guid.NewGuid(),
        RaporTarihi = BrowserDate,
        KasaTuru = KasaRaporTuru.Sabah,
        FormulaSetName = "E2E",
        CalculatedAtUtc = DateTime.UtcNow.AddMinutes(-10 + version),
        CalculatedBy = Username,
        CalculatedByUserId = userId,
        Version = version,
        IsActive = isActive,
        InputsJson = "{}",
        OutputsJson = "{\"genel_kasa\":0,\"gune_ait_eksik_fazla_tahsilat\":0,\"gune_ait_eksik_fazla_harc\":0}",
        Name = $"E2E Historical v{version}",
        KasaRaporDataJson = JsonSerializer.Serialize(payload)
    };

    private static KasaImmutableAuditData Audit(decimal tahsilat, decimal harc) => new()
    {
        GuneAitEksikFazlaTahsilat = tahsilat,
        GuneAitEksikFazlaHarc = harc
    };

    private static HesapKontrolImmutableAuditDetails EmptyDetails() => new(
        Array.Empty<HesapKontrolImmutableAuditRecord>(),
        new HesapKontrolImmutableAuditGroups(
            Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
            Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>()));

    private static string? MultipartValue(string body, string name)
    {
        var match = Regex.Match(
            body,
            $"name=\"{Regex.Escape(name)}\"\\r?\\n\\r?\\n(?<value>.*?)\\r?\\n",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task CleanupSeedAsync(KasaManager.Infrastructure.Persistence.KasaManagerDbContext context)
    {
        context.CalculatedKasaSnapshots.RemoveRange(
            context.CalculatedKasaSnapshots.Where(snapshot => snapshot.RaporTarihi == BrowserDate));
        context.KasaUsers.RemoveRange(
            context.KasaUsers.Where(user => user.Username == Username));
        await context.SaveChangesAsync();
    }
}
