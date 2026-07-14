using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinecraftControlHub.Core.Models;

public enum PortProtocol
{
    TCP,
    UDP
}

public class PortInfo : INotifyPropertyChanged
{
    private int _port;
    private PortProtocol _protocol;
    private string _purpose = string.Empty;
    private bool _isEnabled;
    private bool _isCustom;
    private string _source = string.Empty; // "server.properties", "mod:Simple Voice Chat", etc.

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public PortProtocol Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public string Purpose
    {
        get => _purpose;
        set => SetProperty(ref _purpose, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsCustom
    {
        get => _isCustom;
        set => SetProperty(ref _isCustom, value);
    }

    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
