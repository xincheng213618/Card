using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FinalTargetSlashEscalationChecks
{
    private const string Skill = "fixture:final-target-escalation";
    private const string Driver = "fixture:final-target-escalation-driver";
    private const string JudgmentChild = "fixture:final-target-escalation-judgment";
    private const string TargetChild = "fixture:final-target-escalation-target";
    private const string Owner = "fixture:final-target-escalation-owner";
    private const string Mode = "identity:classic-final-target-escalation";
    private const string Reason = "skill.final-target-escalation";
    private static readonly Lazy<ContentRegistry> Standard = new(() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()));

    public static void ExactTargetJudgmentQuotaAndSilverLion()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash })
        foreach (var outsideFirst in kind == CardKind.Slash ? new[] { false, true } : new[] { false })
        {
            var (g, registry) = Start(kind, lowerHpAfterReceipt: false);
            Accept(g, new UseProgramSkillCommand(0, Driver, "equip", [], [2], g.Revision, P(g)!.PromptId));
            Reach(g, IsPlay);
            Require(g.CreateSnapshot(0).Players[2].Equipment.Any(c => c.Kind == CardKind.SilverLion),
                "The formal target really equips a native Silver Lion before the attack.");
            Require(registry.GetSkill(Skill).Program!.CardIdentities.Count == 0 && Slash(g, 2) is not null,
                "The unconditional static Slash-distance modifier publishes a real distance-two Use without a card-identity rule.");
            if (!outsideFirst)
            {
                Play(g, Slash(g, 1)!);
                Reach(g, IsPlay);
                Require(Facts<DamageAppliedEvent>(g).Single(e => e.SourceSeat == 0 && e.TargetSeat == 1).Amount == 1 &&
                        !Facts<JudgmentRequestedEvent>(g).Any(e => e.Reason == Reason) && Slash(g, 1) is null,
                    "One actual in-range family Slash consumes the normal allowance without issuing the outside-range judgment.");
            }
            var action = Slash(g, 2);
            Require(action is not null, "An outside-range Slash remains legal after normal allowance is exhausted.");
            Play(g, action!);
            Reach(g, p => IsContinue(p, JudgmentChild));
            var producer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
            var judgment = g.ResolutionStack.OfType<JudgmentFrame>().Single();
            Require(producer.InstructionIndex == 1 && judgment.ParentFrameId == producer.Id &&
                    Facts<JudgmentRequestedEvent>(g).Count(e => e.Reason == Reason && e.ParentResolutionId == producer.Id) == 1 &&
                    !Facts<ProgramTargetSlashReceiptIssuedEvent>(g).Any(),
                "The real final-target producer pauses at its own native judgment before issuing a damage receipt.");
            RejectWrongActor(g);
            g = Cold(g, registry);
            Continue(g);
            Reach(g, p => IsContinue(p, TargetChild));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.FinalTargetSlashReceipts is not null);
            var receipt = use.FinalTargetSlashReceipts!.Single();
            Require(use.Action is { Type: CardActionType.Use } accepted && accepted.ActorSeat == 0 && accepted.ProviderSeat == 0 &&
                    accepted.EffectiveKind == kind && accepted.TargetSeats.SequenceEqual([2]) &&
                    accepted.PhysicalCards is [var material] && material.CardId == action!.CardId && material.From == CardLocation.Hand(0) &&
                    receipt.ActionId == accepted.ActionId && receipt.CardUseFrameId == use.Id && receipt.ActorSeat == 0 && receipt.TargetSeat == 2 &&
                    receipt.ProducerFrameId == producer.Id && receipt.SkillId == Skill && receipt.GameplayHash == producer.GameplayHash &&
                    receipt.SkillInstanceId == producer.SkillInstanceId && receipt.EscalateToTargetHp && receipt.DamageBonus == 0 &&
                    use.FinalTargetSlashReceipts is IList<ProgramTargetSlashReceipt> { IsReadOnly: true },
                "The immutable issued receipt belongs to the exact accepted action, material, final target and original judgment producer.");
            g = Cold(g, registry);
            Continue(g);
            Reach(g, IsPlay);
            var applied = Facts<DamageAppliedEvent>(g).Single(e => e.SourceSeat == 0 && e.TargetSeat == 2);
            var requested = Facts<DamageRequestedEvent>(g).Single(e => e.SourceSeat == 0 && e.TargetSeat == 2);
            Require(applied.Amount == 1 && g.CreateSnapshot(0).Players[2].Hp == 2 &&
                    requested.Amount == applied.Amount &&
                    Facts<AfterDamageEvent>(g).Count(e => e.ResolutionId == requested.ResolutionId && e.SourceSeat == 0 && e.TargetSeat == 2 &&
                        e.Amount == applied.Amount && e.RemainingHp == applied.RemainingHp) == 1 &&
                    Facts<SilverLionDamageCappedEvent>(g).Count(e => e.ResolutionId == use.Id && e.TargetSeat == 2) == 1 &&
                    Facts<ProgramTargetSlashDamageEscalatedEvent>(g).Count(e => e.ResolutionId == use.Id && e.TargetSeat == 2) == 1 &&
                    Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                    Facts<ProgramTargetSlashReceiptIssuedEvent>(g).Count(e => e.CardUseFrameId == use.Id) == 1 &&
                    g.CardMovements.Count(m => m.CardId == action!.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                    !g.ResolutionStack.Any(f => f.Id == use.Id || f.Id == producer.Id || f.Id == judgment.Id),
                "A base-one attack raised to HP three is still capped to one by real Silver Lion, with one payment, receipt and completion. Actual: " +
                JsonSerializer.Serialize(new { kind, outsideFirst, UseId = use.Id, Applied = applied, TargetHp = g.CreateSnapshot(0).Players[2].Hp,
                    Requested = requested, After = Facts<AfterDamageEvent>(g).Where(e => e.ResolutionId == requested.ResolutionId).ToArray(),
                    LionCount = Facts<SilverLionDamageCappedEvent>(g).Count(e => e.ResolutionId == use.Id && e.TargetSeat == 2),
                    Adjustments = Facts<ProgramTargetSlashDamageEscalatedEvent>(g).Where(e => e.ResolutionId == use.Id).ToArray(),
                    Finished = Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id),
                    Receipts = Facts<ProgramTargetSlashReceiptIssuedEvent>(g).Count(e => e.CardUseFrameId == use.Id),
                    Payment = g.CardMovements.Count(m => m.CardId == action!.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing),
                    Remaining = g.ResolutionStack.Where(f => f.Id == use.Id || f.Id == producer.Id || f.Id == judgment.Id).Select(f => f.Id).ToArray() }));
            Require(Slash(g, 1) is null && Slash(g, 2) is not null && !Facts<CardUseDebitRefundedEvent>(g).Any(),
                "Outside-range uses are unlimited but genuinely consume quota; a later inside-range use stays unavailable and no refund is invented.");
            var before = State(g);
            var another = Slash(g, 2)!;
            var rejected = g.Submit(new PlayCardCommand(0, another.CardId!.Value, [1], g.Revision, P(g)!.PromptId, another.PlayedCardKind)
            { ConversionSource = another.ConversionSource, AdditionalConversionSources = another.AdditionalConversionSources });
            Require(!rejected.Accepted && State(g) == before,
                "An exhausted in-range use is atomically rejected even when the same real material has a published outside-range use.");
            _ = Cold(g, registry);
        }

        var (dynamic, dynamicRegistry) = Start(CardKind.FireSlash, lowerHpAfterReceipt: true);
        var boosted = Slash(dynamic, 2)!;
        Play(dynamic, boosted);
        Reach(dynamic, p => IsContinue(p, JudgmentChild));
        dynamic = Cold(dynamic, dynamicRegistry);
        Continue(dynamic);
        Reach(dynamic, p => IsContinue(p, TargetChild));
        var owning = dynamic.ResolutionStack.OfType<CardUseFrame>().Single(f => f.FinalTargetSlashReceipts is not null);
        Require(dynamic.CreateSnapshot(0).Players[2].Hp == 3 && owning.FinalTargetSlashReceipts!.Single().TargetSeat == 2,
            "The receipt is already issued while the native final target still has three HP.");
        dynamic = Cold(dynamic, dynamicRegistry);
        Continue(dynamic);
        Reach(dynamic, IsPlay);
        Require(Facts<ProgramSkillHpLostEvent>(dynamic).Count(e => e.SkillId == TargetChild && e.TargetSeat == 2 && e.Amount == 1 && e.RemainingHp == 2) == 1 &&
                Facts<DamageAppliedEvent>(dynamic).Single(e => e.SourceSeat == 0 && e.TargetSeat == 2).Amount == 2 &&
                Facts<DamageRequestedEvent>(dynamic).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) == 1 &&
                Facts<ProgramTargetSlashDamageEscalatedEvent>(dynamic).Single(e => e.ResolutionId == owning.Id).EscalatedAmount == 2 &&
                Facts<CardUseFinishedEvent>(dynamic).Count(e => e.ResolutionId == owning.Id) == 1,
            "The damage equals current target HP at application: a real child lowers HP after issuance and the rule lowers a real bonus-three Fire Slash to two.");
        _ = Cold(dynamic, dynamicRegistry);

        var (higher, higherRegistry) = Start(CardKind.FireSlash, lowerHpAfterReceipt: false, targetHp: 4, damageBonus: 2);
        Play(higher, Slash(higher, 2)!);
        Reach(higher, IsPlay);
        Require(Facts<DamageAppliedEvent>(higher).Single(e => e.SourceSeat == 0 && e.TargetSeat == 2).Amount == 4 &&
                Facts<ProgramTargetSlashDamageEscalatedEvent>(higher).Single().EscalatedAmount == 4,
            "A real conversion total of three becomes current target HP four; the conversion bonus participates once in the replacement delta.");
        _ = Cold(higher, higherRegistry);
    }

    private static (GameEngine, ContentRegistry) Start(CardKind kind, bool lowerHpAfterReceipt, int targetHp = 3, int damageBonus = 0)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, lowerHpAfterReceipt, targetHp,
            lowerHpAfterReceipt ? 2 : damageBonus));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Reach(g, IsPlay);
        return (g, registry);
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsContinue(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static LegalAction? Slash(GameEngine g, int target) => g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash &&
        a.ConversionSource?.BindingId == "material-slash" && a.TargetSeats.SequenceEqual([target]));
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Play(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
        g.Revision, P(g)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!;
        Require(p.PlayerSeat == 0, "Only the native human chooser submits an answer.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> predicate)
    {
        for (var n = 0; n < 128; n++)
        {
            var p = P(g);
            if (predicate(p)) return;
            if (p is { PlayerSeat: 0 })
            {
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
                    Answer(g, c => c.Targets.SequenceEqual([2]));
                else throw new InvalidOperationException("Unexpected actual human boundary: " + SnapshotJson.Serialize(g.CreateSnapshot(0)));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The small real Slash/judgment fixture did not reach its expected owning boundary.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The native serialized command was rejected.");
    }
    private static void RejectWrongActor(GameEngine g)
    {
        var p = P(g)!; var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "Wrong actor rejection preserves the original judgment, target, payment, private views and accepted prefix atomically.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Cold accepted-prefix replay preserves exact final-target receipts, native judgment and all player-private projections.");
        foreach (var viewer in Enumerable.Range(0, 4))
            Require(restored.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0),
                "No foreign physical hand is exposed by the shared judgment or target receipt.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(CardKind kind, bool lowerHpAfterReceipt, int targetHp, int damageBonus) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-final-target-escalation", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddCard(Standard.Value.GetCard("classic:silver-lion"));
            var output = kind switch { CardKind.FireSlash => "fireSlash", CardKind.ThunderSlash => "thunderSlash", _ => "slash" };
            var after = lowerHpAfterReceipt ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventTarget\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}" : "";
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                 {"id":"{{{Skill}}}","revision":1,"modifiers":[{"id":"distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],
                  "cardPolicies":[{"id":"outside","kind":"bypassSlashLimitAgainstOutsideAttackRange","cardKinds":["slash","fireSlash","thunderSlash"]}],
                  "viewAs":[{"id":"material-slash","inputKinds":["silverLion"],"inputSuits":[],"outputKind":"{{{output}}}","forPlay":true,"forResponse":false,"useOnly":true,"damageBonus":{{{damageBonus}}}}],
                  "triggers":[{"id":"final-target","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,
                   "condition":{"kind":"compare","left":{"kind":"ownerEventTargetDistance"},"operator":"greaterThan","right":{"kind":"currentAttackRange"}},
                   "effects":[{"op":"startJudgment","target":"owner","judgmentReason":"{{{Reason}}}","resultBind":"judgment","visibility":"public"},
                    {"op":"zhuiLieEscalateTargetDamage","target":"owner"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment","destination":"discardPile"}]}]},
                 {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
                  "effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"armor"}]}]},
                 {"id":"{{{JudgmentChild}}}","revision":1,"triggers":[{"id":"judgment","window":"judgmentFinalized","subject":"owner","judgmentReasons":["{{{Reason}}}"],"suits":["spade"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"judgment-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{TargetChild}}}","revision":1,"triggers":[{"id":"target","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"target-seen","options":[{"id":"continue"}]}{{{after}}}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Skill, Driver, JudgmentChild, TargetChild }.ToDictionary(id => id, id =>
                    {
                        var fields = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实最终目标和原生判定回执" };
                        if (id is JudgmentChild or TargetChild) fields["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                        return fields;
                    })
                }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "共享实际杀和原生子窗")
            { Program = pair.Value, ProgramPresentation = catalog.Presentations[pair.Key] });
            b.AddGeneral(new(Owner, "固定真实使用者", "supporter", Skill, "wei", 4, [Driver, JudgmentChild, TargetChild]));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:final-target-escalation-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定真实目标", "supporter", "standard:none", "shu", targetHp));
            const string deck = "fixture:final-target-escalation-deck";
            b.AddDeck(new(deck, "固定真实装备实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "正式装备规则小夹具", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
