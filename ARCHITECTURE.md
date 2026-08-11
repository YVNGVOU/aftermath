# SINVAUX — Product Architecture

Canonical reference. Update this file first when the product's shape changes;
code follows the document, not the other way around.

```
SINVAUX
│
├── AFTERMATH   — individual endpoint forensic application (this repo)
├── FLEET       — enterprise endpoint security platform (separate product, not yet built)
└── SETTINGS    — configuration appropriate to whichever environment is active
```

## The question each answers

| | Aftermath | Fleet |
|---|---|---|
| Question | "What is happening on this machine?" | "What is happening across our organization?" |
| Scope | One endpoint | An organization's fleet of endpoints |
| Account model | None required | Organization enrollment, RBAC |
| Operation | Manual, investigative | Continuous, policy-driven |
| Ships as | This single `Aftermath.exe` | A separate product — client + backend service |

Shared across both: visual language, design tokens, terminology, the detection
model, the artifact model, the severity system, forensic concepts. Aftermath
and Fleet should look like the same family. They are not the same program.

## Why Fleet cannot be "Aftermath with more computers"

Fleet's stated requirements — organization accounts, RBAC, authenticated API
communication, secure endpoint enrollment, policy inheritance, audit logs,
encrypted credential storage, org isolation — all require a **server**:
something that holds state across sessions and across machines, authenticates
callers, and enforces authorization. `Aftermath.exe` is a self-contained
WinForms binary with no server component and no persistent identity beyond
one local user's `%LOCALAPPDATA%`. It cannot become Fleet by accumulating
features; Fleet is a different product that happens to reuse Aftermath's
detection/forensics core.

**Rule: nothing in this repo may claim a Fleet capability it cannot actually
back.** No fabricated org dashboards, no decorative RBAC, no "Connected"
status for a service that isn't. See `docs/dont-fabricate.md` if that file
exists, or the pattern already established in the Settings Connections page
(one honest empty state, nothing invented).

## What belongs in Aftermath

Everything already in the sidebar: Overview, Detections, Exposure, History,
Artifacts, Drift, Startup, Persistence, Network, System, Cleanup, Quarantine.
Local configuration (theme, elevation, auto-trigger, quarantine retention)
lives in the Settings workspace, not scattered across Overview or the header.

**Never add to Aftermath's navigation:** organizations, users, fleet
policies, endpoint groups, an organization dashboard, enterprise billing, or
RBAC administration. Those are Fleet concepts. If a feature request implies
"across multiple machines with permissions," it belongs in Fleet's spec, not
this app's sidebar.

## The current "Fleet" sidebar page — a naming and scope correction

Before this document existed, an "agentless sweep" feature was built: an
admin types an explicit host list into Aftermath, it pushes itself via WMI to
each host, runs the same local triage, and pulls results back into one
console. **That feature is legitimate and useful, but it is not Fleet** under
this architecture — it has no organization account, no auth, no persistent
endpoint identity, no RBAC. It is a one-shot incident-response utility that
happens to touch more than one machine, which is exactly the "Aftermath with
more computers" pattern this document rules out for anything called Fleet.

**Done: renamed to Sweep.** Every `Fleet`-prefixed identifier, page key, file
name (`Fleet.cs` → `Sweep.cs`, `FleetReport.cs` → `SweepReport.cs`), and
on-screen label was renamed throughout the codebase; it stays inside
Aftermath's sidebar exactly as built. This keeps real, working functionality
(headless mode, JSON reports, WMI push/pull — all compile and have been
exercised) without misrepresenting it as the enterprise product.

The WMI push/pull and headless-report code is exactly what a real Fleet
backend would eventually orchestrate when it exists — it isn't wasted, it's
just not entitled to the Fleet name until there's an organization and an
identity behind who's allowed to sweep whom. When Fleet's org/auth layer is
actually built, this mechanism is the natural starting point for its
"push a scan to an enrolled endpoint" primitive — at that point it moves out
of Aftermath's sidebar into Fleet proper.

## What belongs in Fleet (not yet built — spec, not implementation)

Enterprise-only. Requires organization enrollment (business identity,
verified — a corporate email domain alone is not sufficient proof and the
architecture must not treat it as such). Modeled as:

```
ORGANIZATION
    ├── USERS
    ├── ADMINS
    ├── POLICIES
    ├── ENDPOINTS
    ├── DETECTIONS
    ├── INCIDENTS
    ├── ALERTS
    └── AUDIT LOG
```

Not `USER → COMPUTERS`. An endpoint belongs to an organization; a user
belongs to an organization; permissions determine what that user can see or
change within it.

**Roles** (architecture must support adding these without a redesign, not all
need to exist on day one): Organization Owner, Security Administrator,
Administrator, Security Analyst, Viewer.

**Detection-to-response pipeline** Fleet is built around:

```
TRIGGER → DETECTION → CORRELATION → INVESTIGATION → RESPONSE → FLEET IMPACT
```

Correlation connects process → file → persistence → network → session → other
events into one evidence timeline (Aftermath already produces most of these
as individual Findings; Fleet's job is linking them across an org). Fleet
Impact asks whether the same pattern exists on other endpoints — this is
Fleet's actual differentiator over a traditional AV console, not a bigger
dashboard.

**Fleet navigation** (separate from Aftermath's, not merged into it):

```
FLEET
  OVERVIEW
  ENDPOINTS      (All / At Risk / Offline / Protected)
  SECURITY       (Detections / Alerts / Incidents / Threats)
  INVESTIGATIONS (Timeline / Evidence / Cases)
  RESPONSE       (Isolation / Quarantine / Remediation)
  POLICIES       (Protection / Detection / Scanning / Network)
  ORGANIZATION   (Users / Roles / Integrations / Audit Log)
  REPORTING
  SETTINGS
```

Build only the sections the backend actually supports at the time — this
document sets direction, it does not authorize building decorative UI ahead
of real capability.

**Fleet access UX** for an Aftermath user with no organization: a calm
enrollment screen ("Sign In" / "Create Organization"), explicitly framed as
"Fleet requires an organization account" — not a paywall, a statement of what
the product is for.

## Settings — scoped to the active environment

Settings' contents depend on whether Aftermath or Fleet is active. Today,
only Aftermath exists, so Settings holds only Aftermath-appropriate
configuration (Appearance, Security/Elevation, Scanning/auto-trigger,
Data/Quarantine retention, Connections — currently empty, About). If Fleet is
ever active in the same client, its settings (organization, policies, users)
are Fleet's own Settings content, not appended to Aftermath's.

## Standing rule for future work

Before adding navigation, ask: **does this describe one machine, or an
organization?** One machine → Aftermath. An organization → Fleet, and Fleet
does not exist yet as real, backed functionality — spec it here first, build
the backend, then build the client surface. Never let the answer be "add it
to Aftermath's sidebar because that's what's open right now."
