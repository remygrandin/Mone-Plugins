using Mone.Contracts.Models;

namespace Mone.Plugins.Tests;

internal sealed class StubPluginContext(string pluginId, Dictionary<string, string> configuration) : IPluginContext
{
    public string PluginId => pluginId;
    public IReadOnlyDictionary<string, string> Configuration { get; } = configuration;
    public CancellationToken ShutdownToken => CancellationToken.None;
}
