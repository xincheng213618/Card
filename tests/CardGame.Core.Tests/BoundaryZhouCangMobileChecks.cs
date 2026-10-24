using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class BoundaryZhouCangMobileChecks
{
    private const string Hero = "boundary:zhou-cang-mobile", Skill = "boundary:zhongyong-mobile", Quiet = "fixture:boundary-zhou-cang-quiet",
        Skip = "fixture:boundary-zhou-cang-skip", Hold = "fixture:boundary-zhou-cang-paid", Peer = "fixture:boundary-zhou-cang-peer";
    private const string Reason = "skill-program.boundary-zhou-cang.zhongyong";

    public static void AcquireOriginalSlashAndDodgeThenOptionalGiftWithCold()
    {
        var s = Scene.Start(dodge: false); s.Play();
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        var hp = s.V(3).Hp; s.Native(slash, 3); s.Offer(); s.Activate(); s.Held();
        var original = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        s.Need(original.BoundaryZhongyong is { Stage: BoundaryZhongyongStage.AcquireSlashChildren, Paid: true } &&
            original.PendingMovementContinuation is not null && s.Zone(slash) == CardLocation.Hand(0) && s.V(3).Hp == hp - 1 &&
            s.Facts<BoundaryZhongyongUseBannedEvent>().Single().CardId == slash,
            "An undodged Slash resolves its native damage, then its original completed-use frame acquires and bans that exact physical entity before its movement child returns.");
        s.Cold(); s.Release(); s.Play();
        s.Need(s.V().Hand.Any(c => c.Id == slash) && !s.G.GetHumanLegalActions().Any(a => a.CardId == slash) &&
            s.G.CardMovements.Count(m => m.CardId == slash && m.Reason.Value == Reason) == 1,
            "Cold returns once to the original Slash without repaying or allowing the acquired physical Slash despite Crossbow's unlimited use count.");
        s.End(); s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.CardId == slash && a.TargetSeats.SequenceEqual([3])),
            "The obtained-entity use prohibition expires at the end of the actual turn.");

        s = Scene.Start(dodge: true); s.Play();
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var dodge = s.V(3).Hand.Single().Id;
        s.Native(slash, 3); s.Offer(); s.Activate(); s.Menu();
        s.Need(!s.P!.Choices.Any(c => c.Targets.Contains(0) || c.Targets.Contains(3)),
            "The other recipient excludes the Slash's user and actual responding target.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "acquire-dodge"); s.Held(); s.Cold(); s.Release(); s.Menu();
        s.Need(s.Zone(dodge) == CardLocation.Hand(0) && s.Facts<BoundaryZhongyongUseBannedEvent>().Single().CardId == dodge &&
            s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "give-slash" && c.Cards.SequenceEqual([slash]) && c.Targets.SequenceEqual([2])),
            "An actual Dodge response is acquired once, prohibited as a Longdan use material, and returns to the same optional original-Slash gift branch.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "give-slash" && c.Targets.SequenceEqual([2])); s.Held(); s.Release(); s.Play();
        s.Need(s.Zone(slash) == CardLocation.Hand(2) && s.Zone(dodge) == CardLocation.Hand(0) &&
            s.Facts<BoundaryZhongyongBonusEvent>().Length == 0 && s.Facts<NextSlashDamageReservedEvent>().Length == 0 &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId == dodge) && s.V(3).Hp == 8,
            "Acquiring the responding Dodge then gifting the original Slash does not grant the other branch's bonus; obtained Dodge cannot be used through Longdan in this turn.");
    }

    public static void GiveRespondingDodgeGrantsOneExtraSlashAndOnlyNextDamage()
    {
        var s = Scene.Start(dodge: true); s.Play();
        var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var dodge = s.V(3).Hand.Single().Id;
        s.Native(slash, 3); s.Offer(); s.Activate(); s.Menu();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "give-dodge" && c.Targets.SequenceEqual([2])); s.Held();
        s.Need(s.Zone(dodge) == CardLocation.Hand(2) && s.Facts<BoundaryZhongyongBonusEvent>().Length == 0,
            "The real responding Dodge reaches its other recipient before the original movement child returns, and before either bonus is granted.");
        s.Cold(); s.Release(); s.Play();
        s.Need(s.Facts<BoundaryZhongyongBonusEvent>() is [{ Owner: 0, Recipient: 2 }] &&
            s.Facts<NextSlashDamageReservedEvent>() is [{ OwnerSeat: 0, Amount: 1 }] && s.Facts<BoundaryZhongyongUseBannedEvent>().Length == 0 &&
            s.Zone(slash) == CardLocation.DiscardPile && s.G.CardMovements.Count(m => m.CardId == dodge && m.Reason.Value == Reason) == 1,
            "Paid-child cold grants the bonus exactly once, does not acquire the Slash, and does not prohibit the gift recipient's cards.");
        var next = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var hp = s.V(3).Hp;
        s.Native(next, 3); s.Offer(); s.SkipOffer(); s.Play();
        var use = s.Facts<CardUseDeclaredEvent>().Single(e => e.CardId == next);
        s.Need(s.V(3).Hp == hp - 2 && s.Facts<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == use.ResolutionId && e.Amount == 2) &&
            s.Facts<NextSlashDamageConsumedEvent>() is [{ Amount: 1 }] &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The second native Slash uses the one extra quota and consumes the reserve on that whole use; a third Slash is unavailable.");
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id); s.Play();
        next = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; hp = s.V(3).Hp;
        s.Native(next, 3); s.Offer(); s.SkipOffer(); s.Play();
        s.Need(s.V(3).Hp == hp - 1 && s.Facts<NextSlashDamageConsumedEvent>().Length == 1,
            "Crossbow permits a later actual Slash, which deals ordinary damage after the one next-Slash reserve is consumed.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision ?? Enumerable.Range(1, 3)
            .Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p?.SkillPrompt?.SkillId == Hold);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public T[] Facts<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Menu() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "boundary-zhongyong"));
        public void Held() => Reach(() => P?.SkillPrompt?.SkillId == Hold);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void SkipOffer() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Release() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Native(int id, params int[] targets)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "The fixed scene retains its exact native legal physical use.");
            Accept(new PlayCardCommand(0, id, action!.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.Single(predicate); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return;
                Need(P is null, "The native Zhongyong scene reached an unexpected human prompt.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded original native Zhongyong boundary was not reached.");
        }
        public void Cold()
        {
            var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == state, "Cold preserves the original completed use, once-paid request, immutable movement invoice and actual child prompt.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements });
        public void Need(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, G.State.Phase, P,
                Origins = Facts<BoundaryZhongyongUseOriginEvent>(), EntityOrigins = Facts<BoundaryZhongyongEntityOriginEvent>(), Movements = G.CardMovements,
                Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Last = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        private void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected original Zhongyong command."); }
        public static Scene Start(bool dodge)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Diamond, 8), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Crossbow, Suit.Club, 1)];
            var setup = new ShanheBattleSetup(29, 1, "boundary-zhou-cang", Hero, 4, 0, 0, 0, 0, [Quiet, Hold, "classic:longdan"], hand, [],
                [new(Peer, Hp: 8) { StartingHand = [] }, new(Peer, Hp: 8) { StartingHand = dodge ? [new(CardKind.Dodge, Suit.Heart, 2)] : [] }],
                [new(Peer, Hp: 8) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-zhou-cang-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero, "classic:zhao-yun");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{Skip}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Hold}}","revision":1,"triggers":[{"id":"paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object> { [Quiet] = new { name = "忠勇固定摸牌", description = "保留原生实际阶段。" },
                [Skip] = new { name = "忠勇安静同桌", description = "同桌不主动出牌。" },
                [Hold] = new { name = "忠勇已付子窗", description = "在真实实体移动后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "忠勇固定原生同桌", "", Quiet, "wei", 8, [Skip, Hold]));
            const string deck = "fixture:boundary-zhou-cang-deck";
            b.AddDeck(new(deck, "忠勇固定库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "界周仓实际原生用牌", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
