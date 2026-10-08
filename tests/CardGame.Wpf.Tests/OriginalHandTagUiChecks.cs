using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class OriginalHandTagUiChecks
{
    private const string Huaiyuan = "ol:huaiyuan", Chongxin = "ol:chongxin";

    public static void RefreshAndColdRestore(string output)
    {
        var official = StandardContentRegistry.CreateWithClassicGenerals();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(official));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Program.Assert(game.CreateSnapshot(0).PendingDecision!.ValidContentIds.Contains(Fixture.Owner),
            "The real small-mode selection publishes the intended original-hand owner.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        ReachPlay(game);
        var initial = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Program.Assert(initial.Length == 4 && game.CreateCardZoneDiagnostics().Count(z => z.Location == CardLocation.DrawPile) == 0,
            "All sixteen fixed physical cards were genuinely dealt; the recast draw must recycle its actual cost.");
        AssertTags(game, initial);

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(false, 31, true, store, useExpandedContent: true, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        Program.Assert(!vm.HasSaveError, vm.SaveStatus);
        game = Program.Engine(vm);
        var originalCard = vm.Hand.Single(c => c.Id == initial[0]);
        var retainedCard = vm.Hand.Single(c => c.Id == initial[1]);
        Program.Assert(vm.Hand.Count == 4 && vm.Hand.All(c => c.OriginalHandTag == "绥" && c.HasOriginalHandTag &&
                c.CardHint.Contains("标记：绥", StringComparison.Ordinal) && c.KindLabel == CardCatalog.Get(c.Kind!.Value).CategoryName),
            "Real initial entities expose the Sui badge and tooltip while retaining their ordinary category label.");
        var originalNotifications = new List<string?>();
        var retainedNotifications = new List<string?>();
        originalCard.PropertyChanged += (_, e) => originalNotifications.Add(e.PropertyName);
        retainedCard.PropertyChanged += (_, e) => retainedNotifications.Add(e.PropertyName);
        Refresh(vm);
        Program.Assert(ReferenceEquals(originalCard, vm.Hand.Single(c => c.Id == originalCard.Id)) &&
            ReferenceEquals(retainedCard, vm.Hand.Single(c => c.Id == retainedCard.Id)) &&
            !originalNotifications.Any(IsTagOrHint) && !retainedNotifications.Any(IsTagOrHint),
            "An unchanged refresh reuses visible hand items without repeated tag or tooltip notifications.");

        var activation = registry.GetSkill(Chongxin).Program!.Activations.Single().Id;
        Accept(game, new UseProgramSkillCommand(0, Chongxin, activation, [originalCard.Id], [1],
            game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Huaiyuan && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("entity-action") == "benefit-target"));
        Program.Assert(vm.Hand.All(c => c.Id != originalCard.Id),
            "The committed real recast cost removes its actual entity from the visible hand while its native benefit pauses.");
        AssertTags(game, initial.Skip(1).ToArray());
        Answer(game, c => c.Parameters.GetValueOrDefault("entity-action") == "benefit-target" && c.Targets.SequenceEqual([0]));
        Reach(game, p => p.SkillPrompt?.SkillId == Huaiyuan && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("entity-action") == "benefit-option"));
        Answer(game, c => c.Parameters.GetValueOrDefault("option") == "hand-limit");
        ReachPlay(game);

        var movements = game.CardMovements.Where(m => m.CardId == originalCard.Id).ToArray();
        var cost = movements.Single(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.RecastDiscard);
        var shuffle = movements.Single(m => m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile &&
            m.Reason == CardMoveReasons.Reshuffle);
        var draw = movements.Single(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
            m.Reason == CardMoveReasons.RecastDraw);
        Program.Assert(cost.Sequence < shuffle.Sequence && shuffle.Sequence < draw.Sequence &&
            game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count(e => e.ActorSeat == 0 && e.CardId == originalCard.Id && e.DrawCount == 1) == 1 &&
            game.Events.Select(e => e.Payload).OfType<OriginalHandEntityConsumedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == Huaiyuan &&
                e.StateId == "sui" && e.MovementSequence == cost.Sequence) == 1,
            "The returned displayed card is the exact once-consumed initial entity, paid and redrawn through native recast and reshuffle movements.");
        var returnedCard = vm.Hand.Single(c => c.Id == originalCard.Id);
        Program.Assert(!returnedCard.HasOriginalHandTag && returnedCard.OriginalHandTag == string.Empty &&
            !returnedCard.CardHint.Contains("标记：绥", StringComparison.Ordinal) &&
            returnedCard.KindLabel == CardCatalog.Get(returnedCard.Kind!.Value).CategoryName &&
            ReferenceEquals(retainedCard, vm.Hand.Single(c => c.Id == retainedCard.Id)) && retainedCard.HasOriginalHandTag &&
            !retainedNotifications.Any(IsTag),
            "Returning the same physical entity does not restore Sui; continuously held cards keep their existing item and badge without spurious tag changes.");
        AssertTags(game, initial.Skip(1).ToArray());
        var returnedNotifications = new List<string?>();
        returnedCard.PropertyChanged += (_, e) => returnedNotifications.Add(e.PropertyName);
        retainedNotifications.Clear();
        Refresh(vm);
        Program.Assert(ReferenceEquals(returnedCard, vm.Hand.Single(c => c.Id == returnedCard.Id)) &&
            ReferenceEquals(retainedCard, vm.Hand.Single(c => c.Id == retainedCard.Id)) &&
            !returnedNotifications.Any(IsTagOrHint) && !retainedNotifications.Any(IsTagOrHint),
            "A second refresh preserves both the returned unmarked item and retained marked item without redundant badge or tooltip notifications.");

        var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        Program.Assert(!vm.HasSaveError && !ReferenceEquals(game, Program.Engine(vm)) &&
            SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
            "Actual save/load cold replay preserves the committed original-hand state and native recast result.");
        game = Program.Engine(vm);
        AssertTags(game, initial.Skip(1).ToArray());
        Program.Assert(vm.Hand.Count == 4 && vm.Hand.Single(c => c.Id == originalCard.Id) is
                { HasOriginalHandTag: false, OriginalHandTag: "" } restored && !restored.CardHint.Contains("标记：绥", StringComparison.Ordinal) &&
            vm.Hand.Where(c => c.Id != originalCard.Id).All(c => c.HasOriginalHandTag && c.OriginalHandTag == "绥" &&
                c.CardHint.Contains("标记：绥", StringComparison.Ordinal)),
            "Cold UI restore renders the returned card without Sui and the three unconsumed original cards with Sui.");
    }

    private static bool IsTag(string? name) => name is nameof(CardViewModel.OriginalHandTag) or nameof(CardViewModel.HasOriginalHandTag);
    private static bool IsTagOrHint(string? name) => IsTag(name) || name == nameof(CardViewModel.CardHint);
    private static void Refresh(MainViewModel vm) => typeof(MainViewModel)
        .GetMethod("RefreshCurrentView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
    private static void AssertTags(GameEngine game, int[] expected)
    {
        var own = game.CreateSnapshot(0).Players[0].OriginalHandEntities!.Single();
        Program.Assert(own is { SkillId: Huaiyuan, StateId: "sui", Name: "绥" } && own.RemainingCount == expected.Length &&
            own.CardIds is not null && own.CardIds.ToHashSet().SetEquals(expected) &&
            own.CardIds is System.Collections.IList { IsReadOnly: true },
            "The UI's owner projection contains exactly the frozen, unconsumed original physical identities.");
        foreach (var viewer in new[] { 1, 2, 3 })
        {
            var foreign = game.CreateSnapshot(viewer).Players[0].OriginalHandEntities!.Single();
            Program.Assert(foreign.RemainingCount == expected.Length && foreign.Name == "绥" && foreign.CardIds is null &&
                game.CreateSnapshot(viewer).Players[0].Hand.Count == 0,
                "Foreign prepared views expose only the public mark name/count, never the original or current hand identities.");
        }
    }
    private static void ReachPlay(GameEngine game) => Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> reached)
    {
        for (var step = 0; step < 100; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && reached(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed original-hand UI fixture did not reach its genuine native prompt.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> select)
    {
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.Single(select).Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "The fixed original-hand UI command was rejected.");
    }
    private sealed class Fixture(ContentRegistry official) : IGameContentPackage
    {
        internal const string Mode = "identity:ui-original-hand-tags", Owner = "fixture:ui-original-hand-owner";
        public PackageManifest Manifest { get; } = new("fixture-ui-original-hand-tags", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Huaiyuan, Chongxin, "ol:dezhang", "ol:weishu" }) builder.AddSkill(official.GetSkill(id));
            builder.AddGeneral(new(Owner, "原始手牌展示者", "supporter", Huaiyuan, "jin", 4, [Chongxin, "ol:dezhang"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:ui-original-hand-peer-{i}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new(id, "其他角色", "supporter", "standard:none", "qun", 4));
            builder.AddDeck(new("fixture:ui-original-hand-deck", "全部真实发出的固定牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 16).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "原始手牌界面边界", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-original-hand-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));

            // MainViewModel constructs its expanded default table before it can
            // load a saved game. Player preferences contain no table-mode key.
            var bootstrap = Enumerable.Range(0, 8).Select(i => $"fixture:ui-original-hand-bootstrap-{i}").ToArray();
            foreach (var id in bootstrap) builder.AddGeneral(new(id, "启动角色", "supporter", "standard:none", "qun", 4));
            builder.AddDeck(new("fixture:ui-original-hand-bootstrap-deck", "界面构造的合法八人牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 32).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-8", "界面构造启动桌", 8, 8,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 },
                "fixture:ui-original-hand-bootstrap-deck", GeneralCandidateCount: 3, GeneralPoolIds: bootstrap));
        }
    }
}
