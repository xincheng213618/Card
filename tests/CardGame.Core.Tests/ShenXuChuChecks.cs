using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenXuChuChecks
{
    private const string Owner = "fixture:shen-xu-chu-owner", Driver = "fixture:shen-xu-chu-driver", Quiet = "fixture:shen-xu-chu-quiet", Mode = "identity:classic-shen-xu-chu-fixture";
    public static void ZhengqingActualTurnsAndMarkedDuelCold()
    {
        var (g, registry) = Start(); Play(g);
        Use(g, "two", [1]); Play(g); Use(g, "incoming-three", [1]); Play(g); Use(g, "heal", []); Play(g); Use(g, "incoming-three", [2]); Play(g); Use(g, "extra", []); Play(g);
        End(g); Play(g);
        Need(E<TurnStartedEvent>(g).Last() is { TurnNumber: 2, ActorSeat: 0 }, "The fixture actually entered an additional full turn before the round ended.");
        Use(g, "two", [1]); Play(g); End(g); Play(g);
        var settled = E<ZhengqingSettledEvent>(g).Single();
        var marked = settled.WinnerSeat;
        Need(settled is { Round: 1, MaximumDamage: 3 } && marked is 1 or 2 && Marker(g, marked) == 3 && Marker(g, 0) == 0,
            "Zhengqing compares the largest single actual turn (not extra-turn 2+2) and uses real game RNG to select one of the other tied sources of three damage.");
        Need(typeof(ZhengqingSettledEvent).GetProperties().All(p => !p.Name.StartsWith("Random", StringComparison.Ordinal)),
            "The public settled event carries its result without leaking the deterministic generator state.");
        Need(E<ShenXuChuDrawReturnedEvent>(g).Where(e => e.FrameId == settled.FrameId).Select(e => (e.Seat, e.Requested)).SequenceEqual([(marked, 1), (0, 1)]), "The other winner and Shen Xu Chu each receive one genuine native draw.");
        g = Cold(g, registry); var hp = V(g, marked).Hp; var hand = V(g, 0).HandCount;
        ConvertDodge(g, marked); Play(g);
        Need(V(g, marked).Hp == hp - 2 && V(g, 0).HandCount == hand && E<ZhuangpoTargetIssuedEvent>(g).Last().TargetMarked,
            "The enhanced converted Duel draws one and deals two to a marked target, preserving the real material debit.");
        Need(E<ShenXuChuDrawReturnedEvent>(g).Last() is { Seat: 0, Requested: 1, Actual: 1 }, "The 2026 marked-target bonus is a native draw, not an approximate hand gain.");
        Use(g, "incoming-two", [marked]); Play(g); End(g); Play(g);
        var second = E<ZhengqingSettledEvent>(g).Last();
        Need(second is { Round: 2, WinnerSeat: 0, MaximumDamage: 2 } && Marker(g, marked) == 0 && Marker(g, 0) == 2,
            "A later tied maximum containing Shen Xu Chu prefers himself without a random roll and clears the old holder first.");
        Need(CardPrintedInfo.Contains(CardKind.Dodge, PrintedCardTerms.Slash) && !CardPrintedInfo.Contains(CardKind.Peach, PrintedCardTerms.Slash) && !CardPrintedInfo.Contains(CardKind.BaguaFormation, PrintedCardTerms.Slash), "Printed-face eligibility includes Dodge but excludes Peach and Bagua, regardless of engine descriptions.");
        var replay = GameReplay.Replay(g.CreateCheckpoint().Options, g.AcceptedCommands, registry);
        Need(State(g) == State(replay), "Accepted-command replay preserves the actual-turn maximum, new round markers and real enhanced Duel.");
    }
    public static void ZhengqingSelfCapAndZhuangpoPaidDiscardCold()
    {
        var (g, registry) = Start(); Play(g); Use(g, "six", [1]); Play(g); End(g); Play(g);
        var first = E<ZhengqingSettledEvent>(g).Single();
        Need(first is { WinnerSeat: 0, MaximumDamage: 6 } && Marker(g, 0) == 6 && E<ShenXuChuDrawReturnedEvent>(g).Single(e => e.FrameId == first.FrameId) is { Requested: 5, Actual: 5 },
            "Self victory awards all six Qing while its replacement draw is capped at five.");
        var targetCards = V(g, 1).HandCount; var hp = V(g, 1).Hp;
        ConvertDodge(g, 1); Reach(g, () => Root(g)?.ShenXuChu?.Stage == ShenXuChuStage.MarkChoice && P(g)?.PlayerSeat == 0);
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "2");
        Reach(g, () => Root(g)?.ShenXuChu?.Stage == ShenXuChuStage.DiscardChoice && P(g)?.PlayerSeat == 1);
        Need(Marker(g, 0) == 4 && E<ZhuangpoMarksPaidEvent>(g).Single().Removed == 2, "Zhuangpo pays the exact two real Qing before the target chooses its own cards.");
        g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1); g = Cold(g, registry);
        Need(Root(g)!.ShenXuChu!.Selected.Count == 1 && P(g)?.PlayerSeat == 1, "Cold recovery retains the target's first selected discard without repaying Qing or changing the chooser.");
        Answer(g, c => c.Cards.Count == 1); Play(g);
        Need(Marker(g, 0) == 4 && V(g, 1).HandCount == targetCards - 2 && V(g, 1).Hp == hp - 1 && E<ZhuangpoMarksPaidEvent>(g).Count() == 1 && E<ZhuangpoDiscardPaidEvent>(g).Single().Count == 2,
            "The target genuinely discards two before the ordinary one-damage Duel; restoring a partially selected cost does not repay it.");
        Need(g.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:zhuangpo.discard") == 2,
            "Zhuangpo uses real owned logical discards rather than subtracting hand counts.");
        End(g); Play(g);
        var second = E<ZhengqingSettledEvent>(g).Last();
        Need(second is { Round: 2, WinnerSeat: 0, MaximumDamage: 1 } && Marker(g, 0) == 1 && E<ShenXuChuDrawReturnedEvent>(g).Single(e => e.FrameId == second.FrameId) is { Requested: 1, Actual: 1 },
            "The 2026 self replacement still applies when the new round's maximum is below the old record of six.");
        _ = Cold(g, registry);
    }
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenXuChu is not null);
    private static int Marker(GameEngine g, int seat) => V(g, seat).Markers?.Where(m => m.Kind == PlayerMarkerKind.QingBanner).Sum(m => m.Count) ?? 0;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void ConvertDodge(GameEngine g, int target)
    {
        var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "anniversary:zhuangpo" && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.Contains(target));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 260; i++)
        {
            if (done()) return; var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "select-god-faction")) Answer(g, c => c.Parameters.GetValueOrDefault("faction-id") == "wei");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Xu Chu native boundary not reached: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Movements = g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold restore preserves the original Xu Chu frame, exact draw entitlements and paid discard cursor."); return restored; }
    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-xu-chu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Driver}}","revision":1,"activations":[{"id":"two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},{"id":"six","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]},{"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":3}]},{"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},{"id":"incoming-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":3}]},{"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]}]}""";
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同桌", description = "跳过出牌阶段。" }, [Driver] = new { name = "真实回合伤害", description = "通过原生伤害与额外回合边界验证统计。" } } });
            var c = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Owner, "神许褚夹具", "supporter", "anniversary:zhengqing", "god", 5, ["anniversary:zhuangpo", Driver], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:shen-xu-chu-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "安静同桌", "supporter", Quiet, "wei", 20));
            b.AddDeck(new("fixture:shen-xu-chu-deck", "闪实体牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 128).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "神许褚原生夹具", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:shen-xu-chu-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
