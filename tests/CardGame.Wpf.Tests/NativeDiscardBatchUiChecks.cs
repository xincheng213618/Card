using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class NativeDiscardBatchUiChecks
{
    public static void CaoXianAdjustmentAndTargetDiscardUseOneEditableDraft(string output)
    {
        var (game, registry) = NativeCollectedDiscardProjectionChecks.CreateCaoXianDiscardScene();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 373, showSetup: false, saveStore: store, contentRegistry: registry)
            { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        VerifyDraft(5, "lingxi");
        NativeCollectedDiscardProjectionChecks.BeginCaoXianSelfDiscard(game); Refresh();
        VerifyDraft(2, "zhifou");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
            "Both original Cao Xian native batch payments must cold-restore through the accepted original answers.");

        void VerifyDraft(int required, string skill)
        {
            Program.Assert(model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending && !model.IsHandResponsePending &&
                game.CreateSnapshot(0).PendingDecision!.RequiredCardCount == required,
                "Cao Xian's current fixed unpaid collection must use the shared batch selector, including native prompts without SkillPrompt.");
            var ids = model.Hand.Where(c => c.IsPlayable).Take(required).Select(c => c.Id).ToArray();
            Program.Assert(ids.Length == required, "The native batch must publish every required owned physical material.");
            var commands = game.AcceptedCommands.Count; var moves = game.CardMovements.Count;
            Select(ids[0]); Select(ids[1]); Refresh();
            Program.Assert(model.Hand.Count(c => c.IsSelected) == 2 && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves,
                "Two physical selections survive a view refresh without submitting or paying either singleton.");
            model.CancelActionCommand.Execute(null);
            Program.Assert(!model.HasSelection && model.Hand.All(c => !c.IsSelected) && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves,
                "Cancel clears the full local Cao Xian draft while its original native collection remains unpaid.");
            foreach (var id in ids) Select(id); Refresh();
            Program.Assert(model.CanConfirmSelected && model.Hand.Count(c => c.IsSelected) == required,
                "A complete exact set enables one explicit confirmation instead of capping selection at one.");
            using (var host = new NativeResponseStatusChecks.NativeHost(model)) host.Capture(output, "native-cao-xian-" + skill + "-selected", 1120);
            model.ConfirmSelectedCommand.Execute(null);
            var actual = game.CardMovements.Skip(moves).Where(m => m.Reason.Value == "skill-program.ol:lingxi-or-zhifou.discard").ToArray();
            Program.Assert(actual.Length == required && actual.Select(m => m.CardId).ToHashSet().SetEquals(ids) &&
                game.AcceptedCommands.Count == commands + required && model.Hand.All(c => !c.IsSelected),
                "One primary confirmation submits only the current original selection commands and pays the entire exact set once.");
        }
        void Select(int id) => model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
        void Refresh() => typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
    }

    public static void NativeVariableCostKeepsMultipleCardsAndCountRestrictions(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Fixture.Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(new StartGameCommand());
        Reach(p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(new SelectGeneralCommand(0, "fixture:native-discard-owner", game.Revision, game.PendingDecision!.PromptId));
        Activate();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, showSetup: false, saveStore: store, contentRegistry: registry)
            { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending && !model.IsHandResponsePending,
            "Xianying's singleton native card commands must form an arbitrary-size local discard draft.");
        var ids = model.Hand.Take(3).Select(c => c.Id).ToArray();
        var commands = game.AcceptedCommands.Count;
        var moves = game.CardMovements.Count;
        Select(ids[0]); Select(ids[1]); Refresh();
        Program.Assert(model.Hand.Count(c => c.IsSelected) == 2 && model.CanConfirmSelected &&
            game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves,
            "Two choices survive refresh without paying or submitting the first singleton immediately.");
        model.CancelActionCommand.Execute(null);
        Program.Assert(!model.HasSelection && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves,
            "Cancel clears both draft cards while the native skill remains unpaid.");
        Select(ids[0]); Select(ids[1]);
        using (var host = new NativeResponseStatusChecks.NativeHost(model)) host.Capture(output, "native-discard-two-selected", 1120);
        model.ConfirmSelectedCommand.Execute(null);
        var discarded = game.CardMovements.Skip(moves).Where(m => m.Reason.Value == "skill-program.qin-lang.xianying.discard").ToArray();
        Program.Assert(discarded.Length == 2 && discarded.Select(m => m.CardId).ToHashSet().SetEquals(ids.Take(2)) &&
            game.Events.Select(e => e.Payload).OfType<QinLangXianyingDiscardPaidEvent>().Single().Count == 2,
            "One explicit confirmation submits the original current choices and pays the exact two-card set once.");
        Reach(p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(new UseProgramSkillCommand(0, Fixture.Driver, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
        Activate(); Refresh();
        var next = model.Hand.Take(3).Select(c => c.Id).ToArray();
        Select(next[0]); Select(next[1]);
        Program.Assert(!model.CanConfirmSelected, "The two-card count already paid this round remains forbidden in the batch UI.");
        Select(next[2]);
        Program.Assert(model.CanConfirmSelected, "A different three-card count remains legal rather than capping selection at one.");
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.Events.Select(e => e.Payload).OfType<QinLangXianyingDiscardPaidEvent>().Select(e => e.Count).SequenceEqual([2, 3]),
            "The UI honors the native round count ledger while paying each set once.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
            "The batch still consists entirely of replayable original native answers.");

        void Select(int id) => model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
        void Refresh() => typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        void Accept(GameCommand command)
        {
            var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native discard fixture rejected a command.");
        }
        void Reach(Func<PendingDecision?, bool> done)
        {
            for (var step = 0; step < 100; step++)
            {
                if (done(game.CreateSnapshot(0).PendingDecision)) return;
                Program.Assert(game.PendingDecision is null || game.PendingDecision.PlayerSeat != 0,
                    "The bounded native discard scene reached an unexpected human prompt: " + game.PendingDecision?.Prompt);
                Accept(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Native discard fixture missed its next boundary.");
        }
        void Activate()
        {
            Reach(p => p?.SkillPrompt?.SkillId == "ol:xianying" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            var p = game.CreateSnapshot(0).PendingDecision!;
            Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, game.Revision));
            Reach(p => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick") == true);
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "fixture:native-discard-ui", Driver = "fixture:native-discard-driver";
        public PackageManifest Manifest { get; } = new("fixture-native-discard-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:qin-lang");
            var programs = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
                 {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                  "effects":[{"op":"damage","target":"owner","amount":1}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "实际受伤", description = "触发真实献应" } } }));
            b.AddSkill(new(Driver, "实际受伤", "触发真实献应") { Program = programs.Programs[Driver], ProgramPresentation = programs.Presentations[Driver] });
            b.AddGeneral(new("fixture:native-discard-owner", "秦朗", "supporter", "ol:xianying", "wei", 8, [Driver]));
            foreach (var seat in Enumerable.Range(1, 3)) b.AddGeneral(new($"fixture:native-discard-{seat}", "目标", "supporter", "standard:none", "qun", 8));
            b.AddDeck(new("fixture:native-discard-deck", "固定牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 8)).ToArray() });
            b.AddMode(new(Mode, "原生弃牌交互", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:native-discard-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:native-discard-owner", "fixture:native-discard-1", "fixture:native-discard-2", "fixture:native-discard-3"]));
        }
    }
}
