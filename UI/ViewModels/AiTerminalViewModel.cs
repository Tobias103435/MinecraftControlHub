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
        await RunQueryAsync(text, displayText: text);
    }

    /// <summary>
    /// Injects an error context into the AI conversation — used by the
    /// "Fix with AI" button in the server terminal and launch error overlays.
    /// </summary>
    public async Task AskAiAboutErrorAsync(string errorContext)
    {
        var prompt      = $"Something went wrong. Here is the error/log:\n\n{errorContext}\n\nWhat is causing this and how do I fix it? If you can fix it with an action, propose the action plan.";
        var displayText = $"Fix with AI: {(errorContext.Length > 200 ? errorContext[..200] + "\u2026" : errorContext)}";
        _chainDepth = 0;
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
    
            // ── AI Chaining: on failure, ask the AI to analyse and propose a fix ──
            if (!result.AllSucceeded && _chainDepth < MaxChainDepth)
            {
                var failures = result.Results
                    .Where(r => !r.Success)
                    .Select(r => r.Message);
                var errorText = string.Join("\n", failures);
    
                _chainDepth++;
                await RunQueryAsync(
                    $"The previous action failed. Here are the errors:\n\n{errorText}\n\n" +
                    "Analyse why it failed and propose a fix. For example: if a mod was not found " +
                    "for a specific version, try a different search term or compatible version. " +
                    "If a dependency is missing, install it. If the action cannot be fixed, explain " +
                    "why clearly instead of proposing another action.",
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
