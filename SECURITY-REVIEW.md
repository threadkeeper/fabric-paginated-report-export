# Security Review: Power BI Paginated Report Export — Service Principal

**Application:** Paginated Report PDF Export Service  
**Date:** April 2026

---

## 1. Authentication Model

| Property | Value |
|---|---|
| **Identity type** | Service principal (Entra ID app registration) |
| **Auth flow** | OAuth 2.0 client credentials (`AcquireTokenForClient`) |
| **Token scope** | `https://analysis.windows.net/powerbi/api/.default` |
| **API permissions configured** | **None** — no delegated or application permissions are assigned |
| **Authorization mechanism** | Power BI Admin Portal tenant settings + workspace role membership |

## 2. Why No API Permissions Are Needed

When using a service principal with Power BI, Entra ID API permissions are **not evaluated**. Authorization is managed entirely by Power BI Admin Portal tenant settings and workspace role membership. The service principal has **zero standing permissions** in Entra ID — its access is scoped entirely by the Power BI platform.

For details and Microsoft's official guidance, see [`README.md` — Step 2: API Permissions](README.md#-step-2-api-permissions).

## 3. What the Service Principal Can Do

The service principal's effective access is determined by two factors:

### Tenant Settings (Power BI Admin Portal)

| Setting | Required Value | Scope |
|---|---|---|
| **Service principals can call Fabric public APIs** | Enabled | Scoped to a dedicated security group |
| **Embed content in apps** | Enabled | Scoped to the same security group |
| **Export reports as PDF/PowerPoint** | Enabled (default) | Controls whether export is allowed at all |

### Workspace Role

| Role | Grants |
|---|---|
| **Member** (configured) | List reports, trigger exports, download exported files within the assigned workspace(s) only |

The service principal **cannot**:
- Access workspaces it is not a member of
- Modify workspace membership or settings
- Access the Admin API (unless explicitly granted Power BI admin role, which it is not)
- Access any Microsoft Graph resources (no Graph permissions assigned)
- Access any other Azure resource

## 4. Attack Surface

| Vector | Risk | Mitigation |
|---|---|---|
| **Client secret compromise** | Attacker can authenticate as the SP and export reports from assigned workspaces | Store secrets securely (see below); rotate regularly; use certificate auth |
| **Data exfiltration via export** | SP can export any report in its workspace(s) to PDF | Limit SP to only the workspace(s) needed; monitor export activity via audit logs |
| **Over-provisioned workspace access** | SP added to workspaces beyond what is required | Audit workspace membership periodically; use a single-purpose workspace |
| **Token replay** | Stolen access tokens can be used until expiry (typically 1 hour) | Restrict network access; use conditional access policies |
| **Tenant setting too broad** | "Service principals can call Fabric public APIs" enabled for entire org | Scope to a dedicated Entra ID security group containing only this SP |

## 5. Current Credential Storage

| Component | Location | Risk | Recommendation |
|---|---|---|---|
| Client secret | `.env` file on disk (`C:\PaginatedExport\.env`) | Readable by users with file system access | Migrate to Azure Key Vault or Windows Credential Manager |
| `.env` file | Git-ignored, not in source control | Accidental commit could expose secret | Verified `.gitignore` includes `.env` |
| File permissions | `svc_paginated` has Modify; Administrators have Full Control | Appropriate for a dedicated service account | Restrict to Read for the `.env` file specifically |

## 6. Recommendations

### Least Privilege (Current Status)
- [x] **No API permissions** assigned in Entra ID — zero standing permissions
- [x] **Member** workspace role (not Admin or Contributor)
- [x] Tenant settings scoped to a **dedicated security group**
- [x] SP added only to the workspace(s) containing reports it needs to export

### Credential Security
- [ ] Use **certificate-based authentication** instead of client secrets ([Microsoft recommendation](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal-certificate))
- [ ] Migrate secrets to **Azure Key Vault** with managed identity access
- [ ] Set secret expiry to **6 months or less** and automate rotation
- [ ] Tighten `.env` file permissions: `icacls .env /grant svc_paginated:R /inheritance:r`

### Monitoring & Audit
- [ ] Enable **Power BI audit logs** via Microsoft Purview — export operations generate `ExportReport` events
- [ ] Set alerts on high-volume or unusual export activity from the SP
- [ ] Monitor Entra ID **sign-in logs** for the service principal (filter by application ID)
- [ ] Review workspace membership quarterly

### Network Controls
- [ ] Restrict the SP's token acquisition to known IPs via **Entra ID Conditional Access** (requires Entra ID Workload Identities Premium)
- [ ] If Fabric capacity uses **private networking**, ensure inbound rules allow the application's network
- [ ] Run the application from within the VNet if "Block Public Internet Access" is enabled

### Runtime Hardening
- [ ] Run scheduled task under a **dedicated local service account** (`svc_paginated`) — not SYSTEM or an admin account
- [x] Service account has only **Modify** on the application folder — no admin privileges
- [x] Scheduled task configured with **10-minute execution timeout**
- [ ] Implement log rotation to prevent unbounded disk usage

## 7. References

| Resource | URL |
|---|---|
| Certificate auth (recommended) | https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal-certificate |
| ExportToFile API reference | https://learn.microsoft.com/en-us/rest/api/power-bi/reports/export-to-file-in-group |
| Export paginated reports | https://learn.microsoft.com/en-us/power-bi/developer/embedded/export-paginated-report |
| Power BI audit log events | https://learn.microsoft.com/en-us/power-bi/admin/service-admin-auditing |
| Fabric private networking | https://learn.microsoft.com/en-us/fabric/security/security-private-links-overview |
| Conditional Access for workload identities | https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity |
