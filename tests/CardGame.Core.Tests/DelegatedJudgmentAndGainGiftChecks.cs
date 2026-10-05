using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DelegatedJudgmentAndGainGiftChecks
{
    private const string Driver = "fixture:gg-driver", Gain = "fixture:gg-gain", Hp = "fixture:gg-hp",
        Red = "fixture:gg-red", Peer = "fixture:gg-peer", Mode = "identity:classic-gain-gift-4";
    private enum Kind { Judgment, Gain, RedForeign, RedBatch }

    public static void DelegatedJudgmentPrivateChoosersAndRealHePaymentColdReplay()
    {
        var (g, r) = Start(Kind.Judgment); Equip(g); Use(g, "hurt", [], []); ReachPlay(g);
        var lion = V(g).Equipment.Single().Id; var hand = V(g).Hand.Select(c => c.Id).ToArray();
        Use(g, "judge", [], [0]); Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
        var j = g.ResolutionStack.OfType<JudgmentFrame>().Single(); var old = j.CardId!.Value;
        Require(P(g) is { PlayerSeat: 0, ValidCardIds.Count: 0 } && j.DelegatedReplacement is { Stage: DelegatedJudgmentStage.Offered } &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PrivateRevealedCards is null), "Only the owner decides whether to offer delegated viewing.");
        Cold(g, r); Reject(g); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "delegated-judgment-accept");
        Require(P(g) is { PlayerSeat: 0 } && g.ResolutionStack.OfType<JudgmentFrame>().Single().DelegatedReplacement is { Stage: DelegatedJudgmentStage.Choosing } &&
            g.CreateSnapshot(0).PrivateRevealedCards!.Select(c => c.Id).SequenceEqual(hand) && P(g)!.Choices.All(c => c.Cards.Count == 1) &&
            P(g)!.ValidCardIds.Contains(lion), "The actual subject chooses one still-owned HE entity, including real equipment; self judging still has two steps.");
        Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:huanshi");
        Require(paid.GainGiftReceipt is { Stage: GainGiftStage.ReplacementChildren, PaymentCardId: var selected } && selected == lion &&
            V(g).Hp == V(g).MaxHp && g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.ProgramJudgmentReplace) == 1 &&
            g.CardMovements.Any(m => m.CardId == old && m.To == CardLocation.DiscardPile) && g.CreateSnapshot(0).PrivateRevealedCards is null,
            "Real replacement pays the original owner's armor once, then drains its exact Silver Lion recovery; private viewing has already ended.");
        Cold(g, r); Continue(g); ReachPlay(g);
        Require(E<ProgramJudgmentReplacementResolvedEvent>(g).Single(e => e.SkillId == "ol:huanshi").ReplacementCardId == lion &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == lion).Location == CardLocation.DiscardPile, "The original judgment returns through its real result and cleanup.");
        Cold(g, r);

        var (foreign, fr) = Start(Kind.Judgment); var ownerHand = V(foreign).Hand.Select(c => c.Id).ToArray();
        Use(foreign, "judge", [], [1]); Reach(foreign, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
        Answer(foreign, c => c.Parameters.GetValueOrDefault("action") == "delegated-judgment-accept");
        Require(P(foreign) is { PlayerSeat: 1, SourceSeat: 0, TargetSeat: 1, IsPrivate: true } &&
            foreign.CreateSnapshot(1).PrivateRevealedCards!.Select(c => c.Id).SequenceEqual(ownerHand) &&
            Enumerable.Range(0, 4).Where(s => s != 1).All(s => foreign.CreateSnapshot(s).PrivateRevealedCards is null && foreign.CreateSnapshot(s).PendingDecision is null),
            "The foreign actual subject alone receives the private hand material and mandatory choices; the owner no longer chooses the card.");
        Cold(foreign, fr); Accept(foreign, new AdvanceOneStepCommand(foreign.Revision)); ReachPlay(foreign);
        Require(E<ProgramJudgmentReplacementResolvedEvent>(foreign).Single(e => e.SkillId == "ol:huanshi") is { SubjectSeat: 1, Activated: true } &&
            Enumerable.Range(0, 4).All(s => foreign.CreateSnapshot(s).PrivateRevealedCards is null), "The subject AI uses its own authorized view and private access ends after payment.");
        Cold(foreign, fr);
    }

    public static void BatchGainGiftsUseDistinctRecipientsAndActualPhaseQuota()
    {
        var (g, r) = Start(Kind.Gain, finishInitialGift: false);
        Require(P(g)!.SkillPrompt?.SkillId == "ol:hongyuan" && E<GainGiftPhaseIssuedEvent>(g).Length == 0,
            "One actual normal Draw batch of two permits optional gifting without subtracting its planned draw.");
        Cold(g, r); Answer(g, c => c.Cards.Count == 0); ReachPlay(g);
        Use(g, "two-singles", [], []); ReachPlay(g);
        Require(E<GainGiftPhaseIssuedEvent>(g).Length == 0, "Two independent one-card gain batches do not form one two-card gain.");
        Use(g, "draw-two", [], []); Reach(g, p => p.SkillPrompt?.SkillId == "ol:hongyuan"); Reject(g); Cold(g, r);
        var first = P(g)!.Choices.First(c => c.Targets.SequenceEqual([1])); Answer(g, c => c.Id == first.Id);
        Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:hongyuan").GainGiftReceipt is
            { Stage: GainGiftStage.GiftChildren, GivenTargets.Count: 1 } && E<GainGiftPaidEvent>(g).Length == 1 &&
            E<GainGiftPhaseIssuedEvent>(g).Single().Phase.Kind == ActualDiscardRecoveryPhaseKind.Play,
            "The first real transfer charges only this actual Play phase and suspends before selecting the second recipient.");
        Cold(g, r); Advance(g); Reach(g, p => p.SkillPrompt?.SkillId == "ol:hongyuan");
        Require(P(g)!.Choices.All(c => !c.Targets.Contains(1)) && P(g)!.Choices.Any(c => c.Cards.Count == 0),
            "After paid children, the first recipient is excluded and giving only one remains legal.");
        Answer(g, c => c.Targets.SequenceEqual([2])); Reach(g, p => p.SkillPrompt?.SkillId == Gain); Cold(g, r); Advance(g); ReachPlay(g);
        Use(g, "draw-two", [], []); ReachPlay(g);
        Require(E<GainGiftPaidEvent>(g).Length == 2 && E<GainGiftPhaseIssuedEvent>(g).Length == 1 &&
            E<GainGiftPaidEvent>(g).Select(e => e.TargetSeat).SequenceEqual([1, 2]), "A later two-card batch cannot reopen an already spent actual phase.");
        Cold(g, r);
        // Explicit host audit after the command-replayed prefix: reacquisition and state reset cannot erase the issued token.
        var owner = HostPlayers(g)[0]; var grant = owner.SkillGrants.Grants.Single(s => s.SkillId == "ol:hongyuan");
        owner.SkillGrants.RemoveGrant(grant.GrantId); owner.SkillGrants.Grant(new("fixture:gg-later", grant.SkillId, grant.SkillInstanceId + ":later", "acquired:host-audit"));
        ((SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", Flags)!.GetValue(g)!).ResetSkill(0, grant.SkillId);
        Use(g, "draw-two", [], []); ReachPlay(g); Require(E<GainGiftPhaseIssuedEvent>(g).Length == 1, "A new skill instance still shares the owner/skill/actual-phase quota.");
    }

    public static void RedLossFreezesOriginalEffectiveColorInForeignPlay()
    {
        var (g, r) = Start(Kind.RedForeign); Equip(g);
        Require(E<RedOwnedLossRevealedEvent>(g).Length == 0, "Hand to the same owner's Equipment is not a loss and own Play losses do not trigger.");
        while (V(g).Hand.Count > 1) { Use(g, "trim", [V(g).Hand[0].Id], []); ReachPlay(g); }
        var lost = V(g).Hand.Single(); Require(lost.Suit == Suit.Spade, "The physical card remains printed black while the original owner rewrites it to Heart.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:mingzhe"); var receipt = root.GainGiftReceipt!;
        Require(receipt is { Stage: GainGiftStage.RedDrawChildren, LossIndex: 1, RedLosses.Count: 1 } &&
            receipt.RedLosses[0].Card.Id == lost.Id && receipt.RedLosses[0].EffectiveSuit == Suit.Heart &&
            receipt.RedLosses[0].Timing is { Phase: TurnPhase.Play, PhaseActorSeat: not 0 } &&
            E<RedOwnedLossRevealedEvent>(g).Single() is { Card.Suit: Suit.Spade, EffectiveSuit: Suit.Heart } &&
            Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).PublicRevealedCards.Any(c => c.Id == lost.Id) &&
                g.CreateSnapshot(s).PrivateRevealedCards is null), "Foreign Play qualifies; the original owner's effective color stays frozen after the printed-black card enters another player's hand.");
        Cold(g, r); Reject(g); Continue(g);
        Require(E<GainGiftDrawPaidEvent>(g).Single().ActualCount == 1 &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "program.red-owned-loss.draw") == 1,
            "One shown lost entity pays one real physical draw and the gain child cannot repeat it."); Cold(g, r);
    }

    public static void RedBatchRevealsOnlyEachPaidLossAndKeepsFrozenLists()
    {
        var (g, r) = Start(Kind.RedBatch);
        while (V(g).Hand.Count > 2) { Use(g, "trim", [V(g).Hand[0].Id], []); ReachPlay(g); }
        var ids = V(g).Hand.Select(c => c.Id).ToArray(); Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Driver); foreach (var id in ids) Answer(g, c => c.Cards.SequenceEqual([id]));
        Reach(g, p => p.SkillPrompt?.SkillId == Gain); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:mingzhe");
        Require(root.GainGiftReceipt is { RedLosses.Count: 2, LossIndex: 1 } && E<RedOwnedLossRevealedEvent>(g).Length == 1 &&
            g.CreateSnapshot(2).PublicRevealedCards.Select(c => c.Id).SequenceEqual([root.GainGiftReceipt.RedLosses[0].Card.Id]),
            "One batch freezes two real losses, but only the current already-shown material enters the public view.");
        var list = (IList<RedOwnedLossMaterial>)root.GainGiftReceipt.RedLosses;
        try { list[0] = list[1]; throw new InvalidOperationException("A frozen receipt accepted observer mutation."); } catch (NotSupportedException) { }
        Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(E<RedOwnedLossRevealedEvent>(g).Length == 2 && E<GainGiftDrawPaidEvent>(g).Length == 2 &&
            E<RedOwnedLossRevealedEvent>(g).Select(e => e.Card.Id).ToHashSet().SetEquals(ids), "Each entity is shown once after the previous real draw child returns.");
        Cold(g, r); Continue(g);
        Require(E<GainGiftDrawPaidEvent>(g).All(e => e.ActualCount == 1) &&
            g.CardMovements.Count(m => m.Reason.Value == "program.red-owned-loss.draw") == 2, "Resuming the original batch never pays either draw twice."); Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Start(Kind kind, bool finishInitialGift = true)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardClassicGeneralPackage(), new Fixture(kind));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:gg-owner", g.Revision, P(g)!.PromptId));
        if (kind == Kind.Gain) { Reach(g, p => p.SkillPrompt?.SkillId == "ol:hongyuan"); if (!finishInitialGift) return (g, r); Answer(g, c => c.Cards.Count == 0); }
        ReachPlay(g); return (g, r);
    }
    private static void Equip(GameEngine g) { var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); ReachPlay(g); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, P(g)!.PromptId));
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var step = 0; step < 128; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed gain/loss fixture did not reach its boundary."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var a = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(a.Accepted, a.Error?.Message ?? "Rejected fixed gain/loss command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Cold command-prefix restore preserves exact paid frames, ledger, choices and private views.");
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "Unpublished choices do not change paid state or any viewer."); }
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IReadOnlyList<CharacterState> HostPlayers(GameEngine g) => (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Kind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-delegated-gain-loss", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var ending = kind == Kind.RedBatch ? $$"""
              ,"triggers":[{"id":"ending-discard-two","window":"turnEnding","subject":"owner","optional":false,"effects":[
                {"op":"selectOwnedCards","target":"owner","minimumCards":2,"maximumCards":2,"zones":["hand"],"resultBind":"lost-two"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"lost-two","destination":"discardPile","awaitMovementTriggers":true}]}]
              """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"judge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.fixture.gain-gift","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]},
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                {"id":"two-singles","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
                {"id":"trim","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]{{ending}}},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.batch-gain.gift","program.red-owned-loss.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"actual-hp-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Red}}","revision":1,"cardPolicies":[{"id":"freeze-owner-effective-color","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"foreign-play-take","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"takeRandomCardFromEveryOtherCharacter","target":"owner","zones":["hand"]}]}]}
            ]}
            """;
            var names = new[] { Driver, Gain, Hp, Red, Peer }.ToDictionary(id => id, id => (object)new { name = id, description = "实际判定和实体移动", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } });
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var p in c.Programs) b.AddSkill(new(p.Key, p.Key, "固定共享能力") { Program = p.Value });
            b.AddSkill(new("fixture:gg-idle", "无技能", "固定对照"));
            var primary = kind == Kind.Judgment ? "ol:huanshi" : kind == Kind.Gain ? "ol:hongyuan" : "ol:mingzhe";
            b.AddGeneral(new("fixture:gg-owner", "固定拥有者", "supporter", primary, "wu", 4,
                kind is Kind.RedForeign or Kind.RedBatch ? [Driver, Gain, Hp, Red] : [Driver, Gain, Hp], GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:gg-peer-{i}", "固定其他角色", "supporter", "fixture:gg-idle", "wei", 4,
                kind == Kind.RedForeign ? [Gain, Peer] : [Gain], GeneralGender.Male));
            b.AddDeck(new("fixture:gg-deck", "固定同类实体", 4, kind == Kind.Gain ? 2 : 0, []) { PhysicalCards = Enumerable.Range(0, 48)
                .Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "固定共享边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:gg-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:gg-owner", "fixture:gg-peer-1", "fixture:gg-peer-2", "fixture:gg-peer-3"]));
        }
    }
}
