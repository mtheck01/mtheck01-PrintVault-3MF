using System.Reflection;

namespace PrintVault.Core;

public static class AppVersion
{
    public static string Version =>
        typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
