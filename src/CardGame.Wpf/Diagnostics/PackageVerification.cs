using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Diagnostics;

/// <summary>Runs the shipped executable without showing a window or accessing player saves.</summary>
internal static class PackageVerification
{
    public static int Run(string reportPath)
    {
        try
        {
            reportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            using var viewModel = new MainViewModel(autoAdvance: false, seed: 721019,
                showSetup: true, saveStore: new VerificationSaveStore(), useExpandedContent: true);
            var window = new MainWindow(viewModel);
            try
            {
                if (GeneralArt.GetPortrait("cao-cao") is not ImageBrush { ImageSource: BitmapSource { PixelWidth: > 0 } })
                    throw new InvalidDataException("Embedded general artwork did not load.");
                var sounds = Enum.GetValues<GameSound>();
                foreach (var sound in sounds)
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", $"{sound.ToString().ToLowerInvariant()}.wav");
                    using var reader = new BinaryReader(File.OpenRead(path));
                    if (reader.BaseStream.Length <= 44 || new string(reader.ReadChars(4)) != "RIFF")
                        throw new InvalidDataException($"Invalid audio asset: {sound}");
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE")
                        throw new InvalidDataException($"Invalid WAVE asset: {sound}");
                }

                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1120, 740));
                root.Arrange(new Rect(0, 0, 1120, 740));
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var screenshot = Path.ChangeExtension(reportPath, ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(screenshot)) encoder.Save(output);

                File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                {
                    Success = true,
                    Runtime = RuntimeInformation.FrameworkDescription,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    BaseDirectory = AppContext.BaseDirectory,
                    AudioAssets = sounds.Length,
                    Screenshot = screenshot,
                    VisibleWindowOpened = false,
                    PlayerSavesAccessed = false
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            finally { window.Close(); }
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = exception.ToString() })); }
            catch (Exception) { /* The process exit code also reports failure when the report path is unwritable. */ }
            return 1;
        }
    }

    private sealed class VerificationSaveStore : IGameSaveStore
    {
        public DateTimeOffset? GetSavedAt(GameSaveSlot slot) => null;
        public GameSaveFile Read(GameSaveSlot slot) => throw new FileNotFoundException("No saves in package verification.");
        public void Write(GameSaveSlot slot, GameSaveFile save) { }
    }
}
