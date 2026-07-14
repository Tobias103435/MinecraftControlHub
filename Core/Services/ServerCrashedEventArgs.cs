using System;
using System.IO;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services
{
    /// <summary>
    /// Event args used when a server process crashes. Includes the Server reference
    /// and optional crash report content (if available).
    /// </summary>
    public class ServerCrashedEventArgs : EventArgs
    {
        public Server Server { get; }
        public string? CrashLogPath { get; }
        public string? CrashReport { get; }

        public ServerCrashedEventArgs(Server server, string? crashLogPath = null, string? crashReport = null)
        {
            Server = server;
            CrashLogPath = crashLogPath;
            CrashReport = crashReport;
        }
    }
}
