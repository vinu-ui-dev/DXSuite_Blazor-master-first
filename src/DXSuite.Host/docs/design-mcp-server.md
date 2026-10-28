# Design MCP Server — Scope & Architecture

**Status:** scoping (feature/design-mcp-server) · **Owner:** Design module · **Phase:** read-only

## Goal

Let designers point their own Claude (claude.ai custom connector, Claude Desktop, Claude Code)
at DXSuite design data: requirements, worksheets, and catalog context. Claude does the reasoning
(requirements advice, finding reference worksheets); DXSuite exposes the data through a small,
read-only, per-user-authenticated MCP tool surface.

**Non-goals (this phase):** write tools of any kind, in-app AI UI (`ILlmGateway` features remain
a separate future track), exposing data to tenant end-customers, embeddings/vector search.

## Architecture

- **Hosting:** in-process in DXSuite.Host, mapped by `DesignModule.MapEndpoints` at
  `/mcp/design` using the official C# SDK (`ModelContextProtocol.AspNetCore`, stable 1.4.x,
  Streamable HTTP via `MapMcp()`). No separate service or deployment: the endpoint rides the
  existing host, TLS, and tenant subdomain.
- **Tenancy:** `UseTenantResolution()` runs before auth for every request, so the MCP endpoint
  on a tenant subdomain gets `ITenantContext` — and therefore the per-tenant
  `IDbContextFactory<DesignDbContext>` — with zero new plumbing. Tools must additionally verify
  the authenticated token's CustomerId matches the resolved tenant (defense in depth; a token
  from tenant A used against tenant B's subdomain must 403).
- **Module gating:** endpoint returns 404 unless `DesignModule.IsAvailableForTenant` passes,
  mirroring the UI gate.

## Authentication

Claude custom connectors support two shapes: full OAuth 2.1 + PKCE (with dynamic client
registration), or a **fixed bearer credential supplied via request headers**. The host today has
only cookie + OIDC interactive auth — no OAuth authorization server — so standing up OAuth 2.1
(DCR, PKCE, loopback redirects for Claude Code) is the single most expensive line item if done
first.

**Phase 1 — Personal Access Tokens (PAT):**

- New `tblDesignMcpToken`: TokenId, CustomerId, UserId, Name, TokenHash (SHA-256), CreateDate,
  LastUsedDate, ExpiresDate, IsRevoked. Raw token shown once at creation
  (`dxd_<random 32 bytes base64url>`), only the hash stored.
- Issued/revoked from a small "AI Access" section on the Design side (per-user, list + revoke).
- New authentication scheme (`DesignMcpToken`) validating `Authorization: Bearer dxd_…` on the
  MCP endpoint only; principal carries UserId + CustomerId claims. Endpoint has
  `.RequireAuthorization()` with a policy bound to that scheme — cookies are never accepted here.
- Designers add the connector with their personal token in the connector's request-headers
  config. Per-user identity, no OAuth build-out, works across claude.ai / Desktop / Claude Code.

**Phase 2 (optional, demand-driven) — OAuth 2.1:** embed an authorization server (OpenIddict)
bridging to the existing OIDC sign-in, so connector setup becomes a browser sign-in instead of
token copy-paste. Nothing in Phase 1 blocks this; the auth scheme just gains a second accepted
token type.

## Tool surface (v1, all read-only)

| Tool | Returns |
|------|---------|
| `list_designs` | id, name, customer entity, opportunity TranId, sales rep, create date; optional search term |
| `get_design` | design header + opportunity snapshot + requirement categories (ordered) |
| `get_design_requirements` | requirements grouped by category with the same N.M numbering as the UI/Excel; priority, status, client notes, comment count |
| `get_requirement_comments` | comment thread for one requirement |
| `list_worksheets` | worksheets for a design (or all designs) with name, estimate TranId, room/line-item counts |
| `get_worksheet_summary` | one worksheet reduced for comparison: rooms, line-item category mix, labor hours by type, non-inventory rows, travel config presence |
| `find_reference_worksheets` | compact summaries of all tenant worksheets (bounded, newest-first) so Claude can rank candidates against the current design's requirements — retrieval by LLM ranking, no embeddings |
| `search_catalog` | catalog items by term: manufacturer, description, cost/rate |

Conventions: JSON responses; every tool takes explicit ids (no session state); list tools are
capped (default 50, `limit` param) and say so in their output when truncated; all EF queries
`AsNoTracking`.

Pricing/margin data is included (internal users only). Revisit before any tenant-facing use.

## Read-only guarantees

1. No write tools registered; the tool assembly contains only queries.
2. All DbContext usage is `AsNoTracking` with no `SaveChanges` path.
3. (Later hardening, optional) a dedicated read-only SQL login for the MCP connection string.

## Phasing

| Phase | Deliverable | Proves |
|-------|-------------|--------|
| A | SDK package + `MapMcp` in DesignModule + `list_designs`/`get_design_requirements`, dev-only flag, no auth | endpoint shape, tenancy, SDK fit — testable with MCP Inspector / Claude Code locally |
| B | PAT table + migration, auth scheme, token management UI | per-user auth end to end |
| C | full tool surface + worksheet summarizer | the actual designer use cases |
| D | deploy, connector setup guide, and a **designer Skill** (how to compare worksheets, how to frame requirements advice) | adoption |

## Open questions

- Public reachability: is the dev/prod host reachable from claude.ai (SaaS) for the designers'
  tenant, or does this need to wait on network/ingress work?
- Token lifetime policy (90-day default expiry proposed).
- Whether `find_reference_worksheets` should exclude worksheets from designs of other sales reps
  (visibility policy — proposed: no restriction, internal team).
