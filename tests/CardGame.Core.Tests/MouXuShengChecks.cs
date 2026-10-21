using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouXuShengChecks
{
    private const string Hero = "mou:xu-sheng", Driver = "fixture:mxs-damage", Source = "fixture:mxs-source",
        EnemyA = "fixture:mxs-enemy-a", EnemyB = "fixture:mxs-enemy-b", Deck = "fixture:mxs-deck";
    public static void NativeColorRunBenefitsAndMutualOpposingHandChoicesCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-xu-sheng-native", Hero, 10, 6, 0, 0, 0, [Driver],
            [new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.IronChain, Suit.Club, 9)], [],
            [new(EnemyA, Hp: 10) { StartingHand = [] }, new(EnemyB, Hp: 10) { StartingHand = [] }],
            [new(Source, Hp: 10) { StartingHand = [new(CardKind.Dodge, Suit.Spade, 5), new(CardKind.Dodge, Suit.Club, 6), new(CardKind.Dodge, Suit.Club, 7)] }], new string('c', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        MouXuShengReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouXuSheng is not null)?.MouXuSheng;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(25).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); }
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human choice is answered.");
            var c = p!.Choices.FirstOrDefault(match); Need(c is not null, "The expected exact native choice is published.");
            Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Xu Sheng flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Use(CardKind kind, int? target = null)
        {
            var id = Player(0).Hand.Single(c => c.Kind == kind).Id;
            var action = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is null && a.Kind != LegalActionKind.Recast &&
                (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(action is not null, "The real physical card has its canonical native action.");
            Submit(new PlayCardCommand(0, id, action!.TargetSeats, game.Revision, Prompt()!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        void Activate(string skill)
        {
            Reach(() => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
        }
        Submit(new StartGameCommand()); Play(); Use(CardKind.DrawTwo); Activate("mou:qinqiang");
        Reach(() => Receipt()?.Stage == MouXuShengStage.Benefit && Prompt()?.PlayerSeat == 0);
        Need(Receipt()?.X == 1, "The first true red Hand Use includes its own card in formal Qinqiang X1.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw"); Play();
        Need(Events<MouXuShengDrawReturnedEvent>() is [{ Requested: 1, Actual: 1 }], "The first item really pays native Draw1 before the original DrawTwo continues.");
        Use(CardKind.Slash, 3); Activate("mou:qinqiang");
        Reach(() => Receipt()?.Stage == MouXuShengStage.Benefit && Prompt()?.PlayerSeat == 0);
        Need(Receipt()?.X == 2 && Prompt()!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") != "draw"),
            "Two consecutive actual red Uses give X2, and this actual turn's already paid Draw item is unavailable independently of Damage.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "damage"); Play();
        Need(Player(3).Hp == 7 && Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 3) &&
            Events<MouXuShengBenefitUsedEvent>() is [{ Damage: false, X: 1 }, { Damage: true, X: 2 }],
            "The exact original red Slash really deals base1+X2 and both independent actual-turn items debit once.");
        Use(CardKind.IronChain, 1); Play();
        Need(Events<MouXuShengBenefitUsedEvent>().Length == 2, "A further true Hand Use cannot reoffer either spent item in the same actual turn.");
        var handBefore = Player(0).HandCount;
        Submit(new UseProgramSkillCommand(0, Driver, "incoming", [], [1], game.Revision, Prompt()!.PromptId)); Activate("mou:yizhen");
        Reach(() => Receipt()?.Stage == MouXuShengStage.Selecting && Prompt()?.PlayerSeat == 0);
        Need(game.CreateSnapshot(0).PrivateRevealedCards?.Count == 3 && game.CreateSnapshot(1).PrivateRevealedCards?.Count == handBefore &&
            (game.CreateSnapshot(2).PrivateRevealedCards?.Count ?? 0) == 0,
            "Yizhen really grants both participants their opposing frozen Hand view while an unrelated viewer sees neither private view.");
        var sourceCard = Prompt()!.Choices.First().Cards.Single(); Answer(c => c.Cards.SequenceEqual([sourceCard]));
        Need(Receipt() is { Stage: MouXuShengStage.Selecting, Cursor: 1, Selected.Count: 1, Paid.Count: 0 } && Events<MouXuShengDiscardPaidEvent>().Length == 0,
            "The first opposing-Hand private choice neither pays nor reveals a selected card before the second participant commits.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Receipt() is { Cursor: 1, Selected.Count: 1, Paid.Count: 0 },
            "One representative Cold restores the partial private opposing-Hand commitment on its actual sourced-damage frame.");
        Play();
        Need(Events<MouXuShengDiscardPaidEvent>() is [{ Chooser: 0, Holder: 1 }, { Chooser: 1, Holder: 0 }] &&
            Events<MouXuShengClaimedEvent>().Length == 2 && Player(0).HandCount == handBefore + 1 && Player(1).HandCount == 2 &&
            Player(0).Hand.Any(c => c.Id == sourceCard) && Events<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.Reason.Value == "skill-program.mou:yizhen.discard" && e.ActualCount == 1) == 2,
            "Each chooser really discards one opposing Hand entity; both black native primary receipts then recover exactly those two available entities once.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-xu-sheng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.DrawTwo, CardKind.Slash, CardKind.IronChain })
            { var card = CardCatalog.Get(kind); builder.AddCard(new("fixture:mxs-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实来源伤害", description = "由所选角色造成真实普通伤害1" } } }));
            builder.AddSkill(new(Driver, "真实来源伤害", "由所选角色造成真实普通伤害1") { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            foreach (var id in new[] { Source, EnemyA, EnemyB }) builder.AddGeneral(new(id, "真实同行", "supporter", "standard:none", "wei", 10));
            builder.AddDeck(new(Deck, "固定真实黑闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("fixture:mxs-" + CardKind.Dodge, Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "谋徐盛真实主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Source, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
