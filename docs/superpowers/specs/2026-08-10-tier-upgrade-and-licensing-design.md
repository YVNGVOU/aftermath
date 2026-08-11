# Tier / Upgrade Page & Licensing — Design

Status: approved (pending written-spec review)
Date: 2026-08-10

## Purpose

Add a monetization layer to SINVAUX Aftermath: five tiers (Free, Plus, Pro,
Max, Enterprise), an in-app Upgrade page that presents them, and the backend
plumbing that makes purchased tiers actually unlock features. Purchase
happens on the (not-yet-built) SINVAUX website, not in-app — Aftermath stays
a single-file desktop app with no account system for individuals.

This spec covers the licensing/entitlement system and the Upgrade page.
Individual paid features (Scheduled Drift baselines, PDF export engine,
custom scan profiles, etc.) get their own specs when built; this doc only
defines *what tier each belongs to* and *how gating is enforced*, not their
internals.

## Tiers

| Tier | Price | Includes |
|---|---|---|
| **Free** | $0 | Scan, quarantine/restore, restart-to-finish-cleanup, Detections, Exposure, History |
| **Plus** | paid | Everything in Free + Artifacts, Startup, Persistence, Network, System pages + raw export (CSV/JSON) |
| **Pro** | paid | Everything in Plus + Scheduled Drift baselines + SINVAUX-branded PDF report export + unlimited history retention |
| **Max** | paid | Everything in Pro + attack-chain correlation timeline + custom scan profiles + multi-host Sweep (cap: 5 hosts) |
| **Enterprise** | custom | Everything in Max + full Fleet (unlimited hosts, org account, RBAC) — "Contact us," no self-serve purchase |

Future roadmap features (pirated-software residue scanner, network
connection historian, Fleet Lite, etc.) slot into this same tier ladder when
built — no new tier page work needed per feature.

## Upgrade Page

- New page in the app (mockup approved: 5-column card layout, SINVAUX-dark
  surface `Encre`/`Parchemin`, Grenat accent, Pro marked "Most popular").
- Each card lists what that tier adds (deltas visible, not just totals) plus
  a few greyed-out not-yet-unlocked items for context.
- Free card shows "Current plan" (or the user's actual current tier,
  disabled) instead of a buy button.
- Plus/Pro/Max cards: "Get {Tier}" button.
- Enterprise card: "Contact us" button — no purchase flow, no price shown
  beyond "Custom."

## Purchase Flow

- Buy buttons open the user's default browser to a tier-specific URL on the
  SINVAUX site, e.g. `sinvaux.com/buy?tier=pro&machine=<anonymous-id>`. The
  `machine` id is optional convenience for the site to pre-associate a
  license key with this install — not required for the license to function.
- Contact-us opens a plain contact/lead page (or `mailto:`).
- After purchase, the site issues a license key to the user (displayed +
  emailed, site's call). No in-app browser, no OAuth callback, no localhost
  listener, no custom URL protocol handler in v1 — user pastes the key back
  into the app manually.
- v2 candidate (not in scope now): a custom `aftermath://license?key=...`
  protocol handler for auto-fill, if manual paste proves clunky in practice.

## Licensing / Entitlement Model

- New Settings → **License** section replaces the current honest-empty-state
  Connections tab. User pastes their license key there.
- License key is a signed blob (tier + expiry + seat info) verified
  **offline** against an embedded public key — Free/Plus/Pro/Max all work
  with no network call, consistent with Aftermath's "one machine, no
  account" framing.
- Enterprise is the one exception: since Fleet requires a real org/RBAC
  backend anyway, Enterprise licenses validate online against that future
  backend instead of offline.
- Verified tier + expiry persisted locally via a new `License.cs`, following
  the same flat-JSON, `.tmp`-then-`File.Move` pattern as
  `Quarantine.cs`/`Baseline.cs`.
- A single `Entitlements.cs` loads the verified license once at startup and
  exposes typed checks: `Entitlements.Current.Tier`, `.HasArtifacts`,
  `.HasScheduledDrift`, `.HasPdfExport`, `.MaxSweepHosts`, etc.

## Gating Enforcement

- Gated pages (Artifacts/Startup/Persistence/Network/System below Plus) are
  **not added** to the Sidebar at build time for a tier that lacks them —
  never present-but-disabled. Matches the project's zero-tolerance rule
  against decorative UI implying a capability that isn't really there.
- Gated actions (PDF export button, "Add scan profile," Sweep host entry
  past the cap) show a small Grenat "Upgrade" pill that deep-links to the
  Upgrade page instead of silently failing or faking output.
- Sweep's host-list entry point checks `Entitlements.Current.MaxSweepHosts`
  before running; an over-cap attempt blocks the whole run (no partial
  execution) and shows the Upgrade pill.

## Export Engines (tier-gated features, scoped here only at the "what tier" level)

- **CSV/JSON (Plus)**: small addition reusing the existing hand-rolled JSON
  writer pattern; CSV via a new `Exporter.Csv` helper. Low complexity.
- **Branded PDF (Pro)**: highest-complexity piece. No PDF library is
  available under the project's constraints (.NET Framework, in-box `csc.exe`
  only, no NuGet), so this requires a minimal hand-rolled `PdfWriter.cs` that
  emits the PDF file format directly — header lockup, stat-tile row, one
  findings table, footer, per the approved mockup (Parchemin paper, Grenat
  hairline, SINVAUX header/footer on every export regardless of tier). Scoped
  to what the mockup needs, not a general-purpose PDF library.

## Out of Scope (this spec)

- Scheduled Drift baseline runner internals (own spec later)
- Custom scan profile internals (own spec later)
- Attack-chain correlation timeline (own spec later)
- Fleet / Enterprise backend (separate product, not this app)
- `aftermath://` protocol handler (v2 candidate, noted above)

## Risks — Resolved via Spike

Both open risks were de-risked with a throwaway spike
(`.spikes/pdf-and-licensing/Spike.cs`, compiled and run standalone with the
same `csc.exe`, not part of the app build — safe to delete before
implementation):

- **License signing scheme**: RSA-2048
  (`RSACryptoServiceProvider`/`SHA256CryptoServiceProvider`, both in-box, no
  NuGet). Spike signed a payload, verified it with only the public half of
  the key pair (matching what the app would embed), and confirmed a
  tampered payload is correctly rejected. **Decision: RSA-2048, not HMAC** —
  safer against key extraction from the shipped binary.
- **Hand-rolled PDF writer**: confirmed feasible. The spike hand-emits a
  minimal valid PDF (catalog/pages/page/content-stream/font objects, xref
  table, trailer) with zero external libraries, and it renders correctly in
  a real PDF viewer — header text, and a filled Grenat-colored rule proving
  both text and basic vector graphics work. This validates the
  `PdfWriter.cs` approach described above is buildable within the project's
  `csc.exe`-only constraint.
