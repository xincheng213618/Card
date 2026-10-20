using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CardGame.Wpf.ViewModels;

public static class WushuangArt
{
    private static readonly Dictionary<string, ImageSource> Images = [];
    public static ImageSource Get(string file)
    {
        if (Images.TryGetValue(file, out var image)) return image;
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/CardGame.Wpf;component/Assets/Wushuang/{file}", UriKind.Absolute));
        bitmap.Freeze(); return Images[file] = bitmap;
    }
}
