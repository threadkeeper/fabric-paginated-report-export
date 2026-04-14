# Security Review: Power BI `Tenant.ReadWrite.All` Application Permission

**Prepared for:** SecOps / Identity & Access Review  
**Application:** Paginated Report PDF Export Service  
**Date:** April 2026

---

## 1. Permission Overview

| Property | Value |
|---|---|
| **API** | Power BI Service (`00000009-0000-0000-c000-000000000000`) |
| **Permission** | `Tenant.ReadWrite.All` |
| **Type** | Application (Role) — no user context |
| **Permission ID** | `28379fa9-8596-4fd9-869e-cb60a93b5d84` |
| **Requires** | Entra ID admin consent |

## 2. What This Permission Grants

`Tenant.ReadWrite.All` is the **broadest** Power BI application permission. It grants the service principal the ability to:

- **Read** all workspaces, reports, datasets, dashboards, dataflows, and capacity information across the entire Power BI tenant
- **Write** (create, update, delete) reports, datasets, and other artifacts in any workspace where the service principal is a member
- **Export** reports to file (PDF, PPTX, XLSX, etc.)
- **Manage** workspace membership, refresh schedules, and data sources
- **Execute** queries against datasets

**Scope is constrained by workspace membership:** although the permission is tenant-wide, the service principal can only operate on workspaces where it has been explicitly added as a member (Contributor or above). It cannot access workspaces it is not a member of, except via the Admin API path.

## 3. Attack Surface

| Vector | Risk | Mitigation |
|---|---|---|
| **Client secret compromise** | Attacker can authenticate as the SP and access all workspaces the SP is a member of | Store secrets in Azure Key Vault; rotate regularly; use certificate auth instead of secrets |
| **Over-provisioned workspace access** | SP added to workspaces beyond what is required | Add SP only to the specific workspace(s) needed; audit membership periodically |
| **Data exfiltration** | SP can export any report in its workspaces to PDF/Excel | Limit SP to Contributor role (not Admin); monitor export activity via Power BI audit logs |
| **Lateral movement** | If SP has Admin role, it could add other principals to workspaces | Use Contributor role (verified sufficient for export); avoid Admin unless workspace management is required |
| **Token replay** | Stolen access tokens can be used until expiry (typically 1 hour) | Use short-lived tokens; restrict network access to known IPs |

## 4. Why This Permission and Not a Lower One

The Power BI REST API defines **delegated scopes** (e.g., `Report.Read.All`, `Dataset.Read.All`) for user-context flows, but for **service principal (client credentials)** flows, only two application-level permissions exist:

| Application Permission | Scope |
|---|---|
| `Tenant.Read.All` | Read-only access to all tenant content |
| `Tenant.ReadWrite.All` | Read/write access to all tenant content |

The `ExportToFile` API is a **write operation** (it creates an export job), so `Tenant.Read.All` is insufficient. `Tenant.ReadWrite.All` is the **only available application permission** that supports report export via service principal.

> **Microsoft's own guidance** for service principal embedded scenarios states: *"A Microsoft Entra application doesn't require you to configure any delegated permissions or application permissions in the Azure portal when it has been created for a service principal. We recommend that you avoid adding permissions."*  
> — [Embed Power BI content with service principal](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal)

In practice, when a service principal is added as a workspace member with Contributor role, it can perform operations within that workspace without explicitly configured API permissions in some Fabric configurations. However, for the `ExportToFile` API via client credentials, `Tenant.ReadWrite.All` with admin consent was required in our testing.

## 5. Is `User.Read` (Microsoft Graph) Required?

**No.** The `User.Read` delegated permission (Microsoft Graph) is automatically added by the Azure Portal when creating any app registration. This application authenticates via client credentials only — there is no user context. `User.Read` is never invoked and can be safely removed.

## 6. Best Practices & Recommendations

### Least Privilege
- [x] Use **Contributor** workspace role (not Admin) — verified sufficient for report export
- [ ] Add the SP only to the workspaces that contain reports it needs to export
- [ ] Remove `User.Read` delegated permission (unused)
- [ ] Consider whether `Tenant.Read.All` would be sufficient if the export operation were performed via a different mechanism (e.g., user-delegated flow)

### Credential Security
- [ ] Use **certificate-based authentication** instead of client secrets ([Microsoft recommendation](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal-certificate))
- [ ] Store credentials in **Azure Key Vault** with managed identity access
- [ ] Set secret expiry to **6 months or less** and automate rotation
- [ ] Enable **conditional access policies** for service principal sign-ins (requires Entra ID P1+)

### Monitoring & Audit
- [ ] Enable **Power BI audit logs** via Microsoft Purview or Unified Audit Log — export operations generate `ExportReport` events
- [ ] Set alerts on high-volume export activity from the SP
- [ ] Monitor Entra ID **sign-in logs** for the service principal (filter by application ID)
- [ ] Review workspace membership quarterly

### Network Controls
- [ ] Restrict the SP's token acquisition to known IPs via **Entra ID Conditional Access**
- [ ] If Fabric capacity uses **private networking**, ensure inbound rules allow the application's network
- [ ] Tenant-level settings `AllowAccessOverPrivateLinks` and `WorkspaceBlockInboundAccess` should be reviewed to match your network posture

### Tenant Settings (Power BI Admin Portal)
- [ ] Scope "Allow service principals to use Power BI APIs" to a **dedicated Entra ID security group** containing only approved service principals — do not enable for the entire organisation
- [ ] Scope "Embed content in apps" to the same security group

## 7. References

| Resource | URL |
|---|---|
| Embed with service principal | https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal |
| Embed with certificate (recommended) | https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-service-principal-certificate |
| ExportToFile API reference | https://learn.microsoft.com/en-us/rest/api/power-bi/reports/export-to-file-in-group |
| Export paginated reports | https://learn.microsoft.com/en-us/power-bi/developer/embedded/export-paginated-report |
| Power BI audit log events | https://learn.microsoft.com/en-us/power-bi/admin/service-admin-auditing |
| Fabric private networking | https://learn.microsoft.com/en-us/fabric/security/security-private-links-overview |
| Conditional Access for workload identities | https://learn.microsoft.com/en-us/entra/identity/conditional-access/workload-identity |
