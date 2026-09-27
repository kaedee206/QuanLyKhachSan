using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace QuanLyKhachSan.Helpers;

/// <summary>
/// A configuration provider that post-processes all values from an inner provider
/// and replaces ${VAR_NAME} placeholders with the corresponding environment variable.
/// This allows appsettings.json to reference env vars using ${...} syntax.
/// </summary>
public class EnvInterpolationConfigurationProvider : ConfigurationProvider
{
    private readonly IConfigurationProvider _inner;

    public EnvInterpolationConfigurationProvider(IConfigurationProvider inner)
    {
        _inner = inner;
    }

    public override void Load()
    {
        _inner.Load();
    }

    public override bool TryGet(string key, out string? value)
    {
        if (!_inner.TryGet(key, out value) || value == null)
            return false;

        // Replace all ${VAR_NAME} occurrences with the environment variable value
        value = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\$\{([^}]+)\}",
            match =>
            {
                var varName = match.Groups[1].Value;
                return Environment.GetEnvironmentVariable(varName) ?? match.Value;
            });

        return true;
    }

    public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        => _inner.GetChildKeys(earlierKeys, parentPath);
}

/// <summary>
/// Wraps an existing IConfigurationSource to inject the EnvInterpolationConfigurationProvider.
/// </summary>
public class EnvInterpolationConfigurationSource : IConfigurationSource
{
    private readonly IConfigurationSource _inner;

    public EnvInterpolationConfigurationSource(IConfigurationSource inner)
    {
        _inner = inner;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new EnvInterpolationConfigurationProvider(_inner.Build(builder));
}

/// <summary>
/// Extension method to wrap all existing sources with ${VAR} interpolation support.
/// Call this after all sources have been added to the builder.
/// </summary>
public static class EnvInterpolationConfigurationExtensions
{
    public static IConfigurationBuilder AddEnvInterpolation(this IConfigurationBuilder builder)
    {
        var sources = builder.Sources.ToList();
        builder.Sources.Clear();

        foreach (var source in sources)
        {
            if (source is EnvInterpolationConfigurationSource)
                builder.Sources.Add(source); // already wrapped
            else
                builder.Sources.Add(new EnvInterpolationConfigurationSource(source));
        }

        return builder;
    }
}
