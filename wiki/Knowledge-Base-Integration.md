# Knowledge Base Integration

## Overview

`KnowledgeService` composes the AI system prompt from two sources: static JSON knowledge files bundled with the app, and a live context snapshot fetched fresh each turn.

---

## IKnowledgeService

```csharp
public interface IKnowledgeService
{
    Task<string> BuildSystemPromptAsync();
}
```

---

## Static Knowledge Loading

Knowledge files are bundled as embedded resources in the `AI/Knowledge/` folder. They are loaded once and cached:

```csharp
private string LoadStaticKnowledge()
{
    var sb = new StringBuilder();
    foreach (var file in _knowledgeFiles)
    {
        var content = LoadEmbeddedResource(file);
        sb.AppendLine(content);
    }
    return sb.ToString();
}
```

---

## Live Context Assembly

Called on every turn to reflect the current app state:

```csharp
private async Task<string> BuildLiveContextAsync()
{
    var installations = await _installationService.GetAllInstallationsAsync();
    var servers       = await _serverService.GetAllServersAsync();

    var sb = new StringBuilder();
    sb.AppendLine("=== CURRENT STATE ===");

    sb.AppendLine("Installations:");
    foreach (var inst in installations)
    {
        var mods = await _modService.GetInstalledModsAsync(inst.Id);
        sb.AppendLine($"- \"{inst.Name}\" ({inst.Loader}, {inst.MinecraftVersion})");
        if (mods.Any())
            sb.AppendLine($"  Mods: {string.Join(", ", mods.Select(m => m.Name))}");
    }

    sb.AppendLine("Servers:");
    foreach (var srv in servers)
    {
        sb.AppendLine($"- \"{srv.Name}\" ({srv.Type}, {srv.Version}) — {(srv.IsRunning ? "RUNNING" : "STOPPED")}");
    }

    return sb.ToString();
}
```

---

## DI Registration

```csharp
services.AddSingleton<IKnowledgeService, KnowledgeService>();
```

Singleton because the static knowledge files are loaded once and the live context query is fast (in-memory).
