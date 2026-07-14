using System;

namespace MinecraftControlHub.Core.Services;

public class ServerConsoleOutputEventArgs : EventArgs
{
    public Guid ServerId { get; }
    public string Line { get; }
    public bool IsError { get; }

    public ServerConsoleOutputEventArgs(Guid serverId, string line, bool isError)
    {
        ServerId = serverId;
        Line = line;
        IsError = isError;
    }
}
