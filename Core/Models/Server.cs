using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinecraftControlHub.Core.Models;

public enum ServerType
{
    Vanilla,
    Paper,
    Purpur,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public enum ServerStatus
{
    Stopped,
    Running,
    Starting,
    Provisioning,
    Stopping
}

public class Server : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private string _minecraftVersion = string.Empty;
    private ServerType _type;
    private ServerStatus _status = ServerStatus.Stopped;
    private int _maxMemoryMB = 2048;
    private int _minMemoryMB = 1024;
    private string? _serverDirectory;
    private string? _javaPath;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime? _lastStarted;
    private int? _port = 25565;
    private bool? _tunnelEnabled;
    private string? _propertiesPath;
    private bool _allowOnlineMode = true;
    private int _maxPlayers = 10;
    private int _currentPlayers;
    private double _loadPercent;
    private string _gamemode = "survival";
    private string _difficulty = "normal";
    private bool _allowCheats;
    private bool _whiteListEnabled;
    private string _motd = string.Empty;
    private string? _jarFileName;
    private string? _customJvmArgs;
    private string _opPlayers = string.Empty;
    private string _whitelistedPlayers = string.Empty;
    private int _viewDistance = 10;
    private int _simulationDistance = 10;
    private bool _pvpEnabled = true;
    private int _spawnProtection = 16;
    private bool _allowNether = true;
    private bool _allowFlight = false;
    private bool _hardcoreMode = false;
    private bool _forceGamemode = false;
    private int _opPermissionLevel = 4;
    private int _networkCompressionThreshold = 256;
    private int _playerIdleTimeout = 0;
    private bool _enableCommandBlocks = false;
    private bool _spawnAnimals = true;
    private bool _spawnMonsters = true;
    private bool _spawnNpcs = true;

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string MinecraftVersion
    {
        get => _minecraftVersion;
        set => SetProperty(ref _minecraftVersion, value);
    }

    public ServerType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public ServerStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public int MaxMemoryMB
    {
        get => _maxMemoryMB;
        set => SetProperty(ref _maxMemoryMB, value);
    }

    public int MinMemoryMB
    {
        get => _minMemoryMB;
        set => SetProperty(ref _minMemoryMB, value);
    }

    public string? ServerDirectory
    {
        get => _serverDirectory;
        set => SetProperty(ref _serverDirectory, value);
    }

    public string? JavaPath
    {
        get => _javaPath;
        set => SetProperty(ref _javaPath, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public DateTime? LastStarted
    {
        get => _lastStarted;
        set => SetProperty(ref _lastStarted, value);
    }

    public int? Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public bool? TunnelEnabled
    {
        get => _tunnelEnabled;
        set => SetProperty(ref _tunnelEnabled, value);
    }

    public string? PropertiesPath
    {
        get => _propertiesPath;
        set => SetProperty(ref _propertiesPath, value);
    }

    public bool AllowOnlineMode
    {
        get => _allowOnlineMode;
        set => SetProperty(ref _allowOnlineMode, value);
    }

    public int MaxPlayers
    {
        get => _maxPlayers;
        set => SetProperty(ref _maxPlayers, value);
    }

    public int CurrentPlayers
    {
        get => _currentPlayers;
        set => SetProperty(ref _currentPlayers, value);
    }

    public double LoadPercent
    {
        get => _loadPercent;
        set => SetProperty(ref _loadPercent, value);
    }

    public string Gamemode
    {
        get => _gamemode;
        set => SetProperty(ref _gamemode, value);
    }

    public string Difficulty
    {
        get => _difficulty;
        set => SetProperty(ref _difficulty, value);
    }

    public bool AllowCheats
    {
        get => _allowCheats;
        set => SetProperty(ref _allowCheats, value);
    }

    public bool WhiteListEnabled
    {
        get => _whiteListEnabled;
        set => SetProperty(ref _whiteListEnabled, value);
    }

    public string Motd
    {
        get => _motd;
        set => SetProperty(ref _motd, value);
    }

    public string? JarFileName
    {
        get => _jarFileName;
        set => SetProperty(ref _jarFileName, value);
    }

    public string? CustomJvmArgs
    {
        get => _customJvmArgs;
        set => SetProperty(ref _customJvmArgs, value);
    }

    public string OpPlayers
    {
        get => _opPlayers;
        set => SetProperty(ref _opPlayers, value);
    }

    public string WhitelistedPlayers
    {
        get => _whitelistedPlayers;
        set => SetProperty(ref _whitelistedPlayers, value);
    }

    public int ViewDistance
    {
        get => _viewDistance;
        set => SetProperty(ref _viewDistance, value);
    }

    public int SimulationDistance
    {
        get => _simulationDistance;
        set => SetProperty(ref _simulationDistance, value);
    }

    public bool PvpEnabled
    {
        get => _pvpEnabled;
        set => SetProperty(ref _pvpEnabled, value);
    }

    public int SpawnProtection
    {
        get => _spawnProtection;
        set => SetProperty(ref _spawnProtection, value);
    }

    public bool AllowNether
    {
        get => _allowNether;
        set => SetProperty(ref _allowNether, value);
    }

    public bool AllowFlight
    {
        get => _allowFlight;
        set => SetProperty(ref _allowFlight, value);
    }

    public bool HardcoreMode
    {
        get => _hardcoreMode;
        set => SetProperty(ref _hardcoreMode, value);
    }

    public bool ForceGamemode
    {
        get => _forceGamemode;
        set => SetProperty(ref _forceGamemode, value);
    }

    public int OpPermissionLevel
    {
        get => _opPermissionLevel;
        set => SetProperty(ref _opPermissionLevel, value);
    }

    public int NetworkCompressionThreshold
    {
        get => _networkCompressionThreshold;
        set => SetProperty(ref _networkCompressionThreshold, value);
    }

    public int PlayerIdleTimeout
    {
        get => _playerIdleTimeout;
        set => SetProperty(ref _playerIdleTimeout, value);
    }

    public bool EnableCommandBlocks
    {
        get => _enableCommandBlocks;
        set => SetProperty(ref _enableCommandBlocks, value);
    }

    public bool SpawnAnimals
    {
        get => _spawnAnimals;
        set => SetProperty(ref _spawnAnimals, value);
    }

    public bool SpawnMonsters
    {
        get => _spawnMonsters;
        set => SetProperty(ref _spawnMonsters, value);
    }

    public bool SpawnNpcs
    {
        get => _spawnNpcs;
        set => SetProperty(ref _spawnNpcs, value);
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
