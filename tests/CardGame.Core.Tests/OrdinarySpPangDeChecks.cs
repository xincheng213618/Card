using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinarySpPangDeChecks
{
    private const string Skill = "ol:juesi", Binding = "discard-slash-and-challenge", Driver = "fixture:spd-driver";
    private const string Observer = "fixture:spd-observer", Entry = "fixture:spd-entry", Loss = "fixture:spd-loss";
    private const string OwnerReason = "skill-program.conditional-discard-duel.owner-cost", TargetReason = "skill-program.conditional-discard-duel.target-cost";
    private const string Mode = "identity:classic-sp-pang-de-fixture";

    public static void TwoRealCostsAndIssuedDuelTypedReturn()
    {
        var (g, r) = Create(); Prepare(g); Begin(g); Reach(g, p => Cost(p, "owner"));
        Private(g); Reject(g); g = Cold(g, r); var ownerCard = PaySlash(g);
        Reach(g, p => Cost(p, "target")); Require(P(g)!.PlayerSeat == 1, "The other character chooses its own real HE card.");
        Private(g); Reject(g); g = Cold(g, r); var targetCard = PayEquipment(g);
        Reach(g, p => p.Kind == DecisionKind.RespondSlash); g = Cold(g, r); Advance(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); g = Cold(g, r); Continue(g); Play(g);
        var issued = E<ConditionalDiscardDuelIssuedEvent>(g).Single().Origin;
        Require(issued.InitialActorSeat == 0 && issued.InitialTargetSeat == 1 && issued.TargetHpAtIssue >= issued.OwnerHpAtIssue &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 0 &&
                e.Action.ProviderSeat == 0 && e.Action.EffectiveKind == CardKind.Duel && e.Action.PhysicalCards.Count == 0) &&
            E<CardUsedEvent>(g).Any(e => e.CardId == 0 && e.CardKind == CardKind.Duel) && E<ConditionalDiscardDuelFinishedEvent>(g).Single().Issued,
            "Both real costs issue one real canonical zero-entity Duel and return once through Damage and Completed children.");
        PaidOnce(g, ownerCard, targetCard); Require(!g.CardMovements.Any(m => m.CardId == 0), "No fabricated Card0 payment or cleanup is emitted.");
        g = Cold(g, r);

        var (cancel, cr) = Create(); Prepare(cancel); Begin(cancel); Reach(cancel, p => Cost(p, "owner"));
        var before = cancel.CardMovements.Count; Answer(cancel, c => c.Parameters.GetValueOrDefault("cost-side") == "cancel"); Play(cancel);
        Require(cancel.CardMovements.Count == before && E<ConditionalDiscardDuelPaidEvent>(cancel).Length == 0 && E<ConditionalDiscardDuelIssuedEvent>(cancel).Length == 0,
            "Declining the unpaid own cost changes no real card and issues no Duel."); cancel = Cold(cancel, cr);
    }

    private static bool Slash(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Cost(PendingDecision p, string side) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("cost-side") == side);
    private static void Private(GameEngine g)
    { var p = P(g)!; Require(p.IsPrivate && p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 1), "Only the payer sees its own real hand-card choices.");
      foreach (var s in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(s).PendingDecision is null, "Unrelated viewers never receive the private payment choice or identity."); }
    private static int PaySlash(GameEngine g)
    { var p = P(g)!; var ids = g.CreateSnapshot(p.PlayerSeat).Players[p.PlayerSeat].Hand.Where(c => Slash(c.Kind)).Select(c => c.Id).ToHashSet();
      var c = p.Choices.First(c => c.Cards is [var id] && ids.Contains(id)); Answer(g, choice => choice.Id == c.Id); return c.Cards.Single(); }
    private static int PayEquipment(GameEngine g)
    { var c = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment)); Answer(g, x => x.Id == c.Id); return c.Cards.Single(); }
    private static void PaidOnce(GameEngine g, int own, int other)
    {
        foreach (var (cardId, reason) in new[] { (own, OwnerReason), (other, TargetReason) })
        {
            var payments = g.CardMovements.Where(m => m.CardId == cardId && m.Reason.Value == reason &&
                m.To == CardLocation.DiscardPile).ToArray();
            Require(payments.Length == 1, "Each original cost is really discarded once and is never reused as Duel material.");
            Require(!g.CardMovements.Any(m => m.Sequence > payments[0].Sequence && m.CardId == cardId &&
                m.To == CardLocation.Processing), "Each original cost is really discarded once and is never reused as Duel material.");
        }
    }
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, Binding, [], [1], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Prepare(GameEngine g) { Play(g); Use(g, "draw", []); Play(g); Use(g, "equip", [1]); Play(g); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> match)
    { for (var i = 0; i < 160; i++) { var p = P(g); if (p is not null && match(p)) return; Advance(g); }
      throw new InvalidOperationException("Fixed SP Pang De fixture did not reach its real boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    { var p = P(g); if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage")) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
      else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
      Require(State(g) == State(restored), "Four private player views, scalar receipt, ledger and exact child/typed return journal-restore identically."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g), "Invalid input does not change costs, history or any private view."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool targetLoss = false, bool ownerDying = false, bool sourceLoss = false, bool issuedSourceLoss = false, bool native = false, bool ownerDamageDying = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(targetLoss, ownerDying, sourceLoss, issuedSourceLoss, native, ownerDamageDying));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:spd-owner", g.Revision, P(g)!.PromptId)); } return (g, r);
    }
    private sealed class Fixture(bool targetLoss, bool ownerDying, bool sourceLoss, bool issuedSourceLoss, bool native, bool ownerDamageDying) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-sp-pang-de", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":20}]},{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
            {"id":"{{Observer}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{(ownerDying || ownerDamageDying ? OwnerReason : TargetReason)}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"{{(ownerDamageDying ? "damage" : "loseHp")}}","target":"owner","amount":{{(ownerDamageDying ? 1 : ownerDying ? 7 : 3)}}},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{{(ownerDamageDying ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : string.Empty)}}{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["duel","peach"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss",{{(issuedSourceLoss ? "\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"target\",\"cardKinds\":[\"duel\"]" : "\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\",\"equipment\"],\"movementOccurrence\":\"perOwnerBatch\",\"movementReasons\":[\"" + TargetReason + "\"]")}},"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]}]}
            """;
            var descriptions = new Dictionary<string, object> { [Driver] = new { name = "材料准备", description = "真实摸牌与装备" },
                [Observer] = new { name = "原弃牌孩子", description = "真实HP变化", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "实际伤害救援完成", description = "真实子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Loss] = new { name = "真实来源失效", description = "成熟抑制能力" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = descriptions }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "固定真实命令能力") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id],
                Tags = id == Entry ? SkillTag.Locked : SkillTag.None });
            foreach (var lord in new[] { true, false }) b.AddSkill(new(lord ? "fixture:spd-pick-owner" : "fixture:spd-pick-other", "正式选将", "角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == lord ? 10000d : -10000d) });
            var owner = new List<string> { "fixture:spd-pick-owner", "classic:mashu" }; if (!native) owner.AddRange([Driver, Entry]); if (ownerDying || ownerDamageDying) owner.Add(Observer);
            b.AddGeneral(new("fixture:spd-owner", "真实决死来源", "supporter", Skill, "wei", 6, owner.ToArray()) { InitialHp = ownerDamageDying ? 1 : null });
            for (var i = 1; i < 4; i++) { var other = new List<string>(); if (!native) other.Add(Entry); if (i == 1 && targetLoss) other.Add(Observer); if (i == 1 && (sourceLoss || issuedSourceLoss)) other.Add(Loss);
                b.AddGeneral(new($"fixture:spd-other-{i}", "其他角色", "supporter", "fixture:spd-pick-other", "qun", 8, other.ToArray())); }
            b.AddDeck(new("fixture:spd-deck", "固定混合真实材料", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i =>
                // Native scoring needs a real payable Slash after the ordinary once-per-turn Slash.
                new ContentDeckPhysicalCard(native ? "standard:slash" : i % 5 == 0 ? "standard:slash" : i % 5 == 1 ? "classic:silver-lion" : "standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "决死真实两费", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:spd-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:spd-owner", "fixture:spd-other-1", "fixture:spd-other-2", "fixture:spd-other-3"]));
        }
    }
}
