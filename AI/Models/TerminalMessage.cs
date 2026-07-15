using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MinecraftControlHub.AI.Models;

public enum TerminalMessageType
{
    User,
    AI,
    ActionPlan,
    ExecutionResult
}

public class TerminalMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _planSummary = string.Empty;
    private bool _isExpanded;
    private bool _isAwaitingConfirmation;
    private bool _isExecuting;
    private bool _isDone;

    public TerminalMessageType Type { get; init; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Compact summary shown when there are many commands (e.g. "Install 7 mod(s)").</summary>
    public string PlanSummary
    {
        get => _planSummary;
        set
        {
            if (_planSummary == value) return;
            _planSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPlanSummary));
        }
    }

    /// <summary>True when a compact summary is available (used to toggle collapsed view).</summary>
    public bool HasPlanSummary => !string.IsNullOrEmpty(_planSummary);

    /// <summary>Whether the detailed command list is expanded (visible).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public AICommandBatch? CommandBatch { get; init; }

    public bool IsSuccess { get; init; }

    public bool IsAwaitingConfirmation
    {
        get => _isAwaitingConfirmation;
        set
        {
            if (_isAwaitingConfirmation == value) return;
            _isAwaitingConfirmation = value;
            OnPropertyChanged();
        }
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set
        {
            if (_isExecuting == value) return;
            _isExecuting = value;
            OnPropertyChanged();
            // When execution finishes, mark as done so the indicator hides.
            if (!value) IsDone = true;
        }
    }

    /// <summary>True once execution has completed (success or failure), used to hide the Executing indicator.</summary>
    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (_isDone == value) return;
            _isDone = value;
            OnPropertyChanged();
        }
    }

    public bool IsUser            => Type == TerminalMessageType.User;
    public bool IsAI              => Type == TerminalMessageType.AI;
    public bool IsActionPlan      => Type == TerminalMessageType.ActionPlan;
    public bool IsExecutionResult => Type == TerminalMessageType.ExecutionResult;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler == null) return;

        // Always raise on the UI thread so WPF bindings update correctly
        // even when properties are set from a background thread.
        if (Application.Current?.Dispatcher.CheckAccess() == false)
            Application.Current.Dispatcher.Invoke(
                () => handler(this, new PropertyChangedEventArgs(name)));
        else
            handler(this, new PropertyChangedEventArgs(name));
    }
}
