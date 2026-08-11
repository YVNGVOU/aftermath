# Aftermath

**Post-infection triage for Windows.** Reads the verdicts your antivirus already made, finds the wreckage, and helps you remove it safely.

Single 33 KB `.exe`. No installer, no runtime download, no dependencies. Download it, double-click it, click one button.

---

## What this is not

**Aftermath is not a virus scanner.** It has no detection engine, no signature database, and it will never tell you a machine is "clean."

It is the tool you run *after* something got flagged — or after you installed something you now regret. Antivirus tells you *that* something was bad. Aftermath tells you *what happened next*: what else appeared at the same moment, what got left behind, what config was quietly changed, and what is still sitting on disk refusing to delete.

## Why it exists

A full antivirus scan takes hours. But Windows Defender has **already scanned** everything that executed on your machine, and it keeps the records. Reading those records takes about one second.

That reframing is the whole tool:

> **Read the verdict before running the scan.**

Once you know something was flagged at 13:42, "what else showed up around 13:42" turns a whole-disk hunt into a targeted sweep.

## What it checks

| Area | What it looks at |
|---|---|
| **Detections** | Defender's detection history with timestamps and file paths, threat names and severity, whether real-time protection is still on |
| **Artifacts** | Disk images (`.iso`/`.img`/`.vhd`), crack/keygen/repack filename patterns, and **binaries whose signature does not match their contents** |
| **Startup** | Every Run key and Startup folder entry, each verified by Authenticode signature rather than by name |
| **System** | Proxy hijack (`AutoConfigURL`), and hosts-file entries blocking Windows Update or antivirus domains |

### The two checks worth calling out

**Tampered binaries.** A file can carry a real vendor signature and still have been modified after signing. Aftermath calls `WinVerifyTrust` and reports `HashMismatch` — which caught a trojanized Adobe installer that Defender did not flag.

**Proxy hijack.** Planting an `AutoConfigURL` silently routes your traffic through someone else's server. It survives cleanup, it survives reboots, and almost nobody checks it.

## Deleting things that will not delete

The other half of the tool, and the reason it exists at all.

When a file is locked by an anti-malware filter driver, Windows does not give you an error — **the operation hangs forever**. Explorer spins. `del` never returns. Every guide online assumes you get a message you can act on.

`Remove-LockedItem` runs an escalation ladder, and **every rung is time-bounded**, so it can never freeze:

1. Ordinary delete
2. Clear read-only/hidden/system attributes, retry
3. `robocopy /MIR` mirror-wipe from an empty directory — beats long paths, odd characters, deep trees
4. **Schedule deletion for next logon** — nothing holds the file at boot. The task deletes the file, then deletes itself and its own script.

Rung 4 is what actually wins against filter-driver locks. You restart, and it is gone.

## Safety

This tool deletes files, so its restraint is a feature:

- **Nothing is deleted unless you tick it and confirm.** No auto-remediation, ever.
- **Protected paths are refused in code** — `C:\Windows`, Program Files roots, your profile root, and any Office program folder — regardless of what the UI asks for.
- **Every operation is time-bounded.** The app cannot hang.
- **Findings carry their evidence**, not just a verdict.

That Office rule exists for a specific reason. A pirated Office 2019 can be layered on top of a **legitimate, paid Microsoft 365** install sharing the same folder. The obvious cleanup — delete the Office directory — destroys software you paid for. A tool with no judgment makes that call every time, so this one is forbidden from making it.

## Not crying wolf

A security tool that flags harmless things trains you to ignore it. Three rules keep the noise down:

- **Keywords must match whole words.** Without this, `TexturePacker.msi` matches "repack" (tex-**turepack**-er) and every Visual Studio log matches "activat" via `AlwaysActivate`.
- **Only payload-capable files are keyword-matched.** An FL Studio preset called `MC Cracked.sawer` cannot execute anything, so it is never flagged.
- **Signature checks understand catalog signing.** Most Windows system binaries are signed via separate catalog files, not embedded signatures. A naive check reports `SecurityHealthSystray.exe` — Defender's own tray icon — as unsigned. Aftermath resolves these through Windows' own verification path.

Detections are also **deduplicated and aged**. Defender writes a new record every rescan, so one bad file can appear a dozen times; a year-old adware bundler is shown as history, not an emergency.

## Limitations, stated plainly

- A clean report **does not mean the machine is clean.** It means these specific checks found nothing.
- Network and process checks are **point-in-time**. Malware that ran and exited leaves nothing to see.
- Some checks (Defender exclusion lists, other users' processes) need Administrator. Run elevated for full coverage; without it, those sections report what they could not read rather than pretending.
- Signature checks skip revocation for speed, so they work offline.

**If a stealer executed on your machine, change your passwords from a different device.** No cleanup tool can un-steal a credential.

## Build

Needs nothing but Windows:

```
build.cmd
```

Compiles with the in-box .NET Framework compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. That is why the output runs anywhere without a runtime install.

## A note on SmartScreen

Aftermath is unsigned, so Windows SmartScreen will warn the first time you run it. Code-signing certificates cost money; this is a free tool.

You can verify what you are running instead of trusting it: the entire program is one readable source file, and `build.cmd` reproduces the executable byte-for-byte using a compiler that is already on your machine.

## License

MIT.
