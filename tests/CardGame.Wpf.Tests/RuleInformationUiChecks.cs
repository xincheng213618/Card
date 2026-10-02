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
        VerifyTurnHandHoldViews(model, root, original, output);
        Program.Assert(JsonSerializer.Serialize(game.CreateCheckpoint()) == before,
            "Reading private turn holds must leave the command checkpoint unchanged.");
        window.Content = null;
        window.Close();
    }

    private static void VerifyTurnHandHoldViews(MainViewModel model, FrameworkElement root,
        GameSnapshot original, string output)
    {
        var first = new CardSnapshot(765434, CardKind.Slash, Suit.Heart, 5, "杀", "5");
        var second = new CardSnapshot(765435, CardKind.Peach, Suit.Diamond, 9, "桃", "9");
        var foreign = new CardSnapshot(765436, CardKind.Dodge, Suit.Spade, 11, "闪", "J");
        PrivateTurnHoldSnapshot Hold(long id, int ownerSeat, IReadOnlyList<CardSnapshot> cards) =>
            new(HoldId: id, OwnerSeat: ownerSeat, SkillId: "boundary:qianxun",
                SkillInstanceId: $"fixture:hold-{id}", SourceId: $"fixture:source-{id}",
                ExpiresTurnNumber: original.TurnNumber, Count: cards.Count, Cards: cards);
        var own = new[] { Hold(81, 0, Array.AsReadOnly(new[] { first, second })), Hold(82, 0, Array.AsReadOnly(new[] { first })) };
        var malformed = Hold(83, 1, Array.AsReadOnly(new[] { foreign }));
        var projected = original with
        {
            PendingDecision = null,
            Players = original.Players.Select(player => player.Seat switch
            {
                0 => player with { GeneralId = "boundary:lu-xun", GeneralName = "界陆逊",
                    PrivateTurnHolds = Array.AsReadOnly(new[] { own[0], own[1], malformed }) },
                // Foreign and misattributed DTOs deliberately contain faces; WPF still must not display them.
                1 => player with { PrivateTurnHolds = Array.AsReadOnly(new[] { Hold(84, 1, Array.AsReadOnly(new[] { foreign })) }) },
                _ => player
            }).ToArray()
        };
        Refresh(model, projected);
        Program.Assert(model.HasTurnHandHolds && model.TurnHandHolds.Count == 4 &&
                       model.TurnHandHolds.Take(2).SelectMany(item => item.PrivateCards).Select(card => card.Id)
                           .SequenceEqual([765434, 765435, 765434]) &&
                       model.TurnHandHolds.Skip(2).All(item => item.PrivateCards.Count == 0) &&
                       model.TurnHandHolds[0].Title.Contains("2张") &&
                       model.TurnHandHolds.All(item => item.ReturnHint == "回合结束时返还") &&
                       !model.HasCenterChoices && model.IsTableIdle,
            "Each source hold must preserve public counts and the idle table while double-gating all private faces by viewer and hold owner.");
        Program.Find<Expander>(root).Single(item => item.Name == "TurnHandHoldOverview").IsExpanded = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "263-turn-hand-hold-overview.png"));
        foreach (var expander in Program.Find<Expander>(root).Where(item => item.Name == "PrivateTurnHandHoldDetails" && item.Visibility == Visibility.Visible))
            expander.IsExpanded = true;
        Program.Render(root, 1120, 740, Path.Combine(output, "264-private-turn-hand-holds.png"));
        var holds = Program.Find<ItemsControl>(root).Single(item => item.Name == "TurnHandHolds");
        var buttons = Program.Find<Button>(holds).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(holds.ActualHeight > 0 && buttons.Length == 3 && buttons.All(button => !button.IsEnabled) &&
                       Program.Find<ItemsControl>(holds).Where(item => item.Name == "PrivateTurnHandHoldCards").Sum(item => item.Items.Count) == 3,
            "The actual hold XAML must contain exactly the authorized read-only faces without exposing foreign or misattributed cards.");
        Refresh(model, projected with
        {
            Players = projected.Players.Select(player => player with { PrivateTurnHolds = null }).ToArray()
        });
        Program.Assert(!model.HasTurnHandHolds && model.TurnHandHolds.Count == 0 && !model.HasCenterChoices && model.IsTableIdle,
            "Returning or clearing the held cards must clear every stale private face without turning the information panel into a prompt.");
        Refresh(model, original);
    }

    private static void Refresh(MainViewModel model, GameSnapshot snapshot) =>
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
}
