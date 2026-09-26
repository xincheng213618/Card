using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
                using var artStream = typeof(GeneralArt).Assembly.GetManifestResourceStream("CardGame.GeneralArtCatalog.json")
                    ?? throw new InvalidDataException("General artwork catalog did not ship.");
                using var artCatalog = JsonDocument.Parse(artStream);
                var skinFiles = artCatalog.RootElement.GetProperty("entries").EnumerateArray()
                    .SelectMany(entry => entry.GetProperty("skins").EnumerateArray())
                    .Select(skin => skin.GetProperty("localPath").GetString()!)
                    .Where(path => path.StartsWith("src/CardGame.Wpf/Assets/Skins/", StringComparison.Ordinal))
                    .Distinct().ToArray();
                foreach (var localPath in skinFiles)
                {
                    var path = Path.Combine(AppContext.BaseDirectory, localPath["src/CardGame.Wpf/".Length..]);
                    using var reader = new BinaryReader(File.OpenRead(path));
                    if (!reader.ReadBytes(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                        throw new InvalidDataException($"Missing or invalid skin: {localPath}");
                }
                var optionalSkin = GeneralArt.GetSkins("classic:ma-dai").First(skin => skin.LocalPath.Contains("/Skins/", StringComparison.Ordinal));
                if (GeneralArt.GetPortrait("classic:ma-dai", optionalSkin.Id) is not ImageBrush { ImageSource: BitmapSource { PixelWidth: > 100 } })
                    throw new InvalidDataException("External skin artwork did not load from the package.");
                var sounds = Enum.GetValues<GameSound>();
                foreach (var asset in GameAudioCatalog.Assets)
                {
                    using var file = File.OpenRead(asset.FilePath);
                    if (file.Length != asset.DeliveredBytes || !Convert.ToHexString(SHA256.HashData(file)).Equals(asset.DeliveredSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Invalid official audio: {asset.Id}");
                }
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
                    OfficialAudioAssets = GameAudioCatalog.Assets.Count,
                    SkinAssets = skinFiles.Length,
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
