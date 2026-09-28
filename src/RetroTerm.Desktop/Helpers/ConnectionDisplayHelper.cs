using System;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Desktop.Helpers;

/// <summary>
/// Static helpers for connection display formatting.
/// </summary>
public static class ConnectionDisplayHelper
{
    /// <summary>
    /// Returns a short protocol badge label (TEL, SSH, SER, GW, SIM).
    /// </summary>
    public static string GetProtocolBadge(string protocol)
    {
        return ConnectionFactory.ParseProtocolType(protocol) switch
        {
            ConnectionFactory.ProtocolType.SSH => "SSH",
            ConnectionFactory.ProtocolType.Serial => "SER",
            ConnectionFactory.ProtocolType.Gateway => "GW",
            ConnectionFactory.ProtocolType.OpcomSimulator => "SIM",
            ConnectionFactory.ProtocolType.TN3270 => "3270",
            _ => "TEL"
        };
    }

    /// <summary>
    /// Returns the hex color string for a protocol badge.
    /// </summary>
    public static string GetProtocolBadgeColorHex(string protocol)
    {
        return ConnectionFactory.ParseProtocolType(protocol) switch
        {
            ConnectionFactory.ProtocolType.SSH => "#569CD6",
            ConnectionFactory.ProtocolType.Serial => "#FF8C00",
            ConnectionFactory.ProtocolType.Gateway => "#C586C0",
            ConnectionFactory.ProtocolType.OpcomSimulator => "#B5CEA8",
            _ => "#4EC9B0"
        };
    }

    /// <summary>
    /// Returns a detail string like "myhost:23 · 5m ago" for a connection.
    /// </summary>
    public static string GetConnectionDetail(HostConfiguration config)
    {
        string address = config.ToConnectionParameters().DisplayName;
        var lastUsed = GetRelativeTime(config.LastUsedAt);
        return $"{address} \u00b7 {lastUsed}";
    }

    /// <summary>
    /// Returns a human-readable relative time string (e.g., "5m ago", "3h ago").
    /// </summary>
    public static string GetRelativeTime(DateTime dateTime)
    {
        var utcNow = DateTime.UtcNow;
        var utcDate = dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        var diff = utcNow - utcDate;

        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return "Never";
    }
}
