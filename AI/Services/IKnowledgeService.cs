namespace MinecraftControlHub.AI.Services;

public interface IKnowledgeService
{
    Task<string> GetCommandKnowledgeAsync();
    Task<string> GetModKnowledgeAsync();
    Task<string> GetSpecialCasesAsync();
    Task<string> GetMinecraftKnowledgeAsync();
    Task<string> BuildSystemPromptAsync();
}
