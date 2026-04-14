using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.Rest;
using Paginated;

// ── Configuration ──────────────────────────────────────────────
// Set these via environment variables.
var tenantId   = Environment.GetEnvironmentVariable("PBI_TENANT_ID")      ?? throw new InvalidOperationException("Set PBI_TENANT_ID environment variable.");
var clientId   = Environment.GetEnvironmentVariable("PBI_CLIENT_ID")      ?? throw new InvalidOperationException("Set PBI_CLIENT_ID environment variable.");
var secret     = Environment.GetEnvironmentVariable("PBI_CLIENT_SECRET")  ?? throw new InvalidOperationException("Set PBI_CLIENT_SECRET environment variable.");
var workspace  = Guid.Parse(Environment.GetEnvironmentVariable("PBI_WORKSPACE_ID") ?? throw new InvalidOperationException("Set PBI_WORKSPACE_ID environment variable."));
var reportId   = Environment.GetEnvironmentVariable("PBI_REPORT_ID"); // optional — if not set, lists available reports

// ── Step 1: Verify authentication ──────────────────────────────
Console.WriteLine("=== Step 1: Authenticating as service principal ===");

var authority = $"https://login.microsoftonline.com/{tenantId}";
var app = ConfidentialClientApplicationBuilder.Create(clientId)
    .WithClientSecret(secret)
    .WithAuthority(new Uri(authority))
    .Build();

var authResult = await app.AcquireTokenForClient(new[] { "https://analysis.windows.net/powerbi/api/.default" }).ExecuteAsync();
Console.WriteLine($"Token acquired. Expires: {authResult.ExpiresOn}");

// ── Step 2: List workspace reports ─────────────────────────────
Console.WriteLine("\n=== Step 2: Listing reports in workspace ===");

var tokenCredentials = new TokenCredentials(authResult.AccessToken, "Bearer");
using var pbiClient = new PowerBIClient(new Uri("https://api.powerbi.com/"), tokenCredentials);

var reports = await pbiClient.Reports.GetReportsInGroupAsync(workspace);
if (reports.Value.Count == 0)
{
    Console.WriteLine("No reports found in workspace. Publish a report first, then re-run.");
    Console.WriteLine("\nTo publish a .pbix or paginated .rdl report:");
    Console.WriteLine("  1. Open Power BI Service → workspace-fabric-001");
    Console.WriteLine("  2. Upload → Browse → select your .rdl or .pbix file");
    Console.WriteLine("  3. Re-run this program with PBI_REPORT_ID set to the report's GUID");
    return;
}

Console.WriteLine($"{"ID",-40} {"Name",-40} {"Type"}");
Console.WriteLine(new string('-', 100));
foreach (var r in reports.Value)
{
    Console.WriteLine($"{r.Id,-40} {r.Name,-40} {r.ReportType}");
}

// ── Step 3: Export a report ────────────────────────────────────
Guid targetReportId;
if (!string.IsNullOrEmpty(reportId))
{
    targetReportId = Guid.Parse(reportId);
}
else
{
    // Default to the first report in the workspace
    targetReportId = reports.Value[0].Id;
    Console.WriteLine($"\nPBI_REPORT_ID not set — using first report: {targetReportId}");
}

// Collect parameters from command-line args (format: Name=Value)
var parameters = args.Length > 0 ? args.ToList() : null;
if (parameters != null)
{
    Console.WriteLine($"\nParameters: {string.Join(", ", parameters)}");
}

Console.WriteLine($"\n=== Step 3: Exporting report {targetReportId} to PDF ===");

var exporter = new PaginatedReportExporter(tenantId, clientId, secret, workspace);
var outputFile = await exporter.GenerateReport(targetReportId, parameters);

Console.WriteLine($"\n=== Done! Output: {Path.GetFullPath(outputFile)} ===");
