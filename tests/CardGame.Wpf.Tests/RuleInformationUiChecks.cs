using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class RuleInformationUiChecks
{
    public static void PrivateFacesAndPublicDeclarations(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false, IsNewGameSetupOpen = false };
        var game = Program.Engine(model);
        var before = JsonSerializer.Serialize(game.CreateCheckpoint());
        var original = game.CreateSnapshot(0);
        var cost = new CardSnapshot(765432, CardKind.Peach, Suit.Heart, 7, "桃", "7");
        var hiddenOpponentCost = new CardSnapshot(765433, CardKind.Dodge, Suit.Club, 12, "闪", "Q");
        var libraries = new[]
        {
            new PrivateGeneralLibrarySnapshot(new(0, "classic:huashen", "fixture:library-a", "fixture:library-a"),
                2, "classic:xiao-qiao", "classic:tianxiang", GeneralGender.Female, "wu",
                Array.AsReadOnly(new[] { "classic:xiao-qiao", "classic:guan-yu" })),
            new PrivateGeneralLibrarySnapshot(new(0, "classic:huashen", "fixture:library-b", "fixture:library-b"),
                1, "classic:zhang-fei", "classic:paoxiao", null, null,
                Array.AsReadOnly(new[] { "classic:zhang-fei" }))
        };
        var other = new PrivateGeneralLibrarySnapshot(new(1, "classic:huashen", "fixture:library-other", "fixture:library-other"),
            2, "classic:xiao-qiao", "classic:tianxiang", GeneralGender.Female, "wu",
            Array.AsReadOnly(new[] { "classic:cao-cao", "classic:lu-bu" }));
        var projected = original with
        {
            PendingDecision = null,
            Players = original.Players.Select(player => player.Seat switch
            {
                0 => player with { GeneralId = "classic:zuo-ci", GeneralName = "左慈", PrivateGeneralLibraries = Array.AsReadOnly(libraries) },
                1 => player with { PrivateGeneralLibraries = Array.AsReadOnly(new[] { other }) },
                _ => player
            }).ToArray(),
            CardDeclarations = Array.AsReadOnly(new[]
            {
                new CardDeclarationSnapshot(71, 0, 0, CardKind.Slash, Array.AsReadOnly(new[] { 1 }), false, OwnerCost: cost),
                // A stale face and an unauthorized owner cost must not become public UI even if supplied by a malformed DTO.
                new CardDeclarationSnapshot(72, 1, 1, CardKind.Duel, Array.AsReadOnly(new[] { 0 }), false,
                    RevealedCard: hiddenOpponentCost, OwnerCost: hiddenOpponentCost)
            })
        };
        Refresh(model, projected);
        Program.Assert(model.HasDeclaredCards && model.HasGeneralLibraries && model.HasCenterChoices &&
                       model.DeclaredCards.All(item => item.PublicFaces.Count == 0) &&
                       model.DeclaredCards[0].OwnerCostFaces is [{ Id: 765432, Kind: CardKind.Peach, IsPrivateReveal: true }] &&
                       model.DeclaredCards[1].OwnerCostFaces.Count == 0 &&
                       model.DeclaredCards[0].Title.Contains("声明【杀】") && model.DeclaredCards[0].RevealStatus == "原牌扣置中",
            "Unrevealed declarations must display the declared kind and authorized owner cost without exposing a stale or foreign cost face.");
        Program.Assert(model.GeneralLibraries.Count == 3 &&
                       model.GeneralLibraries.Take(2).SelectMany(item => item.PrivateAvatars).Select(item => item.GeneralId)
                           .SequenceEqual(["classic:xiao-qiao", "classic:guan-yu", "classic:zhang-fei"]) &&
                       model.GeneralLibraries[0].PublicDeclaration.Contains("小乔") && model.GeneralLibraries[0].Attributes == "当前属性：吴 · 女" &&
                       model.GeneralLibraries[1].Attributes == "当前展示未生效" &&
                       model.GeneralLibraries[2].PrivateAvatars.Count == 0 &&
                       model.GeneralLibraries[2].PublicDeclaration.Contains("小乔"),
            "Separate source libraries must retain public display/current qualification, while the UI only maps this viewer's private general IDs.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "260-private-declaration-library.png"));
        Program.Find<Expander>(root).Single(item => item.Name == "GeneralLibraryOverview").IsExpanded = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "261-public-avatar-overview.png"));
        foreach (var expander in Program.Find<Expander>(root).Where(item => item.Name == "PrivateGeneralLibraryDetails" && item.Visibility == Visibility.Visible))
            expander.IsExpanded = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "261-private-avatar-library.png"));
        var declarations = Program.Find<ItemsControl>(root).Single(item => item.Name == "DeclaredCards");
        var actualFaces = Program.Find<Button>(declarations).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(declarations.ActualHeight > 0 && actualFaces.Length == 1 && actualFaces.All(button => !button.IsEnabled),
            "Actual declaration XAML must show exactly one private read-only face and no foreign or stale public face.");
        Program.Assert(Program.Find<ItemsControl>(root).Where(item => item.Name == "PrivateGeneralAvatars")
                           .Sum(item => item.Items.Count) == 3,
            "The actual expandable library controls must contain only the owner's three authorized avatars.");
        Refresh(model, projected with
        {
            CardDeclarations = Array.AsReadOnly(new[]
            {
                new CardDeclarationSnapshot(71, 0, 0, CardKind.Slash, Array.AsReadOnly(new[] { 1 }), true,
                    RevealedCard: cost, OwnerCost: cost)
            })
        });
        Program.Assert(model.DeclaredCards.Single().PublicFaces is [{ Id: 765432, Kind: CardKind.Peach, IsPrivateReveal: false }] &&
                       !model.DeclaredCards.Single().HasOwnerCost,
            "After reveal the owner's actual DTO may retain OwnerCost, but the UI must render its single public face without a duplicate private copy.");
        Refresh(model, projected with
        {
            CardDeclarations = Array.AsReadOnly(new[]
            {
                new CardDeclarationSnapshot(72, 1, 1, CardKind.Duel, Array.AsReadOnly(new[] { 0 }), true, RevealedCard: hiddenOpponentCost)
            })
        });
        Program.Assert(model.DeclaredCards.Single().PublicFaces is [{ Id: 765433, Kind: CardKind.Dodge, IsPrivateReveal: false }] &&
                       !model.DeclaredCards.Single().HasOwnerCost && model.DeclaredCards.Single().RevealStatus == "原牌已公开",
            "An actually revealed DTO must replace the private cost with its exact public face.");
        Refresh(model, projected with { CardDeclarations = null });
        Program.Assert(model.HasGeneralLibraries && !model.HasDeclaredCards && !model.HasCenterChoices && model.IsTableIdle,
            "Persistent general libraries must retain their separate information surface without hiding the recent-play table or marking an idle table as a response prompt.");
        Program.Render(root, 1120, 740, Path.Combine(output, "262-avatar-library-idle-table.png"));
        Refresh(model, original);
        Program.Assert(!model.HasDeclaredCards && !model.HasGeneralLibraries && model.DeclaredCards.Count == 0 &&
                       model.GeneralLibraries.Count == 0 && JsonSerializer.Serialize(game.CreateCheckpoint()) == before,
            "Retiring the snapshot information must clear every source and face without advancing or modifying the match.");
        window.Content = null;
        window.Close();
    }

    private static void Refresh(MainViewModel model, GameSnapshot snapshot) =>
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
}
