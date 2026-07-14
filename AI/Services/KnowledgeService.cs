using System.IO;
using System.Text;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.AI.Services;

public class KnowledgeService : IKnowledgeService
{
    private static readonly string KnowledgeDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "AI", "Knowledge");

    private readonly IInstallationService _installationService;
    private readonly IModService          _modService;
    private readonly IServerService       _serverService;

    public KnowledgeService(
        IInstallationService installationService,
        IModService          modService,
        IServerService       serverService)
    {
        _installationService = installationService;
        _modService          = modService;
        _serverService       = serverService;
    }

    public Task<string> GetCommandKnowledgeAsync() =>
        ReadFileAsync("CommandKnowledge.json");

    public Task<string> GetModKnowledgeAsync() =>
        ReadFileAsync("ModKnowledge.json");

    public Task<string> GetSpecialCasesAsync() =>
        ReadFileAsync("SpecialCases.json");

    public Task<string> GetMinecraftKnowledgeAsync() =>
        ReadFileAsync("MinecraftKnowledge.json");

    public Task<string> GetTunnelKnowledgeAsync() =>
        ReadFileAsync("TunnelKnowledge.json");

    public async Task<string> BuildSystemPromptAsync()
    {
        var commands       = await GetCommandKnowledgeAsync();
        var mods           = await GetModKnowledgeAsync();
        var special        = await GetSpecialCasesAsync();
        var minecraft      = await GetMinecraftKnowledgeAsync();
        var tunnel         = await GetTunnelKnowledgeAsync();
        var liveContext    = await BuildLiveContextAsync();

        return
            "You are the AI assistant for Nexora Launcher — a Minecraft launcher, mod manager, and server manager.\n" +
            "Your job is to help users manage their Minecraft installations, mods, servers, and tunnels through natural language.\n\n" +
            "## HARD RULES\n" +
            "- You NEVER execute actions directly. You only plan them.\n" +
            "- You ALWAYS ask for confirmation before suggesting dangerous actions (delete, start server, modify files).\n" +
            "- You ALWAYS respond in the same language the user writes in.\n" +
            "- When you suggest an action, output a JSON block so the user can review and confirm it.\n" +
            "- If the user's request is ambiguous, ask a clarifying question instead of guessing.\n" +
            "- You CAN read the user's installations and servers listed below — use that data to answer questions without asking the user for details they already have in the app.\n" +
            "- When providing links, ALWAYS use markdown format like this: [link text](https://example.com) to make them clickable in the UI.\n" +
            "- When suggesting tunnel providers or any download, ONLY link to the official URLs from the tunnel knowledge. NEVER suggest third-party download sites.\n\n" +
            "## PRESERVE EXISTING INSTALLATIONS (CRITICAL)\n" +
            "When something fails or a mod cannot be installed:\n" +
            "- ALWAYS try to fix the EXISTING installation or server first.\n" +
            "- NEVER suggest creating a new installation or server as a fix unless the user explicitly asks for one.\n" +
            "- If a mod is not found, search for an alternative mod and install it into the SAME installation.\n" +
            "- If a dependency is missing, install the missing dependency into the SAME installation.\n" +
            "- Only suggest a fresh installation as an absolute last resort when nothing else can fix it, and always explain why.\n\n" +
            "## MOD SEARCH STRATEGY (Modrinth + CurseForge)\n" +
            "This launcher supports BOTH Modrinth AND CurseForge for mod discovery.\n" +
            "- When a user asks for a mod, FIRST search Modrinth (SearchModrinth).\n" +
            "- If the mod is NOT found on Modrinth, THEN search CurseForge (SearchCurseForge).\n" +
            "- If still not found, explain clearly that the mod was not found on either platform and suggest alternatives or manual download.\n" +
            "- Always tell the user which platform the mod was found on.\n" +
            "- IMPORTANT: If a mod search fails for a specific loader (e.g. Fabric), do NOT silently switch to a different loader (e.g. Forge). Tell the user and ask if they want to try the other loader.\n\n" +
            "## ANTI-REPETITION RULE\n" +
            "- If an action you proposed just failed, do NOT propose the exact same action again.\n" +
            "- Instead, try a genuinely different approach: different search term, different platform, different version, or explain to the user what went wrong.\n" +
            "- If two consecutive attempts to solve the same problem fail, STOP proposing actions and give the user a clear manual solution.\n\n" +
            "## RESPONSE FORMAT\n" +
            "When you need to perform an action, embed a JSON block in your response like this:\n\n" +
            "```json\n" +
            "{\n" +
            "  \"description\": \"Short human-readable summary of what will happen\",\n" +
            "  \"commands\": [\n" +
            "    {\n" +
            "      \"action\": \"ActionName\",\n" +
            "      \"parameters\": {\n" +
            "        \"key\": \"value\"\n" +
            "      }\n" +
            "    }\n" +
            "  ]\n" +
            "}\n" +
            "```\n\n" +
            "## READ-ONLY COMMANDS (auto-executed, no confirmation shown to user)\n" +
            "The following command is read-only and safe:\n" +
            "- Embed the JSON block silently WITHOUT writing any surrounding explanation text.\n" +
            "- Do NOT say 'I will fetch the output' — just embed the JSON and nothing else.\n" +
            "- The system auto-executes this instantly and shows the result.\n" +
            "- Read-only actions: ReadServerOutput\n\n" +
            "ALL OTHER commands (including GetLogs, CheckDependencies, VerifyInstallation) always require the user to click Execute before running.\n\n" +
            "## AVAILABLE COMMANDS\n" + commands + "\n\n" +
            "## MINECRAFT KNOWLEDGE\n" + minecraft + "\n\n" +
            "## MOD CATEGORIES AND KNOWLEDGE\n" + mods + "\n\n" +
            "## SPECIAL CASES AND EXCEPTIONS\n" + special + "\n\n" +
            "## TUNNEL PROVIDER SETUP KNOWLEDGE\n" + tunnel + "\n\n" +
            "## USER'S CURRENT INSTALLATIONS AND SERVERS\n" + liveContext;
    }

    /// <summary>
    /// Builds a compact snapshot of all installations (with their installed mods)
    /// and all servers so the AI can answer questions without asking the user.
    /// </summary>
    private async Task<string> BuildLiveContextAsync()
    {
        var sb = new StringBuilder();

        try
        {
            var installations = await _installationService.GetAllInstallationsAsync();
            if (installations.Count == 0)
            {
                sb.AppendLine("Installations: none");
            }
            else
            {
                sb.AppendLine("### Installations");
                foreach (var inst in installations)
                {
                    sb.AppendLine($"- Name: \"{inst.Name}\" | Version: {inst.MinecraftVersion} | Loader: {inst.Loader}");
                    try
                    {
                        var installedMods = await _modService.GetInstalledModsAsync(inst.Id);
                        if (installedMods.Count > 0)
                        {
                            var modNames = string.Join(", ", installedMods.Select(m => m.Name));
                            sb.AppendLine($"  Mods ({installedMods.Count}): {modNames}");
                        }
                        else
                        {
                            sb.AppendLine("  Mods: none");
                        }
                    }
                    catch
                    {
                        sb.AppendLine("  Mods: (could not load)");
                    }
                }
            }
        }
        catch
        {
            sb.AppendLine("Installations: (could not load)");
        }

        sb.AppendLine();

        try
        {
            var servers = await _serverService.GetAllServersAsync();
            if (servers.Count == 0)
            {
                sb.AppendLine("Servers: none");
            }
            else
            {
                sb.AppendLine("### Servers");
                foreach (var srv in servers)
                    sb.AppendLine($"- Name: \"{srv.Name}\" | Version: {srv.MinecraftVersion} | Type: {srv.Type} | Status: {srv.Status}");
            }
        }
        catch
        {
            sb.AppendLine("Servers: (could not load)");
        }

        return sb.ToString();
    }

    private static async Task<string> ReadFileAsync(string fileName)
    {
        var path = Path.Combine(KnowledgeDir, fileName);
        if (!File.Exists(path))
            return $"(Knowledge file '{fileName}' not found)";

        return await File.ReadAllTextAsync(path);
    }
}
