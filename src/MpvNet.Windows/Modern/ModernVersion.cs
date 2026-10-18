using System.Reflection;

namespace MpvNet.Windows.Modern;

public static class ModernVersion
{
    public static string Label { get; } = "V" + typeof(ModernVersion).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(attribute => attribute.Key == "ModernVersion").Value;
}
