using ReelForge.Application;

namespace ReelForge.App.Views.Generation;

public sealed class GenerationProviderChoice
{
    public GenerationProviderChoice(IVideoGenerationProvider provider)
    {
        Provider = provider;
    }

    public IVideoGenerationProvider Provider { get; }
    public string DisplayName => Provider.Capabilities.DisplayName;
}
