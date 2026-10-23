namespace Lumen.UI.ViewModels;

public enum PageKey
{
    PlaybackTest,
    About,
}

public interface IPageFactory
{
    PageViewModel Create(PageKey key);
}
