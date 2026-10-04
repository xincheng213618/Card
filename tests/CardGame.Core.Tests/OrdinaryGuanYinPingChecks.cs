using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryGuanYinPingChecks
{
    private const string Mode = "identity:classic-guan-yin-ping-fixture", Driver = "fixture:gyp-driver";
    private const string Hp = "fixture:gyp-hp", Gain = "fixture:gyp-gain", Chain = "fixture:gyp-chain";
    private const string Snow = "ol:xuehen", Roar = "ol:huxiao", Awake = "ol:wuji";

    public static void RedEquipmentCostFinishesBeforeLostHpTargetsAndRealFireDraw()
    {
        foreach (var snowLoss in new[] { false, true })
        {
        var (g, r) = Create(equipment: true, snowLoss: snowLoss); Play(g); Use(g, "equip", [0]); Play(g);
        var armor = g.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var hpBefore = g.State.Players[0].Hp; var lostBefore = g.State.Players[0].MaxHp - hpBefore;
        Require(lostBefore == 2, "The actual Lord fixture starts with exactly two lost HP.");
        Use(g, "red-cost-chain-fire", skill: Snow); Reach(g, p => p.SkillPrompt?.SkillId == Snow && Action(p, "select-owned-cards"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Snow);
        Require(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([armor])), "A real red equipment entity is a legal exact owned cost.");
        Private(g); g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Cards.SequenceEqual([armor])));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.State.Players[0].Hp == hpBefore + 1 && !E<ProgramChainedStateSetEvent>(g).Any(e => e.FrameId == root.Id) &&
            g.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id),
            "The actual Silver Lion cost and recovery suspend the original program before X or any chain mutation.");
        g = Cold(g, r); Continue(g); if (snowLoss) g = FinishActualSkillReplacementChild(g, r, Hp, Snow);
        Reach(g, p => p.SkillPrompt?.SkillId == Snow && Action(p, "select-targets"));
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root.Id).LostHpChainedPayment is { CardId: var paidId } && paidId == armor,
            "The real red discard receipt survives recovery children and accepted-journal reconstruction before selecting X.");
        Require(P(g)!.Choices.All(c => c.Targets.Count <= 1) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])) &&
            !P(g)!.Choices.Any(c => c.Targets.Count == lostBefore), "X is frozen from actual post-payment HP, with minimum one.");
        g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Targets.SequenceEqual([1])));
        Reach(g, p => p.SkillPrompt?.SkillId == Chain);
        Require(g.State.Players[1].IsChained && !E<DamageAppliedEvent>(g).Any(), "Entering chain is set true and its real child finishes before damage.");
        g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Snow && Action(p, "strategic-choice"));
        Answer(g, P(g)!.Choices.Single(c => c.Targets.SequenceEqual([1]))); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var fire = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FireTargetBenefit is not null);
        var receipt = fire.FireTargetBenefit!;
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == fire.Id);
        Require(receipt.TargetSeat == 1 && receipt.DrawCount == 1 && receipt.GrantSequence == 0 &&
            !E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Any(e => e.Allowance.ParentFrameId == fire.Id) && movement.Batch.Movements is [var draw] &&
            draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) &&
            movement.Batch.OriginSkillId == Roar && movement.Batch.OriginSkillInstanceId == fire.SkillInstanceId &&
            (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == fire.Id),
            "Fire damage gives its actual recipient one real draw under the original damage candidate and owning receipt.");
        Freeze(fire.SelectedTargetSeats); g = Cold(g, r); Continue(g); Play(g);
        Require(E<ProgramFireTargetDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == fire.Id) == 1 &&
            E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Count(e => e.Allowance.ParentFrameId == fire.Id) == 1 &&
            E<ProgramSkillResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed &&
            g.CardMovements.Count(m => m.CardId == armor && m.To == CardLocation.DiscardPile) == 1,
            "The exact red cost, chained child, real damage, draw and issued turn right finish once after restored continuations.");
        Require(E<ProgramLostHpChainedCostPaidEvent>(g).Count(e => e.ProgramFrameId == root.Id) == 1 &&
            (!snowLoss || !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Snow)),
            "The exact paid seven-node activation keeps its promised chain/fire tail after actual Snow loss, with no second red payment.");
        }
    }

    public static void FireRecipientQuotaKeepsRealUseDebitsAndExpiresOnActualTurnEnd()
    {
        var (g, r) = Create(); Play(g); Use(g, "fire", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var pendingRoot = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FireTargetBenefit is not null);
        Require(!E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Any(e => e.Allowance.ParentFrameId == pendingRoot.Id),
            "No target right is issued or usable while the genuine draw gain child is paused.");
        g = Cold(g, r); Continue(g); Play(g);
        var right = E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Single().Allowance;
        Require(right.Source.OwnerSeat == 0 && right.TargetSeat == 1 && right.TurnSeat == 0 && right.TurnNumber == 1,
            "The actual damage issues a direction-specific right for the original actor and actual turn.");
        g = Cold(g, r);
        Slash(g, 1, converted: true); Play(g);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(3)) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])),
            "The first real converted Slash spends normal quota; only the covered target bypasses the finite limit.");
        Slash(g, 1); Play(g);
        Require(E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 0 && e.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == 2 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Use &&
                e.Action.EffectiveKind == CardKind.Slash && e.Action.ConversionChain.Any(s => s.SkillId == "classic:wusheng")) &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(3)),
            "Two genuine physical/converted uses remain normal recorded uses and do not replenish another target's quota.");
        g = Cold(g, r); End(g); NextOwnPlay(g);
        Slash(g, 3); Play(g);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(1)) &&
            E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Count(e => e.Allowance.GrantSequence == right.GrantSequence) == 1,
            "The old right expires at its real turn end, so the next turn's spent normal quota applies to the old recipient again.");
        g = Cold(g, r);
    }

    public static void AwakeningGetsOriginalPublicWeaponAndDrainsPaidGainAfterSourceLoss()
    {
        foreach (var sourceLoss in new[] { false, true })
        {
            var (g, r) = Create(weapon: true, sourceLoss: sourceLoss); Play(g); Use(g, "equip", [1]); Play(g);
            var weapon = g.CreateSnapshot(0).Players[1].Equipment.Single(c => c.Kind == CardKind.QinglongCrescentBlade).Id;
            var max = g.State.Players[0].MaxHp; var hp = g.State.Players[0].Hp;
            Use(g, "fire-three", [2]); Play(g); End(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            Require(g.State.Players[0].MaxHp == max + 1 && g.State.Players[0].Hp == hp + 1 &&
                E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.Amount == 3),
                "Three actual dealt damage, rather than bare card-use history, triggers the game-limited maximum-HP/recovery awakening.");
            g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Awake && Action(p, "named-card-acquisition"));
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.NamedCardAcquisition is not null);
            Require(E<ProgramSelectedSkillsLostEvent>(g).Single(e => e.ProgramFrameId == root.Id).RemovedGrantCount == 1 &&
                P(g)!.Choices.Any(c => c.Cards.SequenceEqual([weapon])) && P(g)!.Choices.Any(c => c.Cards.Count == 0),
                "Huxiao is really lost; public field entities and a hidden-ID-free deck-kind option are available under the original awakening.");
            g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Cards.SequenceEqual([weapon])));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            var issued = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root.Id).NamedCardAcquisition!;
            Require(issued.CardId == weapon && issued.From == CardLocation.Equipment(1) &&
                g.CardMovements.Count(m => m.CardId == weapon && m.From == CardLocation.Equipment(1) && m.To == CardLocation.Hand(0)) == 1,
                "The printed original weapon is moved once, without creating a replacement entity or reading a foreign hand.");
            g = Cold(g, r); Continue(g); if (sourceLoss) g = FinishActualSkillReplacementChild(g, r, Gain, Awake);
            NextOwnPlay(g);
            Require(E<ProgramNamedCardAcquiredEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.Obtained) == 1 &&
                g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == weapon) &&
                E<ProgramBindingResolvedEvent>(g).Single(e => e.FrameId == root.Id).Completed == !sourceLoss &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id),
                "Already-issued gain children drain after real source loss; the original weapon is preserved and the exact binding returns once.");
            if (!sourceLoss) { Use(g, "fire-three", [2]); Play(g); End(g); NextOwnPlay(g);
                Require(g.State.Players[0].MaxHp == max + 1 && E<ProgramSelectedSkillsLostEvent>(g).Count(e => e.InitiatingSkillId == Awake) == 1,
                    "The actual game usage prevents a second awakening on the next qualifying ending."); }
        }
    }

    public static void PaidFireDrawKeepsIssuedRightAfterLossAndNativeActorCanResolveIt()
    {
        var (g, r) = Create(roarLoss: true); Play(g); Use(g, "normal", [1]); Play(g);
        Require(E<ProgramFireTargetDrawIssuedEvent>(g).Length == 0, "Actual normal damage does not issue a fire-only benefit.");
        Use(g, "fire", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Gain); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FireTargetBenefit is not null);
        Require(root.FireTargetBenefit!.GrantSequence == 0 && !E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Any(e => e.Allowance.ParentFrameId == root.Id),
            "The issued draw promise grants no early quota inside its exact gain child.");
        g = Cold(g, r); Continue(g); g = FinishActualSkillReplacementChild(g, r, Gain, Roar); Play(g);
        Require(E<TurnTargetCardQuotaAllowanceGrantedEvent>(g).Count(e => e.Allowance.ParentFrameId == root.Id) == 1 && !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Roar) &&
            E<ProgramFireTargetDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.ActualCount == 1) == 1 &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id),
            "A real gain child removes the issuer; the already-issued draw still drains and returns without paying twice.");
        Slash(g, 1); Play(g); Slash(g, 1); Play(g);
        Require(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])),
            "The already-issued actual-turn right remains after Huxiao is suppressed or lost.");
        var (native, nr) = Create(native: true);
        for (var step = 0; step < 100 && E<ProgramFireTargetDrawIssuedEvent>(native).Length == 0; step++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(E<ProgramFireTargetDrawIssuedEvent>(native).Length > 0 && native.State.Players.All(p => !p.IsHuman),
            "A native actor really issues the mandatory fire benefit through a small actual-command prefix.");
        native = Cold(native, nr);
        for (var step = 0; step < 40 && native.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.FireTargetBenefit is not null); step++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(!native.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.FireTargetBenefit is not null),
            "The native child and owning receipt return without a human-only operation path.");
    }

    private static GameEngine FinishActualSkillReplacementChild(GameEngine g, ContentRegistry r, string observerSkill, string lostSkill)
    {
        Reach(g, p => p.SkillPrompt?.SkillId == observerSkill && Action(p, "advanced-lifecycle"));
        Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("advanced-value") == lostSkill),
            "The actual replacement producer publishes the exact currently-owned original skill.");
        g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("advanced-value") == lostSkill));
        g = Cold(g, r); Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == lostSkill),
            "The real replacement producer removes the selected source; no host state injection is used.");
        return g;
    }

    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Answer(GameEngine g, PromptChoice c) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, c.Id, g.Revision));
    private static void Continue(GameEngine g) => Answer(g, P(g)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null, string skill = Driver) =>
        Accept(g, new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target, bool converted = false)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) &&
            (converted ? a.ConversionSource?.SkillId == "classic:wusheng" : a.ConversionSource is null));
        var id = a.CardId ?? throw new InvalidOperationException("A true entity Slash is required.");
        Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void NextOwnPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 140; step++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p is not null && p.PlayerSeat == 0)
            {
                if (p.Kind == DecisionKind.ProgramTrigger) Answer(g, p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "decline") ?? p.Choices[0]);
                else if (p.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification)
                    Answer(g, p.Choices.Single(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("program-action") is null));
                else throw new InvalidOperationException("Unexpected human boundary " + p.Kind + "/" + p.SkillPrompt?.SkillId);
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Small fixed prefix did not reach the exact boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Accepted-journal cold prefix preserves four viewer projections, exact frames, movements and facts."); return restored; }
    private static void Private(GameEngine g) { var p = P(g)!; Require(p.IsPrivate, "The owned red-card choice is private.");
        for (var i = 1; i < 4; i++) Require(g.CreateSnapshot(i).PendingDecision is null, "Foreign viewers cannot inspect the red-cost hand identities."); }
    private static void Freeze<T>(IReadOnlyList<T> list) { if (list.Count > 0 && list is IList<T> mutable) { try { mutable[0] = list[0]; throw new InvalidOperationException("Mutable exposed collection."); } catch (NotSupportedException) { } } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool weapon = false, bool sourceLoss = false, bool roarLoss = false, bool native = false, bool snowLoss = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(equipment, weapon, sourceLoss, roarLoss, snowLoss));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0,
            HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
            Accept(g, new SelectGeneralCommand(0, "fixture:gyp-owner", g.Revision, P(g)!.PromptId)); } return (g, r);
    }
    private sealed class Fixture(bool equipment, bool weapon, bool sourceLoss, bool roarLoss, bool snowLoss) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-guan-yin-ping", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:gyp-driver","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"fire-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3,"nature":"fire"}]},
                {"id":"normal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"fixture:gyp-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:gyp-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:gyp-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"any","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.fire-target-benefit.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:gyp-chain","revision":1,"triggers":[{"id":"chain","window":"characterEnteredChain","subject":"any","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (weapon) rules["skills"]![3]!["triggers"]![0]!["movementReasons"] = JsonNode.Parse("""["skill-program.ol:wuji.LoseSkillsAndObtainNamedCard"]""");
            if (sourceLoss || roarLoss) rules["skills"]![3]!["triggers"]![0]!["effects"]!.AsArray().Add(new JsonObject
            { ["op"] = "replaceSkillsOnAwakening", ["target"] = "owner", ["skillIds"] = new JsonArray(JsonValue.Create("fixture:gyp-noop")) });
            if (snowLoss) rules["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray().Add(new JsonObject
            { ["op"] = "replaceSkillsOnAwakening", ["target"] = "owner", ["skillIds"] = new JsonArray(JsonValue.Create("fixture:gyp-noop")) });
            var ids = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var descriptions = ids.ToDictionary(id => id, id => id is Hp or Gain or Chain ? (object)new { name = id, description = "真实子结算选择", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : new { name = id, description = "固定真实命令" });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in ids) b.AddSkill(new(id, id, "真实夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:gyp-noop", "失源后的替换", "No program"));
            b.AddSkill(new("fixture:gyp-pick-owner", "固定主人", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 10000d : -10000d) });
            b.AddSkill(new("fixture:gyp-pick-other", "固定其他", "Passive") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r != Role.Lord ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:gyp-owner", "关银屏共享能力", "supporter", Snow, "shu", 3, [Roar, Awake, Driver, "classic:wusheng", Hp, Gain, Chain, "fixture:gyp-pick-owner"], GeneralGender.Female) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:gyp-other-{i}", "其他角色", "supporter", "fixture:gyp-pick-other", "wei", 8, ["fixture:gyp-quiet"], GeneralGender.Male));
            // Fire Slash permits both a real printed Slash and a distinct normal-Slash Wusheng conversion.
            var kind = equipment ? "classic:silver-lion" : weapon ? "classic:qinglong-crescent-blade" : "standard:fire_slash";
            b.AddDeck(new("fixture:gyp-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard(kind, Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "当前关银屏实际命令草稿", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:gyp-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:gyp-owner", "fixture:gyp-other-1", "fixture:gyp-other-2", "fixture:gyp-other-3"]));
        }
    }
}
