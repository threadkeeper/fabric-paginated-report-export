# Power BI Paginated Report Export via Fabric Workspace

Export Power BI paginated reports to PDF using the Power BI REST API, authenticated via an Entra ID app registration with client credentials.

## Architecture

```
App (C# / MSAL) ──► Entra ID token endpoint ──► Power BI REST API ──► Fabric Workspace
                     (client_credentials)        ExportToFileInGroup     (dedicated capacity)
```

## Prerequisites

| Requirement | Detail |
|---|---|
| **Fabric capacity** | The target workspace must be on **dedicated capacity** (Fabric F-SKU or Premium P-SKU). `ExportToFile` is not supported on shared/Pro capacity. |
| **Entra ID app registration** | Single-tenant app with a client secret and the Power BI API permissions listed below |
| **Workspace membership** | The service principal must be added as a **Contributor** (minimum) of the target workspace |
| **Power BI tenant setting** | "Allow service principals to use Power BI APIs" must be enabled in the Power BI Admin Portal |
| **.NET 8 SDK** | Required to build and run the application |

---

## Step 1: Create the App Registration

1. In the Azure Portal, go to **Entra ID → App registrations → New registration**
2. Set **Supported account types** to "Accounts in this organizational directory only" (single tenant)
3. Note the **Application (client) ID** and **Directory (tenant) ID**

## Step 2: Configure API Permissions

Add the following **application** permission on the **Power BI Service** API:

| Permission | Type | Purpose |
|---|---|---|
| `Tenant.ReadWrite.All` | Application (Role) | Read/write all Power BI content in tenant (covers listing workspaces, reports, and triggering exports) |

Then grant **admin consent** — either via the portal or CLI:
```bash
az ad app permission admin-consent --id <appId>
```

### Is `User.Read` (Microsoft Graph) required?

**No.** The `User.Read` delegated permission on Microsoft Graph is added by default when creating an app registration in the Azure Portal. It is **not used** by this application since we authenticate via client credentials (no user context). You can safely remove it. It has no effect on the export flow.

## Step 3: Create a Client Secret

In the portal: **App registration → Certificates & secrets → New client secret**

Or via CLI:
```bash
az ad app credential reset --id <appId> --display-name "paginated-export" --years 1
```

> **Tip:** After creating or rotating a secret, wait **30–60 seconds** before using it. Entra ID credential replication across regions is not instantaneous, and you may see `AADSTS7000215: Invalid client secret provided` if you authenticate too quickly.

## Step 4: Add the Service Principal to the Workspace

The service principal must be a workspace member to access reports and trigger exports.

**Option A — Via the Power BI Portal:**
Workspace → Settings → Access → Add the app by name → set role to **Contributor** (minimum required).

**Option B — Via the standard REST API:**
```http
POST https://api.powerbi.com/v1.0/myorg/groups/{groupId}/users
Content-Type: application/json

{
  "identifier": "<service-principal-object-id>",
  "groupUserAccessRight": "Contributor",
  "principalType": "App"
}
```

**Option C — Via the Admin API (if Option B returns 403):**
```http
POST https://api.powerbi.com/v1.0/myorg/admin/groups/{groupId}/users
```

The standard API (Option B) may return **403 Forbidden** when the calling user is not already a direct member of the workspace via the non-admin API path, or when workspace-level inbound networking policies are in effect. In our testing, we encountered this on a Fabric workspace with private networking enabled and had to use the Admin API instead.

> **Verified:** Contributor access is sufficient for listing reports and exporting to PDF. Admin is not required.

