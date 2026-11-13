# crosstenant-auth-bridge: Cross-Tenant / Cross-Cloud AI Auth Broker

Reference architecture and token broker for **cross-tenant and cross-cloud sovereign access** to Azure AI Foundry agents. 

This service allows client applications residing in one tenant (e.g., **SharePoint Online in Microsoft 365 GCC Moderate**) to securely invoke AI Foundry agents hosted in an isolated sovereign cloud tenant (e.g., **Azure Government**) without B2B guest accounts, directory peering, or client secrets.

---

## 🏛️ Architecture

[![crosstenant-auth-bridge architecture: SharePoint Online SPFx web part in Tenant A (M365 GCC) calls crosstenant-auth-bridge Azure Function in Tenant B (Azure Government), which validates Entra ID JWT in code and invokes Azure AI Foundry agent via Managed Identity.](docs/output/architecture/crosstenant-auth-bridge.svg)](docs/output/architecture/crosstenant-auth-bridge.svg)

[PNG image](docs/output/architecture/crosstenant-auth-bridge.png) | [Editable Excalidraw source](docs/output/architecture/crosstenant-auth-bridge.excalidraw) | [Interactive Archify diagram](docs/architecture/crosstenant-auth-bridge.html)

The diagram describes the checked-in infrastructure and runtime, not live deployment status. The frontend SPFx Web Part runs in Tenant A (M365 GCC Moderate / Commercial Entra ID), while the auth broker Azure Function and Azure AI Foundry project run in Tenant B (Azure Government / Sovereign Cloud). In-code JWT validation verifies caller tokens directly against Tenant A's public discovery endpoint without B2B directory synchronization; `ScopedCredential` rewrites the Managed Identity token scope to `https://ai.azure.us/.default`.

### Architecture Flow

```text
[Tenant A: M365 GCC Moderate / Commercial Entra ID]
       │
       ▼ (SharePoint Online / SPFx Client / Power App)
  1. Requests OAuth token for exposed API scope via AadHttpClientFactory
       │
       ▼
  2. Entra ID Tenant A issues Bearer JWT (Audience: api://<apiClientId>)
       │
       │   HTTPS POST /api/chat with Bearer Token
       ▼
────────────────────────────────────────────────────────────────────────────────
[Cloud / Sovereign Boundary]
────────────────────────────────────────────────────────────────────────────────
       ▼
[Tenant B: Azure Government / Commercial]
[Azure Function: crosstenant-auth-bridge (.NET Isolated)]
       │
       ├── 3. JwtAuthenticationMiddleware validates JWT in C# code
       │      Fetches Tenant A public JWKS from login.microsoftonline.com
       │      Validates: Signature, Issuer, Audience, Lifetime
       │      (No Easy Auth / No directory peering required)
       │
       ├── 4. Extracts caller identity claims (oid, upn, name)
       │
       ├── 5. ScopedCredential overrides Managed Identity scope to:
       │      https://ai.azure.us/.default (Azure Gov) or https://ai.azure.com/.default
       │
       ▼ 6. Calls Azure AI Foundry Responses API via Managed Identity
[Azure AI Foundry Project]
       │
       ▼ (Vector & File Search RAG)
[Foundry Prompt Agent with Knowledge Store]
```

---

## 🎯 Target Environment

| Layer | Cloud / Tenant | Purpose / Notes |
|---|---|---|
| **Client** (SPFx Web Part / Power Apps / Teams) | **Tenant A** (M365 GCC Moderate / Commercial Entra ID) | Caller interface. Users authenticate transparently against Tenant A without secondary login prompts. |
| **Auth Broker** (.NET Isolated Azure Function) | **Tenant B** (Azure Government or Isolated Commercial) | Middle-tier proxy. Validates Tenant A JWTs in C# code using Tenant A's public JWKS discovery endpoint. |
| **AI Agent** (Azure AI Foundry Project) | **Tenant B** (Azure Government or Isolated Commercial) | Hosts the Prompt Agent and Vector/File Search knowledge base. Accessible only via Managed Identity. |

---

## 💡 How It Works

