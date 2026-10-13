using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ZhugeFriendRosterUiChecks
{
    private static readonly string[] Friends =
        ["friend:zhuge-liang", "friend:pang-tong", "friend:xu-shu", "friend:shi-tao", "friend:cui-jun"];

    public static void SelectionAndGallery(string output)
    {
        var standard = Registry();
        Program.Assert(CardArt.Get(CardKind.Xuanjian) is not null && CardArt.GetEquipment(CardKind.Xuanjian) is not null &&
            CardArt.NeedsRuntimeNameOverlay(CardKind.Xuanjian), "Xuanjian must display the existing weapon artwork with its own runtime name on the face and equipment strip.");
        var groups = standard.Modes["identity:classic-8"].GeneralVariantGroups!;
        Program.Assert(groups["character:zhuge-liang"].Contains("mou:zhuge-liang") &&
            groups["character:zhuge-liang"].Contains("friend:zhuge-liang") &&
            groups["character:pang-tong"].Contains("friend:pang-tong") &&
            groups["character:xu-shu"].Contains("friend:xu-shu"),
            "The real classic identity roster must register both Zhuge additions and the friend editions under their existing character families.");
        var registry = Registry(new FamilyMode(groups));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = FamilyMode.Id, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The family selection must start through the actual setup command.");
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "The family fixture must reach its real selection prompt.");
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.SelectGeneral, "The bounded setup must reach private general selection.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, useExpandedContent: false, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(model);
        var revision = engine.Revision;
        var before = SnapshotJson.Serialize(engine.CreateSnapshot(0));
        var window = new MainWindow(model) { Width = 1120, Height = 820, ShowActivated = false, ShowInTaskbar = false };
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var family in new[] { "zhuge-liang", "pang-tong", "xu-shu" })
            {
                var variants = groups["character:" + family];
                model.PreviewGeneralChoiceCommand.Execute(model.GeneralChoices.Single(choice => variants.Contains(choice.GeneralId)));
                Program.Render(root, 1120, 820, Path.Combine(output, "friend-selection-" + family + ".png"));
                Program.Assert(model.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(variants),
                    "Selection must use the actual identity roster's same-person variants, including the new edition.");
                foreach (var id in variants.Where(id => id.StartsWith("friend:") || id == "mou:zhuge-liang"))
                {
                    var choices = (ItemsControl)window.FindName("GeneralVariantChoices");
                    var button = Program.Find<Button>(choices).Single(button => button.DataContext is GeneralChoiceViewModel choice && choice.GeneralId == id);
                    Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The new edition must expose an enabled real version button.");
                    button.Command!.Execute(button.CommandParameter);
                    root.UpdateLayout();
                    Program.Assert(model.SelectedGeneralChoice!.GeneralId == id && model.GeneralChoices.Count == 3 &&
                        model.SelectedGeneralChoice.SkillDescription.Contains(registry.Skills[registry.Generals[id].SkillId].Name) &&
                        model.SelectedGeneralChoice.PortraitBrush is ImageBrush { ImageSource: not null } &&
                        engine.Revision == revision && SnapshotJson.Serialize(engine.CreateSnapshot(0)) == before,
                        "Previewing a new version must update its artwork and skills without consuming a candidate or submitting a command.");
                }
            }
            model.PreviewGeneralChoiceCommand.Execute(model.GeneralChoices.Single(choice => groups["character:zhuge-liang"].Contains(choice.GeneralId)));
            model.PreviewGeneralChoiceCommand.Execute(model.SelectedGeneralVariants.Single(choice => choice.GeneralId == "friend:zhuge-liang"));
            Program.Render(root, 1120, 820, Path.Combine(output, "zhuge-new-editions.png"));
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.Assert(engine.CreateSnapshot(0).Players[0].GeneralId == "friend:zhuge-liang",
                "Confirmation must select the actual friend edition through the engine.");
            CheckPersistentPrediction(engine, model, root, output);

            model.OpenGeneralGalleryCommand.Execute(null);
            model.SelectGeneralGallerySeriesCommand.Execute("friend");
            Program.Assert(model.GeneralGalleryEntries.Select(entry => entry.GeneralId).ToHashSet().SetEquals(Friends) &&
                model.GeneralGalleryEntries.All(entry => entry.IsPlayable && entry.HasPortrait),
                "The friend series must contain all five playable editions with distinct local portraits.");
            var panel = new GeneralGalleryPanel { DataContext = model };
            Program.Render(panel, 1440, 880, Path.Combine(output, "friend-series-gallery.png"));
            foreach (var entry in model.GeneralGalleryEntries.ToArray())
            {
                model.SelectGeneralGalleryEntryCommand.Execute(entry);
                Program.Assert(model.SelectedGeneralGalleryEntry == entry && entry.Skills.Count == 3 &&
                    entry.Portrait.Brush is ImageBrush { ImageSource: not null }, "Each friend detail must show its own three skills and local artwork.");
                if (entry.GeneralId == "friend:zhuge-liang")
                    Program.Render(panel, 1120, 820, Path.Combine(output, "friend-zhuge-detail.png"));
                model.CloseGeneralGalleryDetailsCommand.Execute(null);
            }
            model.SelectGeneralGallerySeriesCommand.Execute("mou");
            Program.Assert(model.GeneralGalleryEntries.Any(entry => entry.GeneralId == "mou:zhuge-liang" && entry.SkillName.Contains("智绝")),
                "The separate Mou series must expose the selected OL ruleset.");
        }
        finally { window.DataContext = null; window.Close(); }
    }

    private static void CheckPersistentPrediction(GameEngine engine, MainViewModel model, FrameworkElement root, string output)
    {
        for (var step = 0; step < 64 && engine.PendingDecision?.SkillPrompt?.SkillId != "friend:yance"; step++)
            Program.Assert(engine.Submit(new AdvanceOneStepCommand(engine.Revision)).Accepted,
                "The selected friend must reach its real first-round prediction opportunity.");
        void Answer(Func<PromptChoice, bool> predicate)
        {
            var prompt = engine.PendingDecision!;
            Program.Assert(prompt.PlayerSeat == 0 && engine.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                prompt.Choices.Single(predicate).Id, engine.Revision)).Accepted, "The private prediction choice must commit through its actual prompt.");
        }
        Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(choice => choice.Parameters.GetValueOrDefault("value") == "predict");
        Answer(choice => choice.Parameters.GetValueOrDefault("value") == "color");
        for (var index = 0; index < 3; index++) Answer(choice => choice.Parameters.GetValueOrDefault("value") == "red");
        Answer(choice => choice.Parameters.GetValueOrDefault("value") == "hidden");
        Program.Assert(engine.CreateSnapshot(0).FriendPredictions is [{ Revealed: false, Index: 0 }] &&
            engine.CreateSnapshot(1).FriendPredictions is null &&
            model.HumanSkillCards.Single(skill => skill.ContentId == "friend:yance").StateText.Contains("仅自己可见") &&
            model.HumanSkillCards.Single(skill => skill.ContentId == "friend:yance").StateText.Contains("红色 → 红色 → 红色"),
            "The skill rail must retain the owner's private sequence after the choice dialog closes.");
        Program.Render(root, 1120, 820, Path.Combine(output, "friend-private-prediction.png"));
    }

    private static ContentRegistry Registry(IGameContentPackage? extra = null)
    {
        IGameContentPackage[] packages = [new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()];
        return ContentRegistry.Build(extra is null ? packages : [.. packages, extra]);
    }

    private sealed class FamilyMode(IReadOnlyDictionary<string, IReadOnlyList<string>> groups) : IGameContentPackage
    {
        internal const string Id = "identity:classic-zhuge-friends-ui-5";
        public PackageManifest Manifest { get; } = new("zhuge-friends-ui-check", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var families = new[] { "character:zhuge-liang", "character:pang-tong", "character:xu-shu" }
                .ToDictionary(id => id, id => groups[id]);
            builder.AddMode(new(Id, "诸葛与友系列版本检查", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "classic:standard-deck", GeneralCandidateCount: 3, GeneralPoolIds: families.Values.SelectMany(ids => ids).ToArray())
                { GeneralVariantGroups = families });
        }
    }
}
