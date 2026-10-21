using System.Diagnostics;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ParticipantPromptUiChecks
{
    private const string Hero = "mou:chen-lin", Plain = "fixture:participant-ui-plain", Deck = "fixture:participant-ui-deck";

    public static void PrivateHumanInputStepAutoAndReload()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "participant-host-ui", Plain, 12, 0, 0, 0, 0, [],
            [new(CardKind.Dodge, Suit.Heart, 13)], [],
            [new(Plain, Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 11)] },
             new(Plain, Hp: 12) { StartingHand = [] }],
            [new(Hero, Hp: 3) { StartingHand = [new(CardKind.Dodge, Suit.Club, 1), new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Club, 3)] }],
            new string('d', 64));
        var game = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 6
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        ReachRaw(() => game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard);
        var play = game.CreateSnapshot(0).PendingDecision!;
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        ReachRaw(() => game.CreateSnapshot(0).PendingDecision?.ParticipantFrameId is not null);
        var original = game.CreateSnapshot(0).PendingDecision!;
        var frameId = original.ParticipantFrameId!.Value;
        Program.Assert(original.SourceSeat == 1 && original.IsPrivate,
            "The actual NPC Chen Lin must issue the same private invitation to human0 and real NPC participants.");

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var host = Host(model);
        PumpUntil(() => host.HasReadyResponse || host.Failure is not null, "The actual WPF bridge did not receive an independently computed NPC answer.");
        Program.Assert(host.Failure is null && host.IsAcceptingInput && model.CanStepAi && !model.IsAutoAdvance,
            "A paused invitation must expose ready NPC input through the actual Step control.");
        var human = game.CreateSnapshot(0).PendingDecision!;
        var ownIds = model.Hand.Select(card => card.Id).ToHashSet();
        var npcChoices = new[] { 2, 3 }.SelectMany(seat => game.CreateSnapshot(seat).PendingDecision!.Choices).ToArray();
        Program.Assert(human.ParticipantFrameId == frameId && human.PromptId == original.PromptId &&
            model.IsSkillSelectionPending && model.SkillChoices.Select(c => c.Id).SequenceEqual(human.Choices.Select(c => c.Id)) &&
            human.Choices.Where(c => c.Cards.Count > 0).All(c => c.Cards.All(ownIds.Contains)) &&
            npcChoices.Where(c => c.Cards.Count > 0).All(c => c.Cards.All(id => !ownIds.Contains(id))) &&
            !game.Events.Any(e => e.Payload is MouChenLinInvitationAnswerEvent a && a.FrameId == frameId),
            "Ready NPC workers must neither answer while paused nor replace the human's own private material choices.");

        var beforeStep = game.AcceptedCommands.Count;
        model.StepAiCommand.Execute(null);
        Program.Assert(game.AcceptedCommands.Count == beforeStep + 1 &&
            game.AcceptedCommands.Last() is AnswerPromptCommand { ActorSeat: 2 or 3 } npcAnswer &&
            npcAnswer.PromptId == original.PromptId &&
            game.Events.Count(e => e.Payload is MouChenLinInvitationAnswerEvent a && a.FrameId == frameId) == 1 &&
            game.CreateSnapshot(0).PendingDecision?.ParticipantFrameId == frameId,
            "One real Step must submit exactly one completed NPC's original Answer command while preserving the human invitation.");

        var selected = model.SkillChoices.First(c => c.Cards.Count == 1);
        var gift = selected.Cards.Single();
        var beforeHuman = game.AcceptedCommands.Count;
        model.SelectSkillChoiceCommand.Execute(selected);
        Program.Assert(game.AcceptedCommands.Count == beforeHuman + 1 &&
            game.AcceptedCommands.Last() is AnswerPromptCommand { ActorSeat: 0 } answer &&
            answer.PromptId == original.PromptId && answer.Choice == selected.Id,
            "The human's existing WPF choice command must submit its original published answer alongside NPC workers.");
        for (var step = 0; step < 128 && !host.IsAcceptingInput; step++)
        {
            Program.Assert(model.CanStepAi, "The human Give's native continuation must remain available to the existing Step command.");
            model.StepAiCommand.Execute(null);
            DrainPosted();
        }
        Program.Assert(host.IsAcceptingInput && HumanGifts() == 1 &&
            game.Events.Count(e => e.Payload is MouChenLinInvitationAnswerEvent a && a.FrameId == frameId) == 2,
            "The human's native Give must finish once before saving with one real NPC still unanswered.");

        model.SaveGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && store.Read(GameSaveSlot.Manual).Checkpoint.Revision == game.Revision, model.SaveStatus);
        var detached = game;
        var detachedRevision = detached.Revision;
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        host = Host(model);
        Program.Assert(!ReferenceEquals(game, detached) && !model.IsAutoAdvance && HumanGifts() == 1,
            "Actual manual reload must replace the engine and reconstruct only the unanswered participant work.");
        PumpUntil(() => host.HasReadyResponse || host.Failure is not null, "The restored WPF host did not reconstruct its outstanding NPC response.");
        Program.Assert(host.Failure is null && host.IsAcceptingInput, "The restored participant bridge failed.");
        model.IsHelpOpen = true;
        model.IsAutoAdvance = true;
        var pausedRevision = game.Revision;
        DrainPosted();
        Program.Assert(game.Revision == pausedRevision && detached.Revision == detachedRevision &&
            game.Events.Count(e => e.Payload is MouChenLinInvitationAnswerEvent a && a.FrameId == frameId) == 2,
            "Auto must respect the existing help modal, and canceled old-host callbacks must not submit to the detached game.");
        model.IsHelpOpen = false;
        PumpUntil(() => game.Events.Any(e => e.Payload is MouChenLinInvitationClosedEvent c && c.FrameId == frameId) || host.Failure is not null,
            "Closing the modal did not let the actual automatic participant bridge finish its remaining NPC answer.");
        model.IsAutoAdvance = false;
        Program.Assert(host.Failure is null && HumanGifts() == 1 &&
            game.Events.Count(e => e.Payload is MouChenLinInvitationClosedEvent c && c.FrameId == frameId) == 1 &&
            game.Events.Where(e => e.Payload is MouChenLinInvitationAnswerEvent a && a.FrameId == frameId)
                .Select(e => ((MouChenLinInvitationAnswerEvent)e.Payload).Actor).Order().SequenceEqual([0, 2, 3]) &&
            game.AcceptedCommands.OfType<AnswerPromptCommand>().Count(c => c.PromptId == original.PromptId && c.ActorSeat == 0) == 1 &&
            detached.Revision == detachedRevision,
            "Step, human input, restore and Auto must finish one invitation with one answer per actor and one human native Give.");
        Program.Assert(model.FlushPendingSave() && !model.HasSaveError &&
            GameCheckpointJson.Serialize(store.Read(GameSaveSlot.Automatic).Checkpoint) == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "The NPC bridge must use the normal WPF command-session autosave path.");

        int HumanGifts() => game.Events.Count(e => e.Payload is MouChenLinGiftPaidEvent g && g.FrameId == frameId && g.Giver == 0 && g.CardId == gift);
        void ReachRaw(Func<bool> done)
        {
            for (var step = 0; step < 200 && !done(); step++)
            {
                Program.Assert(game.CreateSnapshot(0).PendingDecision is null,
                    "The fixed native setup encountered an unexpected human prompt before the invitation.");
                Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            }
            Program.Assert(done(), "The fixed native setup did not reach its actual invitation boundary.");
        }
    }

    private static ParticipantPromptHost Host(MainViewModel model) =>
        (ParticipantPromptHost)typeof(MainViewModel).GetField("_participantHost",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(model)!;

    private static void DrainPosted()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> done, string failure)
    {
        if (done()) return;
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        EventHandler tick = (_, _) => { if (done() || watch.Elapsed >= TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Tick += tick;
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); timer.Tick -= tick; }
        Program.Assert(done(), failure);
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Original command rejected.");

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-participant-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var dodge = CardCatalog.Get(CardKind.Dodge);
            builder.AddCard(new("fixture:participant-ui-dodge", dodge.DisplayName, dodge.CategoryName, dodge.Description, CardKind.Dodge));
            builder.AddGeneral(new(Plain, "邀作真实参与者", "", "standard:none", "wei", 12));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:participant-ui-dodge", Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "独立参与者桌面桥接", 4, 4,
                new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
