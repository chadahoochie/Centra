using System;
using System.IO;

namespace Centra.ControlPlane.Dashboard;

public static class DashboardFileLocator
{
    public static string? FindDashboardRoot()
    {
        // 1. Direct check in AppContext.BaseDirectory
        var baseDir = AppContext.BaseDirectory;
        var direct1 = Path.Combine(baseDir, "wwwroot", "dashboard");
        if (Directory.Exists(direct1))
        {
            return direct1;
        }

        // 2. Direct check in current working directory
        var currentDir = Directory.GetCurrentDirectory();
        var direct2 = Path.Combine(currentDir, "wwwroot", "dashboard");
        if (Directory.Exists(direct2))
        {
            return direct2;
        }

        var direct3 = Path.Combine(currentDir, "src", "Centra.ControlPlane", "wwwroot", "dashboard");
        if (Directory.Exists(direct3))
        {
            return direct3;
        }

        // 3. Upward directory traversal from BaseDirectory
        var dirInfo = new DirectoryInfo(baseDir);
        while (dirInfo is not null)
        {
            var probeSrc = Path.Combine(dirInfo.FullName, "src", "Centra.ControlPlane", "wwwroot", "dashboard");
            if (Directory.Exists(probeSrc))
            {
                return probeSrc;
            }

            var probeRoot = Path.Combine(dirInfo.FullName, "wwwroot", "dashboard");
            if (Directory.Exists(probeRoot))
            {
                return probeRoot;
            }

            dirInfo = dirInfo.Parent;
        }

        // 4. Upward directory traversal from CurrentDirectory
        var curInfo = new DirectoryInfo(currentDir);
        while (curInfo is not null)
        {
            var probeSrc = Path.Combine(curInfo.FullName, "src", "Centra.ControlPlane", "wwwroot", "dashboard");
            if (Directory.Exists(probeSrc))
            {
                return probeSrc;
            }

            curInfo = curInfo.Parent;
        }

        return null;
    }
}