This behaviour is consistent with the [Power BI REST API documentation for Groups - Add Group User](https://learn.microsoft.com/en-us/rest/api/power-bi/groups/add-group-user), which requires the caller to have workspace access, versus the [Admin - Add Group User](https://learn.microsoft.com/en-us/rest/api/power-bi/admin/groups-add-user) endpoint which operates at the tenant admin level and bypasses workspace-level access checks.

> **Note:** The `identifier` field must be the **service principal object ID** (not the app/client ID):
> ```bash
> az ad sp show --id <appId> --query id -o tsv
> ```

## Step 5: Run the Application

```bash
# Set required environment variable
export PBI_CLIENT_SECRET="<your-secret>"

# Optional overrides (defaults are in program.cs)
export PBI_TENANT_ID="<tenant-id>"
export PBI_CLIENT_ID="<client-id>"
export PBI_WORKSPACE_ID="<workspace-id>"
export PBI_REPORT_ID="<report-id>"

# Run with report parameters (Name=Value format)
dotnet run -- "Company=Contoso Suites"
```

If `PBI_REPORT_ID` is not set, the program lists all reports in the workspace and exports the first one.

---

## How the Export Works

1. **Authenticate** — acquire a token via MSAL client credentials flow with scope `https://analysis.windows.net/powerbi/api/.default`
2. **Start export** — `POST /groups/{workspaceId}/reports/{reportId}/ExportTo` with format (PDF) and optional paginated report parameters
3. **Poll for status** — `GET /groups/{workspaceId}/reports/{reportId}/exports/{exportId}` every 5 seconds until `status == Succeeded`
4. **Download** — `GET /groups/{workspaceId}/reports/{reportId}/exports/{exportId}/file`

### Paginated Report Parameters

Paginated reports may **require** parameters to render. If a required parameter is missing, the export will silently fail (status goes to `Failed` with no error message). Pass parameters as `Name=Value` arguments:

```bash
dotnet run -- "Company=Contoso Suites" "Year=2026"
```

---

## Networking Considerations

### Required Outbound Endpoints

The application must reach these endpoints over HTTPS (port 443):

| Endpoint | Purpose |
|---|---|
| `login.microsoftonline.com` | Entra ID token acquisition (OAuth 2.0 client credentials) |
| `api.powerbi.com` | Power BI REST API (report export, status polling, file download) |
| `analysis.windows.net` | Power BI service resource URI (used in token scope) |
| `*.pbidedicated.windows.net` | Fabric/Premium dedicated capacity backend |

For firewall rules, refer to the [Azure IP Ranges and Service Tags](https://www.microsoft.com/en-us/download/details.aspx?id=56519) JSON file under the `PowerBI` service tag.

### Fabric Private Networking

If the Fabric capacity has **workspace-level inbound network rules** enabled (`WorkspaceBlockInboundAccess` tenant setting), API calls from outside the allowed network will be blocked. Symptoms:

- The Power BI Import API returns `RequestedFileIsEncryptedOrCorrupted` (misleading)
- The Fabric REST API returns `RequestDeniedByInboundPolicy`

To resolve this, either:
1. Add the client's IP/VNet to the workspace inbound allow-list in the Fabric Admin Portal
2. Create a **Private Endpoint** to the Fabric capacity and run the app from within that VNet
3. Disable workspace-level inbound rules if public access is acceptable

### Proxy / DNS

- If behind a corporate proxy, configure MSAL's `HttpClient` via `WithHttpClientFactory()`
- Ensure DNS resolves `login.microsoftonline.com` and `api.powerbi.com` — private DNS zones can interfere in VNet-connected environments

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `AADSTS7000215: Invalid client secret` | Entra ID replication delay after secret creation | Wait 30–60 seconds, then retry |
| `403 Forbidden` on ExportToFile | Workspace is not on dedicated capacity | Assign a Fabric/Premium capacity to the workspace |
| Export status `Failed` with no error | Paginated report has required parameters not supplied | Pass parameters via `PaginatedReportConfiguration.ParameterValues` |
| `RequestedFileIsEncryptedOrCorrupted` on import | Fabric inbound networking policy blocking the request | See [Fabric Private Networking](#fabric-private-networking) above |
| `403` when adding SP to workspace via API | Standard API requires existing workspace access | Use the Admin API endpoint (`/admin/groups/{id}/users`) or the Power BI Portal |
