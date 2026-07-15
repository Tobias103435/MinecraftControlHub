# Health Monitoring and Diagnostics

## Overview

The health subsystem gives users a live view of the state of their Minecraft installations. It aggregates signals from Java detection, RAM usage, mod update availability, and crash history into a single score that drives the health cards on the Home page.

---

## Components

| Component | Responsibility |
|---|---|
| `HealthCheckService` | Aggregates health signals per installation into a `HealthReport` with a numeric score |
| `RamCalculatorService` | Recommends Min/Max RAM based on Minecraft version, loader, mod count, and render distance |
| `AppLogService` | Append-only diagnostics log (`diagnostics.log`) for non-UI errors and background activity |
| `JavaService` | Detects installed JREs/JDKs and recommends versions per Minecraft version |

---

## HealthCheckService

`HealthCheckService` runs a set of checks against an installation and produces a `HealthReport`:

```csharp
public class HealthReport
{
    public int Score { get; init; }              // 0–100
    public bool JavaOk { get; init; }
    public bool RamOk { get; init; }
    public bool HasModUpdates { get; init; }
    public bool HasRecentCrash { get; init; }
    public string? JavaIssueDetail { get; init; }
    public string? RamIssueDetail { get; init; }
}
```

### Scoring

| Check | Points deducted |
|---|---|
| Java version incompatible | −25 |
| Java not found | −30 |
| Allocated RAM below minimum | −20 |
| Allocated RAM above 75% of system RAM | −10 |
| Mod updates available | −5 |
| Crash in last 24 hours | −15 |

A score of 100 means no issues detected. The Home page shows a color-coded indicator: green (80–100), yellow (50–79), red (0–49).

---

## RamCalculatorService

Recommends RAM allocation based on:

- **Minecraft version** — newer versions require more RAM
- **Mod loader** — modded instances need more than Vanilla
- **Mod count** — each mod adds an estimated overhead
- **Render distance** — higher distances increase chunk loading memory

```csharp
public RamRecommendation Calculate(Installation installation, int modCount)
{
    // Base values per version + loader
    int baseMin = GetBaseMin(installation.MinecraftVersion, installation.Loader);
    int baseMax = baseMin * 2;

    // Scale by mod count
    int modOverhead = modCount * 32; // MB per mod
    return new RamRecommendation(
        Min: baseMin + modOverhead,
        Max: Math.Min(baseMax + modOverhead, SystemRamMb / 2)
    );
}
```

---

## AppLogService

A lightweight append-only logger that writes to `diagnostics.log`. Used for background service errors, HTTP failures, and provisioning events — not for game output (that goes to per-server terminal streams).

```csharp
public interface IAppLogService
{
    void Log(string message);
    void LogError(string message, Exception? ex = null);
    IReadOnlyList<string> GetRecentLines(int count = 100);
}
```

All writes are thread-safe. Log entries include a UTC timestamp prefix.

---

## JavaService

Detects installed Java runtimes on the system and recommends the correct version per Minecraft version:

| Minecraft Version | Required Java |
|---|---|
| 1.17 and earlier | Java 8 |
| 1.18 – 1.20 | Java 17 |
| 1.21+ | Java 21 |

`JavaService` scans common installation paths, the Windows Registry, and the `JAVA_HOME` environment variable. Each installation can also specify a custom Java path that overrides auto-detection.

---

## Diagnostics Log Location

```
%LocalAppData%\MinecraftControlHub\logs\diagnostics.log
```

The log is plain text with one entry per line. It is not rotated automatically — users should clear it manually if it grows large.
