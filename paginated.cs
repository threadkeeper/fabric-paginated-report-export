using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Microsoft.Rest;

namespace Paginated;

public class PaginatedReportExporter
{
    private readonly string tenantId;
    private readonly string clientId;
    private readonly string clientSecret;
    private readonly Guid workspaceId;
    private readonly string[] scopes = new[] { "https://analysis.windows.net/powerbi/api/.default" };

    public PaginatedReportExporter(string tenantId, string clientId, string clientSecret, Guid workspaceId)
    {
        this.tenantId = tenantId;
        this.clientId = clientId;
        this.clientSecret = clientSecret;
        this.workspaceId = workspaceId;
    }

    private async Task<AuthenticationResult> GetAuthentication()
    {
        string authority = $"https://login.microsoftonline.com/{tenantId}";
        var app = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri(authority))
            .Build();
 
        var authResult = await app.AcquireTokenForClient(scopes).ExecuteAsync();

        if (authResult?.ClaimsPrincipal?.Identity?.IsAuthenticated == false)
        {
            throw new UnauthorizedAccessException("Client cannot be authenticated with PowerBI");
        }

        return authResult;
    }

    public async Task<string> GenerateReport(Guid reportId, IList<string>? parameters = null)
    {
        var authResult = await GetAuthentication();

        var tokenCredentials = new TokenCredentials(authResult?.AccessToken, "Bearer");
        using var pbiClient = new PowerBIClient(new Uri("https://api.powerbi.com/"), tokenCredentials);

        Console.WriteLine($"Exporting report {reportId}...");

        var exportRequest = new ExportReportRequest
        {
            Format = FileFormat.PDF,
        };

        // Paginated reports use PaginatedReportConfiguration with ParameterValues
        if (parameters != null && parameters.Count > 0)
        {
            var paramValues = new List<ParameterValue>();
            foreach (var p in parameters)
            {
                var parts = p.Split('=', 2);
                if (parts.Length == 2)
                    paramValues.Add(new ParameterValue(parts[0], parts[1]));
            }
            exportRequest.PaginatedReportConfiguration = new PaginatedReportExportConfiguration
            {
                ParameterValues = paramValues,
            };
        }

        // The report is generated asynchronously — poll for status
        var export = await pbiClient.Reports.ExportToFileInGroupAsync(workspaceId, reportId, exportRequest);

        Export exportStatus;
        do
        {
            await Task.Delay(5000);
            exportStatus = await pbiClient.Reports.GetExportToFileStatusInGroupAsync(workspaceId, reportId, export.Id);
            Console.WriteLine($"  Export status: {exportStatus.Status} ({exportStatus.PercentComplete}%)");
        } while (exportStatus.Status != ExportState.Succeeded && exportStatus.Status != ExportState.Failed);

        if (exportStatus.Status == ExportState.Failed)
        {
            // Fetch error details via raw REST call
            var errorDetails = "";
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResult?.AccessToken);
                var rawResp = await httpClient.GetStringAsync(
                    $"https://api.powerbi.com/v1.0/myorg/groups/{workspaceId}/reports/{reportId}/exports/{export.Id}");
                errorDetails = rawResp;
            }
            catch { }

            throw new InvalidOperationException(
                $"Export failed. ReportName: {exportStatus.ReportName}, " +
                $"Details: {errorDetails}");
        }

        // Download the exported file
        var fileStream = await pbiClient.Reports.GetFileOfExportToFileAsync(workspaceId, reportId, export.Id);
        string fileName = $"Report_{reportId}.pdf";
        using var file = File.Create(fileName);
        await fileStream.CopyToAsync(file);

        Console.WriteLine($"Report exported successfully to {fileName}");
        return fileName;
    }
}