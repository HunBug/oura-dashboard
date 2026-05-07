using System.Reflection;

namespace OuraDashboard.Web.Services;

/// <summary>
/// Build-time metadata surfaced in the UI footer.
/// Version comes from AssemblyInformationalVersion (set in Directory.Build.props).
/// BuildTime is the DLL's last-write timestamp — a reliable proxy for compile time.
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly _assembly = typeof(BuildInfo).Assembly;

    public static readonly string Version =
        (_assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                  ?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];  // strip git-hash suffix that dotnet appends

    public static readonly DateTime BuildTime =
        File.Exists(_assembly.Location)
            ? File.GetLastWriteTimeUtc(_assembly.Location)
            : DateTime.UtcNow;
}
