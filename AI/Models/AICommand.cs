namespace MinecraftControlHub.AI.Models;

public class AICommand
{
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class AICommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class AICommandBatch
{
    public List<AICommand> Commands { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public class AICommandBatchResult
{
    public bool AllSucceeded => Results.All(r => r.Success);
    public List<AICommandResult> Results { get; set; } = new();

    public string Summary =>
        AllSucceeded
            ? $"All {Results.Count} action(s) completed successfully."
            : $"{Results.Count(r => !r.Success)} of {Results.Count} action(s) failed.";
}
