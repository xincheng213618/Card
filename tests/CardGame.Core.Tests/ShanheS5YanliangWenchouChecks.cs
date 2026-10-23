using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5YanliangWenchouChecks
{
    private const string Skill = StandardShanheS5YanliangWenchouPackage.ShuangxiongId;
    private const string Driver = "fixture:shanhe-sx-driver", Extra = "fixture:shanhe-sx-extra",
        PayWatch = "fixture:shanhe-sx-paid", GainWatch = "fixture:shanhe-sx-gain", Quiet = "fixture:shanhe-sx-quiet";
    private const string Mode = "identity:classic-shanhe-sx", Cost = "program.paid-color-conversion.discard",
        Claim = "program.actual-turn-damage-entity.claim";

    public static void ExtraDrawRoundCountNativeDuelAndNextRoundReset()
    {
        var s = Scene.Start(backDamage: false); s.PayTwoDrawPhases();
        var paid = s.E<ShanheS5ShuangxiongPaidEvent>();
        Need(paid.Length == 2 && paid.All(p => p.RoundNumber == 1 && p.ActualTurnNumber == 1) &&
            paid.Select(p => p.FrozenDamage).SequenceEqual([1, 2]) && paid.Select(p => p.ProgramFrameId).Distinct().Count() == 2,
            "A real inserted Draw phase and the ordinary Draw phase each pay once and freeze X=1 then X=2 in the same true round.");
        var targetHp = s.V(1).Hp; var duel = s.BeginDuel(); s.Play();
        var issued = s.E<ShanheS5ShuangxiongDuelIssuedEvent>().Single();
        Need(issued.FrozenDamage == 2 && issued.ProducerFrameId == paid[1].ProgramFrameId &&
            s.E<CardUseDeclaredEvent>().Single(e => e.ResolutionId == issued.UseFrameId) is { CardKind: CardKind.Duel, SourceSeat: 0 } &&
            s.E<DamageAppliedEvent>().Single(e => e.SourceCardUseResolutionId == issued.UseFrameId) is
                { SourceSeat: 0, TargetSeat: 1, Amount: 2 } && s.V(1).Hp == targetHp - 2 &&
            s.G.CardMovements.Count(m => m.CardId == duel.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The latest paid opposite-color conversion issues an actual native physical Duel whose base damage is the frozen round count, then cleans its material exactly once.");
        s.Cold(); s.End(); s.AtOffer(); s.Pay(); s.Play();
        var reset = s.E<ShanheS5ShuangxiongPaidEvent>().Last();
        Need(reset.RoundNumber == 2 && reset.FrozenDamage == 1 && reset.ActualTurnNumber > paid[1].ActualTurnNumber &&
            s.E<ShanheS5ShuangxiongPaidEvent>().Length == 3,
            "The next actual round starts at X=1 without losing or rewriting the prior round's real payments.");
        s.Cold();
    }

    public static void PaidChildColdNativeBackDamageAndCausalEndingClaim()
    {
        var s = Scene.Start(backDamage: true); s.PayTwoDrawPhases();
        var hp = s.V().Hp; var duel = s.BeginDuel();
        s.Reach(p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        var issued = s.E<ShanheS5ShuangxiongDuelIssuedEvent>().Single();
        var responses = s.E<CardActionAcceptedEvent>().Where(e => e.Action.ActorSeat == 1 &&
            e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash)
            .SelectMany(e => e.Action.PhysicalCards).Select(c => c.CardId).ToArray();
        Need(responses.Length > 0 && issued.FrozenDamage == 2,
            "The real opponent pays a physical Slash to answer the X=2 converted native Duel.");
        s.Cold(); s.Pass(); s.Play();
        var causal = s.E<ActualTurnCardDamageEntityEvent>().Single(e => e.CausalFrameId == issued.UseFrameId);
        Need(s.E<DamageAppliedEvent>().Single(e => e.SourceCardUseResolutionId == issued.UseFrameId) is
                { SourceSeat: 1, TargetSeat: 0, Amount: 2 } && s.V().Hp == hp - 2 &&
            causal.CardId == duel.CardId && causal.CardActorSeat == 0 && causal.DamageSourceSeat == 1 &&
            responses.All(id => id != causal.CardId),
            "The same frozen Duel base applies to native back-damage, whose causal entity is the original converted card rather than a response Slash.");
        s.End(); s.Reach(p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == GainWatch);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
        Need(root.ActualTurnDamageClaim!.CardIds.SequenceEqual([duel.CardId!.Value]) &&
            s.V().Hand.Any(c => c.Id == duel.CardId) && s.E<ActualTurnDamageEntityClaimedEvent>().Single().OriginalDamageFrameId == causal.DamageFrameId,
            "The independent mandatory Ending branch truly obtains only this turn's original physical damage entity before its gained-card child pauses.");
        s.Cold(); s.Continue(); s.Until(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0));
        Need(s.E<ShanheS5ShuangxiongPaidEvent>().Length == 2 && s.E<ShanheS5ShuangxiongDuelIssuedEvent>().Length == 1 &&
            s.E<ActualTurnDamageEntityClaimedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.CardId == duel.CardId && m.Reason.Value == Claim) == 1,
            "Cold returns retain the original paid color, use, X and one causal entity claim without repaying or duplicate cleanup.");
        s.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        private PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native Shuangxiong input rejected."); }
        private void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<PendingDecision, bool> predicate)
        { for (var i = 0; i < 200; i++) { if (P is { } p && predicate(p)) return; Advance(); } throw new InvalidOperationException("Native S5 Shuangxiong boundary missing: " + JsonSerializer.Serialize(P)); }
        public void Until(Func<bool> done)
        { for (var i = 0; i < 200; i++) { if (done()) return; Advance(); } throw new InvalidOperationException("Native S5 Shuangxiong continuation missing."); }
        private void Advance()
        {
            if (P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (P is { PlayerSeat: 0 } q && q.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue();
            else if (P is { PlayerSeat: 0 } r && r.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass")) Pass();
            else Accept(new AdvanceOneStepCommand(G.Revision));
        }
        public void AtOffer() => Reach(p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Skill &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Pay()
        {
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Cards.Count == 1));
            var black = V().Hand.First(c => c.Suit is Suit.Spade or Suit.Club);
            Answer(c => c.Cards.SequenceEqual([black.Id]));
        }
        public void PayTwoDrawPhases()
        {
            AtOffer(); Pay(); Reach(p => p.SkillPrompt?.SkillId == PayWatch);
            var root = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
            Need(root.PaidColorConversion is { Granted: false } paid && paid.Origin.ActualTurnNumber == 1 &&
                E<ShanheS5ShuangxiongPaidEvent>().Single().FrozenDamage == 1 &&
                G.CardMovements.Count(m => m.Reason.Value == Cost) == 1 && !E<CardConversionGrantedEvent>().Any(e => e.Conversion.PaidColorOrigin is not null),
                "The first actual HE payment freezes X before its movement child; the original owning frame has not yet granted the conversion.");
            Cold(); Continue(); AtOffer(); Pay(); Play();
            Need(E<ProgramPaidColorConversionPaidEvent>().Length == 2 && G.CardMovements.Count(m => m.Reason.Value == Cost) == 2,
                "Returning from the first paid child pays no duplicate cost before the second actual completed Draw phase.");
        }
        public void Play() => Reach(p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
        public LegalAction BeginDuel()
        {
            var duel = G.GetLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeat == 1 && a.ConversionSource?.SkillId == Skill);
            Need(V().Hand.Single(c => c.Id == duel.CardId).Suit is Suit.Heart or Suit.Diamond,
                "The current grant consumes only a real opposite-color material.");
            Need(G.PredictPublicActionDamage(0, duel, 1).DamageAmount == 2,
                "The public native action prediction uses the same frozen X=2 before committing this viewer's own legal Duel.");
            Accept(new PlayCardCommand(0, duel.CardId!.Value, [1], G.Revision, P!.PromptId, duel.PlayedCardKind)
                { ConversionSource = duel.ConversionSource, AdditionalConversionSources = duel.AdditionalConversionSources });
            return duel;
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Pass() => Answer(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))).ToArray(),
            Frames = JsonSerializer.Serialize(G.ResolutionStack), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands), Zones = G.CreateCardZoneDiagnostics() });
        public void Cold()
        { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "All private views, native frames, paid round facts, movements and command history cold-restore identically."); }
        public static Scene Start(bool backDamage)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5YanliangWenchouPackage(), new Fixture(backDamage));
            var s = new Scene(GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
                HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = true,
                AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r), r);
            s.Accept(new StartGameCommand()); s.Reach(p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
            s.Accept(new SelectGeneralCommand(0, "fixture:shanhe-sx-owner", s.G.Revision, s.P!.PromptId)); return s;
        }
    }

    private sealed class Fixture(bool backDamage) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-shuangxiong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}]},
              {"id":"{{Extra}}","revision":1,"triggers":[{"id":"extra-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"draw","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{PayWatch}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["{{Cost}}"],"movementOccurrence":"perOwnerBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{GainWatch}}","revision":1,"triggers":[{"id":"claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{Claim}}"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Extra, PayWatch, GainWatch, Quiet })
                labels[id] = id is PayWatch or GainWatch
                    ? new { name = "实际付款子窗", description = "真实移牌后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    : (object)new { name = "固定实际小局", description = "真实额外摸牌与普通流程" };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
                { Program = program, ProgramPresentation = c.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:shanhe-sx-owner-weight" : "fixture:shanhe-sx-other-weight", "固定选将", "正式公开角色权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:shanhe-sx-owner", "专版双雄来源", "supporter", Skill, "qun", 12,
                [Driver, Extra, PayWatch, GainWatch, "fixture:shanhe-sx-owner-weight"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:shanhe-sx-other-{i}", "原生决斗对手", "supporter", Quiet, "wei", 8,
                ["fixture:shanhe-sx-other-weight"]) { InitialHp = 6 });
            b.AddDeck(new("fixture:shanhe-sx-deck", "固定原生双色实体", 4, 2, [])
                { PhysicalCards = Enumerable.Range(0, 144).Select(i => new ContentDeckPhysicalCard(backDamage ? "standard:slash" : "standard:dodge",
                    i % 2 == 0 ? Suit.Club : Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "专版双雄原生小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:shanhe-sx-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:shanhe-sx-owner", "fixture:shanhe-sx-other-1", "fixture:shanhe-sx-other-2", "fixture:shanhe-sx-other-3"]));
        }
    }
}
