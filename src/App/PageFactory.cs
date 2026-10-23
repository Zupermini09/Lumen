using Lumen.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Lumen.App;

internal sealed class PageFactory(IServiceProvider services) : IPageFactory
{
    public PageViewModel Create(PageKey key) => key switch
    {
        PageKey.PlaybackTest => services.GetRequiredService<PlaybackTestViewModel>(),
        PageKey.About => services.GetRequiredService<AboutViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
