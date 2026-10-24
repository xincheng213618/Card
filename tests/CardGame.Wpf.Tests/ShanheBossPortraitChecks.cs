using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheBossPortraitChecks
{
    public static void PublishedS5BossAliasesDecodeAndRenderOnRealSeats(string output)
    {
        var checkedIds = new HashSet<string>(StringComparer.Ordinal);
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        // These source packages explicitly lack usable local art.
        // They must remain honest missing-art fallbacks until a separate asset batch.
        var missingArt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StandardShanheS5YuduPackage.YuduId] = "",
            [StandardShanheS5ZhangliangPackage.ZhangliangId] = "zhang_liang",
            [StandardShanheS5XuncanCaoshiPackage.XuncanId] = ""
        };
        var seenMissing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tier in new[] { 1, 2, 3 })
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            foreach (var node in definition.Nodes)
            foreach (var encounter in node.Encounters ?? [])
            {
                if (!encounter.Enemies.Any(e => e.GeneralId.StartsWith("shanhe:s5-", StringComparison.Ordinal))) continue;
                var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(Setup(definition, node, encounter));
                foreach (var member in encounter.Enemies.Where(e => e.GeneralId.StartsWith("shanhe:s5-", StringComparison.Ordinal)))
                {
                    var general = registry.Generals[member.GeneralId];
                    if (general.PortraitKey == "supporter") { placeholders.Add(general.Id); continue; }
                    if (missingArt.TryGetValue(general.Id, out var expectedMissingKey))
                    {
                        Program.Assert(general.PortraitKey == expectedMissingKey && !GeneralArt.HasPortrait(general.Id),
                            "Only the exact declared no-asset forms retain their missing-art fallback: " + general.Id);
                        seenMissing.Add(general.Id); continue;
                    }
                    if (!checkedIds.Add(general.Id)) continue;
                    // Old content portrait keys use underscores; the artwork catalogue
                    // uses hyphens. Dong Ji's declared Dong Bai portrait is the local OL asset.
                    var expectedKey = general.PortraitKey == "dong_bai" ? "ol-dong-bai" : general.PortraitKey.Replace('_', '-');
                    var expected = GeneralArt.GetSkin(expectedKey);
                    Program.Assert(expected is not null && GeneralArt.GetSkin(general.Id)?.LocalPath == expected.LocalPath &&
                        GeneralArt.GetPortrait(general.Id, decodeWidth: 96) is ImageBrush
                            { ImageSource: BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } } brush && HasPaintedPixels(brush),
                        "The actual published conditional registry general ID must decode and paint its own existing local portrait: " + general.Id);
                }
            }
        }
        Program.Assert(seenMissing.SetEquals(missingArt.Keys) && placeholders.Count > 0 && new[]
        {
            StandardShanheS5MaSiblingsPackage.MachaoId, StandardShanheS5MaSiblingsPackage.MayunluId,
            StandardShanheS5DongwuVeteransPackage.ChengpuId, StandardShanheS5DongwuVeteransPackage.HandangId,
            StandardShanheS5MoushiPackage.XunchenId, StandardShanheS5MoushiPackage.GuotuId,
            StandardShanheS5MoushiPackage.ShenpeiId, StandardShanheS5HuaxiongBasePackage.HuaxiongId,
            StandardShanheS5ZhangxiuPackage.ZhangxiuId, StandardShanheS5CaoWeiPioneersPackage.CaozhenId,
            StandardShanheS5CaoWeiPioneersPackage.CaocaoId, StandardShanheS5CaoWeiPioneersPackage.CaohongId,
            StandardShanheS5ZuociHuatuoPackage.ZuociId, StandardShanheS5ZuociHuatuoPackage.HuatuoId
        }.All(checkedIds.Contains),
            "The representative source generals have real portraits; only explicit supporter mappings and declared no-asset forms are excluded.");

        RenderActualMaSiblingsTable(output);
    }

    private static ShanheBattleSetup Setup(ShanheCampaignDefinition definition, ShanheNodeDefinition node, ShanheEncounterDefinition encounter) =>
        new(83, 1, node.Id, "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, encounter.Allies ?? [],
            ShanheCampaignContent.GetContentHash(definition))
        { DefaultGodFactionId = definition.DefaultGodFactionId, StartingResourcesExpireOnUse = definition.StartingResourcesExpireOnUse };

    private static bool HasPaintedPixels(Brush brush)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(brush, null, new Rect(0, 0, 32, 48));
        var bitmap = new RenderTargetBitmap(32, 48, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return HasPaintedPixels(bitmap);
    }

    private static bool HasPaintedPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) return true;
        return false;
    }

    private static void RenderActualMaSiblingsTable(string output)
    {
        var definition = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var node = definition.Nodes.Single(n => n.Id == "dagu");
        var encounter = node.Encounters!.Single(e => e.Id == "dagu:ma-siblings-s5");
        var setup = Setup(definition, node, encounter);
        var game = StandardShanheCampaignCatalog.CreateBattle(setup);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The published Ma Chao/Ma Yunlu recipe must start its actual three-seat battle.");
        using var model = new MainViewModel(false, 83, true, new MemorySaveStore(), useExpandedContent: true,
            shanheSaveStore: new MemoryShanheCampaignSaveStore())
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        typeof(MainViewModel).GetField("_shanheTableGame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, game);
        typeof(MainViewModel).GetField("_shanheTableSetup", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, setup);
        typeof(MainViewModel).GetMethod("ReplaceEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [game]);
        model.IsNewGameSetupOpen = false;
        var window = new MainWindow(model); window.ApplyTemplate();
        try
        {
            var surface = (FrameworkElement)window.Content;
            Program.Render(surface, 1440, 880, Path.Combine(output, "shanhe-s5-ma-siblings-portraits.png"));
            foreach (var id in new[] { StandardShanheS5MaSiblingsPackage.MachaoId, StandardShanheS5MaSiblingsPackage.MayunluId })
            {
                var seat = model.Seats.Single(s => s.GeneralId == id);
                var portrait = Program.Find<Rectangle>(surface).Single(r => r.Name == "SinglePortrait" && ReferenceEquals(r.DataContext, seat));
                Program.Assert(seat.HasPortrait && seat.Portrait.Image is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } &&
                    portrait is { ActualWidth: > 0, ActualHeight: > 0, Fill: ImageBrush { ImageSource: not null } } && portrait.Visibility == Visibility.Visible,
                    "The actual public enemy seat template must bind its source form's decoded portrait rather than transparent fallback: " + id);
                var crop = new RenderTargetBitmap((int)Math.Ceiling(portrait.ActualWidth), (int)Math.Ceiling(portrait.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                crop.Render(portrait);
                Program.Assert(HasPaintedPixels(crop), "The real laid-out enemy portrait must contain painted pixels: " + id);
                if (id == StandardShanheS5MaSiblingsPackage.MachaoId)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop));
                    using var file = File.Create(Path.Combine(output, "shanhe-s5-ma-chao-portrait.png")); encoder.Save(file);
                }
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }
}