1. **Client Call**: The frontend client (e.g., [sharepoint-foundry-agent-webpart](https://github.com/sallyg1/sharepoint-foundry-agent-webpart)) requests a token for `api://<API_CLIENT_ID>/access_as_user` using Tenant A's Entra ID.
2. **In-Code Cryptographic Validation**: The HTTP request hits the Azure Function `POST /api/chat`. `JwtAuthenticationMiddleware` intercepts the request and verifies the JWT against Tenant A's OpenID Connect discovery document (`https://login.microsoftonline.com/{tenantAId}/v2.0`).
3. **Audience & Authority Check**: Validates that:
   - `Issuer` matches `https://login.microsoftonline.com/{tenantAId}/v2.0` or `https://sts.windows.net/{tenantAId}/`.
   - `Audience` matches the configured `API_CLIENT_ID` or `api://{API_CLIENT_ID}`.
   - Token is active and cryptographically signed with Tenant A's current signing keys.
4. **Token Audience Scoping**: Azure AI Foundry SDK (`Azure.AI.Projects`) defaults to the commercial audience (`https://ai.azure.com/.default`). `ScopedCredential.cs` wraps `DefaultAzureCredential` to target the sovereign cloud scope (`https://ai.azure.us/.default`).
5. **Foundry Agent Invocation**: The function instantiates `AIProjectClient`, manages conversation persistence via `GetProjectConversationsClient()`, and executes prompt inference with grounding via `GetProjectResponsesClientForAgent()`.
6. **Response**: Returns `{ conversationId: "conv_...", reply: "..." }` with CORS headers allowed for the SharePoint origin.

---

## 📁 Repository Layout

| Path | Description |
|---|---|
| `Program.cs` | Function host startup, JWT Bearer configuration with Tenant A authority, and `AIProjectClient` DI registration |
| `JwtAuthenticationMiddleware.cs` | Custom worker middleware enforcing Bearer authentication and setting `HttpContext.User` |
| `ChatFunction.cs` | HTTP trigger endpoint (`POST /api/chat`) handling conversation sessions and agent execution |
| `ScopedCredential.cs` | Custom `TokenCredential` wrapper that overrides token audience scopes for sovereign clouds (Azure Gov) |
| `DallasPermitFunc.csproj` | .NET 8 / 9 isolated worker project file with Azure SDK dependencies |
| `host.json` | Azure Functions host configuration |

---

## 🛠️ Key Design Decisions

- **In-Code JWT Validation over Easy Auth**:
  Azure App Service Authentication (Easy Auth) is bound to the local tenant and cannot easily validate foreign commercial/GCC tokens without complex B2B directory synchronization. Validating via `Microsoft.AspNetCore.Authentication.JwtBearer` directly against the issuer's public JWKS eliminates all directory coupling.
- **Managed Identity (Zero Stored Secrets)**:
  The Azure Function communicates with Azure AI Foundry using its System-Assigned Managed Identity. No API keys or client secrets are stored in code or configuration. The identity requires the **Cognitive Services OpenAI Contributor** (or **Azure AI Developer**) role on the Foundry Project.
- **Sovereign Cloud Audience (`ScopedCredential`)**:
  Allows the same codebase to run seamlessly in Azure Commercial or Azure Government by simply changing the `FOUNDRY_SCOPE` configuration setting (`ai.azure.com` vs `ai.azure.us`).

---

## ⚙️ Configuration Settings

Configure these in `local.settings.json` for local development or in **App Service > Environment Variables** in Azure:

| Setting | Required | Description | Example |
|---|---|---|---|
| `API_TENANT_ID` | Yes | Microsoft Entra ID Tenant GUID for **Tenant A** (M365 / SharePoint tenant) | `f8402f5c-9ad4-48cd-ba98-80ecc3d70afb` |
| `API_CLIENT_ID` | Yes | App ID / Client ID of the App Registration exposed in **Tenant A** | `d4e5f6a7-b8c9-0123-def4-567890abcdef` |
| `FOUNDRY_PROJECT_ENDPOINT` | Yes | Azure AI Foundry Project endpoint in **Tenant B** | `https://ai-foundry-hub.usgovvirginia.models.ai.azure.us` |
| `FOUNDRY_SCOPE` | No | Scope/audience for Foundry token. Default: `https://ai.azure.com/.default`. Set to `https://ai.azure.us/.default` for Azure Gov. | `https://ai.azure.us/.default` |
| `FOUNDRY_AGENT_NAME` | Yes | Display name of the published Foundry Prompt Agent | `dallas-permit-agent` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | No | Application Insights connection string for OpenTelemetry logging | `InstrumentationKey=...` |

---

## 🌐 CORS Configuration

For web clients (like SharePoint SPFx), configure CORS on the Function App:
1. In the Azure Portal, navigate to **Function App > API > CORS**.
2. Add your SharePoint Online root URL (e.g. `https://yourtenant.sharepoint.com`).
3. Ensure **Enable Access-Control-Allow-Credentials** is checked.
4. Verify no trailing spaces or hidden unicode characters exist in the allowed origins list.

---

## 🚀 Build and Run

### Prerequisites
- .NET 8.0 SDK or .NET 9.0 SDK
- Azure Functions Core Tools v4 (`npm install -g azure-functions-core-tools@4`)
- Azure CLI (`az login`)

### Build
```bash
dotnet build DallasPermitFunc.csproj
```

### Local Run
```bash
func start
```

---

## 🔗 Related Repositories

- **Client Web Part**: [sharepoint-foundry-agent-webpart](https://github.com/sallyg1/sharepoint-foundry-agent-webpart) — SPFx v1.24 React web part deployed to SharePoint Online modern pages.
