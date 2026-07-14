using System.Collections.ObjectModel;
using System.Text;
using MinecraftControlHub.AI.Models;
using MinecraftControlHub.AI.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class AiTerminalViewModel : ViewModelBase
{
    private readonly AITerminalService _terminalService;

    private string _inputText = string.Empty;
    private bool _isBusy;
    private CancellationTokenSource? _cts;

    private readonly List<ChatMessage> _conversationHistory = new();

    // AI chaining: when an action fails, feed errors back to the AI for auto-fix.
    private int _chainDepth;
    private const int MaxChainDepth = 2;

    // Tracks signatures of actions that already failed during chaining,
    // so the AI never retries the exact same action (e.g. same mod download).
    private readonly HashSet<string> _failedActionSignatures = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<TerminalMessage> Messages { get; } = new();

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(CanSend));
        }
    }

    public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    public bool IsConfigured => _terminalService.IsConfigured;

    public AiTerminalViewModel(AITerminalService terminalService)
    {
        _terminalService = terminalService;
    }

    public void NotifyInputChanged() => OnPropertyChanged(nameof(CanSend));

    public async Task ReloadConfigurationAsync()
    {
        await _terminalService.ReloadConfigurationAsync();
        OnPropertyChanged(nameof(IsConfigured));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public send / error-inject entry points
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy) return;

        InputText    = string.Empty;
        _chainDepth = 0; // reset chain depth on new user message
        _failedActionSignatures.Clear(); // reset failed-action dedup tracker
        await RunQueryAsync(text, displayText: text);
    }

    /// <summary>
    /// Injects an error context into the AI conversation — used by the
    /// "Fix with AI" button in the server terminal and launch error overlays.
    /// </summary>
    public async Task AskAiAboutErrorAsync(string errorContext)
    {
        var prompt =
            "The previous actions partially failed. Here are the execution results:\n\n" +
            $"{errorContext}\n\n" +
            "Analyse what failed and propose fixes on the EXISTING installation/server:\n" +
            "- CRITICAL: Do NOT create a new installation or server. Always fix the existing one.\n" +
            "- If a mod was not found for a specific version or loader, search for an alternative mod with a similar purpose " +
            "(e.g. if 'Man from the Fog' is not available for Fabric 1.20.1, search for other horror mods that ARE available).\n" +
            "- Install the alternative into the SAME installation that was used before (use the exact same targetName).\n" +
            "- Try the other platform: if Modrinth failed, try CurseForge, and vice versa.\n" +
            "- Try a different search term or a compatible version.\n" +
            "- Do NOT retry the exact same action that already failed.\n" +
            "- If no alternative exists, explain clearly to the user what they can do manually.";

        var displayText = "Fix with AI: " +
            (errorContext.Length > 200 ? errorContext[..200] + "\u2026" : errorContext);

        _chainDepth = 0;
        _failedActionSignatures.Clear();
        await RunQueryAsync(prompt, displayText);
    }

    // Read-only actions that never change state — skip confirmation for these.
    private static readonly HashSet<string> AutoExecuteActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "readserveroutput"
    };

    private static bool IsAutoExecuteBatch(AICommandBatch batch)
        => batch.Commands.Count > 0
        && batch.Commands.All(c => AutoExecuteActions.Contains(c.Action));

    // ─────────────────────────────────────────────────────────────────────────
    // Core streaming pipeline — shared by both entry points
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunQueryAsync(string prompt, string displayText)
    {
        if (IsBusy) return;

        IsBusy = true;
        _cts   = new CancellationTokenSource();

        AddMessage(new TerminalMessage { Type = TerminalMessageType.User, Text = displayText });

        var aiMessage = new TerminalMessage { Type = TerminalMessageType.AI, Text = string.Empty };
        AddMessage(aiMessage);

        try
        {
            if (!IsConfigured)
            {
                aiMessage.Text = "No AI API key configured. Go to Settings → AI Terminal and enter your API key.";
                _conversationHistory.Add(new ChatMessage("user",      prompt));
                _conversationHistory.Add(new ChatMessage("assistant", aiMessage.Text));
                return;
            }

            var sb = new StringBuilder();
            await foreach (var chunk in _terminalService.StreamQueryAsync(
                prompt, _conversationHistory, _cts.Token))
            {
                sb.Append(chunk);
                // Hide incomplete JSON blocks during streaming so the user
                // never sees raw JSON while the AI is still generating it.
                aiMessage.Text = AITerminalService.StripStreamingJson(sb.ToString());
            }

            var fullText = sb.ToString();
            _conversationHistory.Add(new ChatMessage("user",      prompt));
            _conversationHistory.Add(new ChatMessage("assistant", fullText));

            var parsed = AITerminalService.ParseActionPlan(fullText);
            if (parsed != null)
            {
                var textWithoutJson = AITerminalService.ExtractTextWithoutJson(fullText);

                // Pre-validate mods in a loop until the plan is clean or max attempts reached.
                const int MaxValidationAttempts = 3;
                for (var attempt = 0; attempt < MaxValidationAttempts; attempt++)
                {
                    var modFailures = await _terminalService.ValidateModsAsync(parsed);
                    if (modFailures.Count == 0)
                        break; // All mods are valid

                    // Some mods don't exist — ask AI to propose alternatives
                    var failureList = string.Join("\n", modFailures.Select(f =>
                        $"- \"{f.ModName}\" for {f.MinecraftVersion} ({f.Loader}) on target \"{f.TargetName}\""));

                    var correctionPrompt =
                        "Before executing, I validated the mods in your plan. The following mods DO NOT EXIST " +
                        "for the specified Minecraft version and loader on either Modrinth or CurseForge:\n\n" +
                        failureList + "\n\n" +
                        "Please revise your plan:\n" +
                        "- Replace each unavailable mod with a similar alternative that DOES exist for that version+loader.\n" +
                        "- Only propose mods that are actually available. Verify mentally that each mod exists before including it.\n" +
                        "- Keep the rest of the plan the same (installations, servers, other commands).\n" +
                        "- Output a new complete action plan with the corrected mods.";

                    // Add the correction to conversation and re-query
                    _conversationHistory.Add(new ChatMessage("user", prompt));
                    _conversationHistory.Add(new ChatMessage("assistant", fullText));
                    _conversationHistory.Add(new ChatMessage("system", correctionPrompt));

                    // Clear the current AI message and re-query
                    aiMessage.Text = string.Empty;
                    sb.Clear();
                    await foreach (var chunk in _terminalService.StreamQueryAsync(
                        correctionPrompt, _conversationHistory, _cts.Token))
                    {
                        sb.Append(chunk);
                        aiMessage.Text = AITerminalService.StripStreamingJson(sb.ToString());
                    }

                    fullText = sb.ToString();
                    _conversationHistory.Add(new ChatMessage("assistant", fullText));
                    parsed = AITerminalService.ParseActionPlan(fullText);
                    if (parsed == null) return; // AI didn't produce a valid plan
                    textWithoutJson = AITerminalService.ExtractTextWithoutJson(fullText);
                }

                if (IsAutoExecuteBatch(parsed))
                {
                    // Read-only batch — hide the AI bubble if it only contained JSON
                    if (!string.IsNullOrWhiteSpace(textWithoutJson))
                        aiMessage.Text = textWithoutJson;
                    else
                        System.Windows.Application.Current.Dispatcher.Invoke(
                            () => Messages.Remove(aiMessage));

                    var planMsg = new TerminalMessage
                    {
                        Type         = TerminalMessageType.ActionPlan,
                        Text         = FormatPlan(parsed),
                        CommandBatch = parsed
                    };
                    AddMessage(planMsg);
                    await ExecuteActionAsync(planMsg, setBusy: false);
                }
                else
                {
                    aiMessage.Text = textWithoutJson;
                    AddMessage(new TerminalMessage
                    {
                        Type                   = TerminalMessageType.ActionPlan,
                        Text                   = FormatPlan(parsed),
                        CommandBatch           = parsed,
                        IsAwaitingConfirmation = true
                    });
                }
            }
        }
        catch (OperationCanceledException) { aiMessage.Text += "\n\n[Cancelled]"; }
        catch (Exception ex)              { aiMessage.Text  = $"Error: {ex.Message}"; }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Action plan execution / cancellation
    // ─────────────────────────────────────────────────────────────────────────

    public async Task ExecuteActionAsync(TerminalMessage planMessage)
        => await ExecuteActionAsync(planMessage, setBusy: true);

    private async Task ExecuteActionAsync(TerminalMessage planMessage, bool setBusy)
    {
        if (planMessage.CommandBatch == null) return;
        if (setBusy && IsBusy) return;
    
        planMessage.IsAwaitingConfirmation = false;
        planMessage.IsExecuting            = true;
        if (setBusy) IsBusy = true;
    
        try
        {
            var result = await _terminalService.ExecuteAsync(planMessage.CommandBatch);
    
            var summary = new StringBuilder();
            foreach (var r in result.Results)
                summary.AppendLine(r.Success ? $"\u2713 {r.Message}" : $"\u2717 {r.Message}");
    
            AddMessage(new TerminalMessage
            {
                Type      = TerminalMessageType.ExecutionResult,
                Text      = summary.ToString().Trim(),
                IsSuccess = result.AllSucceeded
            });
    
            _conversationHistory.Add(new ChatMessage(
                "system",
                $"Execution result: {result.Summary}\n{summary}"));
    
            // Record failed action signatures so the AI never retries the same thing.
            foreach (var r in result.Results.Where(r => !r.Success))
            {
                // Build a rough signature from the command that produced this failure.
                // The result itself carries the message; we store it as-is for dedup.
                _failedActionSignatures.Add(r.Message);
            }

            // ── AI Chaining: on failure, ask the AI to analyse and propose a DIFFERENT fix ──
            if (!result.AllSucceeded && _chainDepth < MaxChainDepth)
            {
                var failures = result.Results
                    .Where(r => !r.Success)
                    .Select(r => r.Message);
                var errorText = string.Join("\n", failures);

                var previousAttempts = _failedActionSignatures.Count > 0
                    ? "\n\nIMPORTANT — these actions have ALREADY been tried and FAILED, do NOT propose them again:\n"
                      + string.Join("\n", _failedActionSignatures.Select(s => $"  - {s}"))
                    : string.Empty;

                _chainDepth++;
                await RunQueryAsync(
                    $"The previous action failed. Here are the errors:\n\n{errorText}\n\n" +
                    "Analyse why it failed and propose a DIFFERENT fix on the EXISTING installation/server.\n\n" +
                    "CRITICAL RULES:\n" +
                    "- Do NOT create a new installation or server. Fix the existing one.\n" +
                    "- Do NOT retry the exact same action that just failed. If installing mod 'X' failed, " +
                    "do NOT try installing 'X' again with the same parameters.\n" +
                    "- If a mod was not found, try: a shorter search term, the other platform " +
                    "(CurseForge if Modrinth failed, or vice versa), a different compatible version, " +
                    "or an alternative mod with a similar purpose. Install it into the SAME installation.\n" +
                    "- If the same type of action keeps failing, STOP proposing actions and instead " +
                    "explain to the user what went wrong and what they can do manually.\n" +
                    "- Maximum chain attempts remaining: " + (MaxChainDepth - _chainDepth) + ".\n" +
                    previousAttempts,
                    $"\u26a1 Analyzing failure (attempt {_chainDepth}/{MaxChainDepth})\u2026");
            }
        }
        catch (Exception ex)
        {
            AddMessage(new TerminalMessage
            {
                Type      = TerminalMessageType.ExecutionResult,
                Text      = $"Execution failed: {ex.Message}",
                IsSuccess = false
            });
        }
        finally
        {
            planMessage.IsExecuting = false;
            if (setBusy) IsBusy = false;
        }
    }

    public void CancelAction(TerminalMessage planMessage)
    {
        planMessage.IsAwaitingConfirmation = false;
        AddMessage(new TerminalMessage
        {
            Type      = TerminalMessageType.ExecutionResult,
            Text      = "Action cancelled.",
            IsSuccess = false
        });
        _conversationHistory.Add(new ChatMessage("system", "The user cancelled the proposed action."));
    }

    public void Cancel() => _cts?.Cancel();

    public void ClearHistory()
    {
        Messages.Clear();
        _conversationHistory.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void AddMessage(TerminalMessage msg)
        => System.Windows.Application.Current.Dispatcher.Invoke(() => Messages.Add(msg));

    private static string FormatPlan(AICommandBatch batch)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(batch.Description))
            sb.AppendLine(batch.Description);
        sb.AppendLine();

        for (var i = 0; i < batch.Commands.Count; i++)
        {
            var cmd = batch.Commands[i];
            sb.Append($"  {i + 1}. {cmd.Action}");
            if (cmd.Parameters.Count > 0)
            {
                var paramList = string.Join(", ",
                    cmd.Parameters.Select(p => $"{p.Key}: {p.Value}"));
                sb.Append($" ({paramList})");
            }
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
