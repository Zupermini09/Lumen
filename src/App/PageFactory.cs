using Lumen.Core.Jellyfin;
using Lumen.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Lumen.App;

internal sealed class PageFactory(IServiceProvider services) : IPageFactory
{
    public PageViewModel Connect(string? error = null) => Create<ConnectViewModel>(error!);
    public PageViewModel Login(ServerInfo server) => Create<LoginViewModel>(server);
    public PageViewModel Home() => Create<HomeViewModel>();
    public PageViewModel Search() => Create<SearchViewModel>();
    public PageViewModel Favorites() => Create<FavoritesViewModel>();
    public PageViewModel Items(MediaItem parent) => Create<ItemsViewModel>(parent);
    public PageViewModel Player(MediaItem item) => Create<PlayerViewModel>(item);
    public PageViewModel Details(MediaItem item) => Create<ItemDetailsViewModel>(item);
    public PageViewModel Series(MediaItem item) => Create<SeriesDetailsViewModel>(item);
    public PageViewModel PlaybackTest() => Create<PlaybackTestViewModel>();
    public PageViewModel About() => Create<AboutViewModel>();

    // Constructor arguments not registered in DI (the item, the server...) are passed explicitly.
    private T Create<T>(params object[] arguments) where T : PageViewModel =>
        ActivatorUtilities.CreateInstance<T>(services, arguments.Where(a => a is not null).ToArray());
}
