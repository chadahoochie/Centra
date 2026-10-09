using System;
using System.Collections.Generic;
using System.Linq;
using Centra.ControlPlane.Topology;

namespace Centra.Sample.ControlPlane.Simulation;

public static class AsciiDashboardRenderer
{
    public static void Render(
        string role,
        string leaderEndpoint,
        IReadOnlyCollection<ClientNodeInfo> nodes)
    {
        var clusters = nodes
            .Select(static n => string.IsNullOrWhiteSpace(n.ClusterId) ? "default" : n.ClusterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                 CENTRA CONTROL PLANE - MULTI-CLUSTER DASHBOARD                 ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"║  Role: [{role.PadRight(10)}]  │  Active Clusters: [{clusters.Count}]  │  Connected Nodes: [{nodes.Count.ToString().PadRight(2)}]     ║");
        Console.WriteLine($"║  Leader: [{leaderEndpoint.PadRight(24)}]  │  Cluster Status: [HEALTHY]           ║");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  CLUSTER ID       │ APP ID              │ INSTANCE ID      │ STATUS  │ HEARTBEAT║");
        Console.WriteLine("╟───────────────────┼─────────────────────┼──────────────────┼─────────┼──────────╢");
        Console.ResetColor();

        foreach (var node in nodes.OrderBy(static n => n.ClusterId).ThenBy(static n => n.AppId).ThenBy(static n => n.InstanceId))
        {
            var clusterStr = (string.IsNullOrWhiteSpace(node.ClusterId) ? "default" : node.ClusterId).PadRight(17);
            var appStr = node.AppId.PadRight(19);
            var instStr = node.InstanceId.PadRight(16);
            var statusStr = node.Status.PadRight(7);
            var hbStr = node.LastHeartbeatUtc.ToString("HH:mm:ss");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"║  {clusterStr}│ ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{appStr}│ {instStr}│ ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{statusStr} ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"│ {hbStr} ║");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
}
