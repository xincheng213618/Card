using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class BoundaryWuYiMobileChecks
{
    private const string Hero = "boundary:wu-yi-mobile", Skill = "boundary:benxi-mobile", Quiet = "fixture:boundary-wu-yi-quiet",
        SkipPlay = "fixture:boundary-wu-yi-skip-play", Hold = "fixture:boundary-wu-yi-paid", Peer = "fixture:boundary-wu-yi-peer";

    public static void PaidPhaseMultipleNativeTargetsOneWholeUseRewardAndCold()
    {
        var s = Scene.Start(); s.Pay(2);
        s.Need(s.G.GetCombatDistance(0, 2) == 1 && s.Events<BoundaryWuYiBenxiGrantedEvent>() is [{ X: 2 }],
            "Two actual phase-start discards issue one distance-minus-two grant for the original Play.");
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        s.Need(s.Events<BoundaryWuYiBenxiConsumedEvent>().Length == 0,
            "A real equipment use does not spend the next basic-or-ordinary-trick entitlement.");
        var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        var before = new[] { s.V(1).Hp, s.V(2).Hp, s.V(3).Hp };
        s.Native(slash, 1); s.Targets(); s.Answer(c => c.Targets.SequenceEqual([2])); s.Cold();
        s.Need(s.P!.Choices.Any(c => c.Targets.SequenceEqual([3])) && !s.P.Choices.Any(c => c.Targets.SequenceEqual([2])),
            "Cold keeps the original one-target Slash and its first unpaid additional target without reopening that same recipient.");
        s.Answer(c => c.Targets.SequenceEqual([3])); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
        var declared = s.Events<CardUseDeclaredEvent>().Single(e => e.CardId == slash);
        var applied = s.Events<DesignatedExtraTargetResolvedEvent>().Single(e => e.Source.SkillId == Skill);
        s.Need(applied.OriginalTargetSeats.SequenceEqual([1]) && applied.AddedTargetSeats.SequenceEqual([2, 3]) &&
            applied.ResultTargetSeats.SequenceEqual([1, 2, 3]) &&
            new[] { s.V(1).Hp, s.V(2).Hp, s.V(3).Hp }.SequenceEqual(before.Select(hp => hp - 1)) &&
            s.Events<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == declared.ResolutionId && e.Amount == 1) == 3 &&
            s.Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId) == 1 &&
            s.Events<BoundaryWuYiRewardIssuedEvent>() is [{ UseId: var rewarded }] && rewarded == declared.ResolutionId &&
            s.Events<CardsDrawnEvent>().Count(e => e.PlayerSeat == 0 && e.Count == 5) == 1,
            "All three actual Slash targets resolve in the original native use; the complete damaging card rewards one native five-card draw.");
        s.Native(s.V().Hand.First(c => c.Kind == CardKind.Slash).Id, 1); s.Play();
        s.Need(s.Events<BoundaryWuYiBenxiConsumedEvent>().Length == 1 && s.Events<BoundaryWuYiRewardIssuedEvent>().Length == 1 &&
            s.G.GetCombatDistance(0, 2) == 1,
            "The next real Slash gets neither another target grant nor another reward, while the paid distance lasts through the same actual Play.");
        s.End(); s.Offer();
        s.Need(s.G.GetCombatDistance(0, 2) == 2, "The paid reduction expires when the original actual Play ends.");
    }

    public static void NonDamagingFirstEligibleUseConsumesTheEntitlement()
    {
        var s = Scene.Start(); s.Pay(1);
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        var chain = s.V().Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        s.Native(chain, 1); s.Targets(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
        s.Need(s.Events<BoundaryWuYiBenxiConsumedEvent>() is [{ Kind: CardKind.IronChain }] &&
            s.Events<BoundaryWuYiRewardIssuedEvent>().Length == 0 && s.V(1).IsChained,
            "A genuine non-damaging ordinary trick consumes the entitlement even when its optional additional-target choice is declined.");
        s.Native(s.V().Hand.First(c => c.Kind == CardKind.Slash).Id, 1); s.Play();
        s.Need(s.Events<BoundaryWuYiRewardIssuedEvent>().Length == 0 && s.Events<BoundaryWuYiBenxiConsumedEvent>().Length == 1,
            "Later unrelated actual damage cannot turn an already-consumed non-damaging card into a five-card reward.");
    }

    private sealed class Scene(GameEngine engine, ContentRegistry registry)
    {
        public GameEngine G = engine;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Targets() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "boundary-benxi-targets"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Pay(int count)
        {
            Offer(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
            var ids = V().Hand.Where(c => c.Kind is CardKind.Dodge or CardKind.Peach).Take(count).Select(c => c.Id).ToArray();
            Need(ids.Length == count, "The fixed native scenario retains the exact chosen phase-start payment.");
            foreach (var id in ids) Answer(c => c.Cards.SequenceEqual([id]));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            Reach(() => P?.SkillPrompt?.SkillId == Hold);
            Need(Events<BoundaryWuYiBenxiGrantedEvent>().Length == 0 && ids.All(id =>
                G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile),
                "The exact real card batch pays before its movement child returns; the original phase grant is not issued early.");
            Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play();
        }
        public void Native(int id, params int[] targets)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "The exact original physical card retains a real native legal use.");
            Accept(new PlayCardCommand(0, id, action!.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.Single(predicate); Accept(new AnswerPromptCommand(0, p.PromptId, choice.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return;
                Need(P is null, "The native Benxi scene reached an unexpected human decision.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded native Benxi scene missed its next original boundary.");
        }
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold preserves the paid original phase, consumed first-use identity and unpaid native target draft.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements });
        public void Need(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P,
                Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        private void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected original Benxi command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Crossbow, Suit.Club, 1),
                new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Diamond, 8), new(CardKind.IronChain, Suit.Club, 9)];
            var setup = new ShanheBattleSetup(29, 1, "boundary-wu-yi", Hero, 4, 0, 0, 0, 0, [Quiet, Hold], hand, [],
                [new(Peer, Hp: 8) { StartingHand = [] }, new(Peer, Hp: 8) { StartingHand = [] }], [new(Peer, Hp: 8) { StartingHand = [] }], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-wu-yi-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{SkipPlay}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Hold}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["skill-program.boundary:benxi-mobile.MoveBoundCards"],"movementOccurrence":"perOwnerBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object> { [Quiet] = new { name = "奔袭固定摸牌", description = "保留原生实际阶段。" },
                [SkipPlay] = new { name = "奔袭安静同桌", description = "同桌不主动出牌。" },
                [Hold] = new { name = "奔袭已付子窗", description = "原生完整弃置后的实际移动子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "固定原生同桌", "", Quiet, "wei", 8, [SkipPlay]));
            const string deck = "fixture:boundary-wu-yi-deck";
            builder.AddDeck(new(deck, "奔袭固定原生库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "界吴懿实际原生用牌", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
