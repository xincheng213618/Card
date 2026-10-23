using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenSimaYiChecks
{
    private const string Hero = "happy:shen-sima-yi", Renjie = "happy:renjie", Jilue = "happy:jilue", Lianpo = "happy:lianpo",
        Wansha = "happy:jilue-wansha", Peer = "fixture:happy-sima-peer", NoDraw = "fixture:happy-sima-no-draw",
        Quiet = "fixture:happy-sima-quiet", Hold = "fixture:happy-sima-draw-hold", Driver = "fixture:happy-sima-driver", Deck = "fixture:happy-sima-deck";

    public static void FourJiluePathsStrictPeachAndFirstActualTurnDrawResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.Ren == 1 && s.V(0).Hp == 3 && s.V(0).MaxHp == 3, "Happy Sima starts at 3/3 with one real Ren and initial Jilue, without awakening.");
        var keep = s.V(0).Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        var weapon = s.V(0).Equipment.Single().Id;
        var victimPeach = s.V(3).Hand.Single().Id;
        s.Use("hurt", [0]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue);
        s.Need(s.Ren == 3 && s.V(0).Hp == 1 && s.E<PlayerMarkerChangedEvent>().Count(e => e.PlayerSeat == 0 && e.Marker == PlayerMarkerKind.Ren && e.Delta == 2) == 1,
            "One actual two-point damage gains two Ren in a single original damage invocation before the paid Fangzhu offer.");
        s.Activate(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "happy-sima-first-draw") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("mode") == "draw");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.HappySimaYi is not null));
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HappySimaYi is not null);
        var native = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        s.Need(s.Ren == 2 && producer.TriggerId == "injury-exile" && producer.HappySimaYi is { Stage: HappySimaYiStage.Drawing } && native.Context.RequestedCount == 1,
            "The original one-Ren Fangzhu frame owns its accepted-first-use bonus and its one-card native draw child.");
        s.Cold(); s.Skip();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue && s.P.Choices.Any(c => c.Targets.SequenceEqual([1])));
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.Ren == 2 && s.V(0).HandCount == 4 && s.V(1).IsFaceDown && s.V(1).HandCount == 1 &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == native.Id && !e.Replaced && e.RequestedCount == 1) == 1 &&
            s.E<HappySimaJilueDrawReturnedEvent>().Count(e => e.FrameId == producer.Id && e.Actual == 1) == 1,
            "Cold neither repays Ren nor redraws the first reward; Fangzhu flips first and draws exactly one despite two lost HP.");

        var hand = s.V(0).HandCount;
        s.Card(CardKind.DrawTwo); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.Activate(); s.Play();
        s.Need(s.Ren == 1 && s.V(0).HandCount == hand + 2 && s.E<HappySimaJilueFirstOfferedEvent>().Length == 1,
            "A real ordinary trick pays another Ren for one Jizhi draw plus its original two, with no second first-use reward.");

        s.Use("kill", [3]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.Activate();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.Ren == 0 && s.Has(Wansha) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach") &&
            s.V(3).Hand.Any(c => c.Id == victimPeach),
            "The actual dying entry may buy Wansha: the current owner can still use a real Peach, while the dying other character retains its own Peach before rescue.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianpo);
        s.Need(!s.V(3).IsAlive && !s.E<DyingResponseEvent>().Any(e => e.ResponderSeat == 3 && e.UsedPeach) &&
            s.G.CardMovements.Count(m => m.CardId == victimPeach && m.From == CardLocation.Hand(3) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DeathDiscard) == 1,
            "Happy Wansha has no standard dying-self exception: the real self-Peach cannot rescue, and remains until actual death cleanup.");
        s.Activate(); s.Play();
        s.Need(s.E<ProgramExtraTurnPendedEvent>().Count(e => e.SkillId == Lianpo) == 1, "The real killer reserves a native extra turn after this actual turn.");

        var turn = s.G.State.TurnNumber;
        s.End(); s.Reach(() => s.P?.Kind == DecisionKind.DiscardCards);
        var discarded = s.P!.ValidCardIds.Where(id => id != keep).ToArray();
        s.Need(discarded.Length == s.P.RequiredCardCount && discarded.Length > 1, "The real hand-limit discard preserves only the later physical trick.");
        s.Accept(new DiscardCardsCommand(0, discarded, s.P.PromptId, s.G.Revision)); s.Play();
        s.Need(s.G.State.TurnNumber == turn + 1 && s.G.State.CurrentSeat == 0 && s.Ren == discarded.Length && !s.Has(Wansha) &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Renjie && e.BindingId == "discard-ren") == 1 &&
            s.E<PlayerMarkerChangedEvent>().Count(e => e.PlayerSeat == 0 && e.Marker == PlayerMarkerKind.Ren && e.Delta == discarded.Length) == 1,
            "One actual hand-discard batch grants its complete count once; native Lianpo starts the next owner turn and expires the preceding Wansha lease.");

        s.Use("judge", []); s.Reach(() => s.P?.Kind == DecisionKind.ProgramJudgmentReplacement);
        s.Need(s.P!.ValidCardIds.Contains(weapon), "Happy Guicai accepts a real equipment card, unlike the original OL hand-only edition.");
        s.Answer(c => c.Cards.SequenceEqual([weapon]));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "happy-sima-first-draw") == true);
        s.Need(s.Ren == discarded.Length - 1 && s.E<JudgmentReplacementResolvedEvent>().Count(e => e.OwnerSeat == 0 && e.Used && e.NewCardId == weapon && e.Reason == "fixture.happy-sima") == 1 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Jilue && f.TriggerId == "replace-judgment" && f.InstructionIndex == 2 && f.HappySimaYi is { Stage: HappySimaYiStage.Choice }),
            "The new actual turn's first paid Guicai replaces the real judgment before offering its optional bonus.");
        s.Answer(c => c.Parameters.GetValueOrDefault("mode") == "decline"); s.Play();
        s.Need(s.V(0).Equipment.Count == 0 && s.V(0).HandCount == 1 &&
            s.E<ProgramJudgmentReplacementResolvedEvent>().Count(e => e.SkillId == Jilue && e.Activated && e.ReplacementCardId == weapon) == 1,
            "Declining the accepted-first-use reward does not draw or return the paid equipment; the original judgment binding then returns exactly once.");
        s.Card(CardKind.IronChain, [0]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jilue); s.Activate(); s.Play();
        s.Need(s.Ren == discarded.Length - 2 && s.V(0).HandCount == 1 && s.E<HappySimaJilueFirstOfferedEvent>().Length == 2 &&
            s.E<HappySimaJilueFirstChosenEvent>().Count(e => !e.Draw) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Jilue && e.BindingId == "trick-used") == 2,
            "A refused first bonus still consumes the new actual turn's qualification; the later real Jizhi only draws its own one card.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-sima", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var card = CardCatalog.Get(CardKind.IronChain);
            b.AddCard(new("fixture:happy-sima-chain", card.DisplayName, card.CategoryName, card.Description, CardKind.IronChain));
            var catalog = SkillProgramCatalog.Load(Rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { NoDraw, Quiet, Hold, Driver }.ToDictionary(id => id, id => new { name = id, description = "真实付忍、原生摸牌、判定及严格完杀" }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "保留真实回合的角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定实体锦囊", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐司马懿真实濒死与付忍", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[
            {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
            {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
            {"id":"judge","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"owner","judgmentReason":"fixture.happy-sima","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]}]}
        ]}
        """;
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Ren => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Ren)?.Count ?? 0;
        public bool Has(string skill) => V(0).Skills?.Any(k => k.Id == skill) == true;
        public void Use(string binding, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void Card(CardKind kind, int[]? targets = null)
        {
            var cards = V(0).Hand.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet();
            var actions = G.GetHumanLegalActions();
            var action = actions.FirstOrDefault(a => a.ConversionSource is null && a.CardId is { } id && cards.Contains(id) &&
                (targets is null || a.TargetSeats.SequenceEqual(targets)) && a.Kind != LegalActionKind.Recast);
            if (action is null) throw new InvalidOperationException("Missing exact physical Happy Sima action " + JsonSerializer.Serialize(new { kind, targets, Hand = V(0).Hand, Actions = actions, P, G.State.TurnNumber, G.State.CurrentSeat }));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing exact Happy Sima choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 140; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Happy Sima boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missed the bounded Happy Sima flow.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains the once-paid first Jilue frame and exact native draw return.");
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Happy Sima command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Ren, Players = Enumerable.Range(0, 4).Select(V), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var peer = new ShanheCombatantDefinition(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] };
            var victim = new ShanheCombatantDefinition(Peer, Hp: 1, ExtraSkillIds: [NoDraw]) { StartingHand = [new(CardKind.Peach, Suit.Heart, 11)] };
            var setup = new ShanheBattleSetup(37, 1, "happy-sima", Hero, 3, 0, 0, 0, 0, [NoDraw, Hold, Driver],
                [new(CardKind.DrawTwo, Suit.Club, 7), new(CardKind.IronChain, Suit.Club, 9), new(CardKind.Peach, Suit.Heart, 2)],
                [new(CardKind.Crossbow, Suit.Club, 1)], [peer, victim], [peer], new string('f', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
