using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ForeignSelectedCardSlashChecks
{
    private const string Actual = "ol:xuanbei-current";
    private const string Owner = "ol:yang-yan";
    private const string Mode = "identity:classic-selected-foreign-slash";
    private const string Driver = "fixture:selected-foreign-slash-driver";
    private const string Before = "fixture:selected-foreign-slash-before";
    private const string Gain = "fixture:selected-foreign-slash-gain";
    private const string Hp = "fixture:selected-foreign-slash-hp";
    private const string Redirect = "fixture:selected-foreign-slash-redirect";
    private const string Suppress = "fixture:selected-foreign-slash-suppress";
    private const string Grain = "fixture:selected-foreign-slash-grain";
    private const string Weapon = "fixture:selected-foreign-slash-weapon";
    private const string QualificationProbe = "qualification-probe";
    private const string DrawReason = "skill-program.foreign-selected-card-slash.outcome-draw";
    private static readonly Lazy<ContentRegistry> Standard = new(() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()));

    public static void HandSlotsFreezeNativeUseAndDamageOutcome()
    {
        RejectMalformedContracts();
        foreach (var damage in new[] { false, true })
        {
            var (g, registry) = Start();
            var hp = V(g, 0).Hp;
            var initial = V(g, 0).HandCount;
            Begin(g, 1);
            var choice = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Require(P(g) is { PlayerSeat: 0, IsPrivate: true, TargetSeat: 1 } && choice.Cards.Count == 0 &&
                P(g)!.Choices.All(c => c.Cards.Count == 0), "The registered current skill publishes only opaque foreign Hand slots to its actual issuer.");
            Frozen(P(g)!.Choices); RejectWrongActor(g); RejectUnpublished(g); Private(g);
            g = Cold(g, registry); Answer(g, c => c.Id == choice.Id);
            Reach(g, p => IsContinue(p, Before));
            var use = NativeUse(g); var returned = use.ForeignSelectedCardSlashReturn!;
            AssertNative(g, use, CardLocation.Hand(1));
            Require(returned.Source.SkillId == Actual && returned.Source.BindingId == "xuanbei" && returned.Source.OwnerSeat == 0 &&
                returned.OriginalTargetSeat == 0 && returned.ActorSeat == 1 && use.Action!.PhysicalCards.Single().CardKind == CardKind.Dodge,
                "A genuinely printed Dodge becomes one real Slash: the foreign actor is also provider, while the original Yang Yan remains issuer and target.");
            g = Cold(g, registry); Continue(g);
            Reach(g, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
            g = Cold(g, registry);
            Answer(g, c => damage ? c.Parameters.GetValueOrDefault("response") == "take-damage" :
                c.Cards.Count == 1 && V(g, 0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Dodge));
            Reach(g, p => IsContinue(p, Gain));
            AssertOutcome(g, returned, damage, damage ? 2 : 1);
            Require(V(g, 0).Hp == hp - (damage ? 1 : 0) && V(g, 0).HandCount == initial - (damage ? 0 : 1) + (damage ? 2 : 1),
                "The exact native Dodge/payment and actual same-use damage produce the requested one/two-card issuer reward.");
            g = Cold(g, registry); Reach(g, IsPlay);
            AssertFinished(g, returned, damage ? 2 : 1);
            Require(g.GetHumanLegalActions().All(a => a.ProgramSkillId != Actual), "The original actual Play quota is paid once and blocks a second selected foreign-card Slash.");
            _ = Cold(g, registry);
        }
    }

    public static void EquipmentAndJudgmentMaterialsKeepNativeChildren()
    {
        foreach (var zone in new[] { CardZoneKind.Equipment, CardZoneKind.Judgment })
        {
            var (g, registry) = Start(zone == CardZoneKind.Equipment ? "classic:silver-lion" : "standard:indulgence");
            if (zone == CardZoneKind.Equipment)
            {
                UseDriver(g, "equip", 2); Reach(g, IsPlay);
                UseDriver(g, "damage", 2); Reach(g, IsPlay);
                Require(V(g, 2).Equipment is [var armor] && armor.Kind == CardKind.SilverLion && V(g, 2).Hp == 3,
                    "The foreign source really equips Silver Lion and then suffers actual damage before paying it as Slash.");
            }
            else
            {
                var delayed = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([2]));
                Play(g, delayed); Reach(g, IsPlay);
                Require(g.CardMovements.Any(m => m.CardId == delayed.CardId && m.To == CardLocation.Judgment(2)),
                    "A real native delayed-trick use places the exact later Slash material in the foreign Judgment region.");
            }
            var recoveryBefore = E<RecoveryAppliedEvent>(g).Length;
            Begin(g, 2);
            Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString() && c.Cards.Count == 1),
                "Public foreign Equipment/Judgment entities are actually selectable alongside opaque Hand slots.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString());
            Reach(g, p => IsContinue(p, Before));
            var use = NativeUse(g); var returned = use.ForeignSelectedCardSlashReturn!;
            AssertNative(g, use, new(zone, 2));
            Require(use.Action!.ActorSeat == 2 && use.Action.ProviderSeat == 2 && use.Action.PhysicalCards.Single().From == new CardLocation(zone, 2),
                "The distance-two foreign actor uses its real selected region entity, rather than transferring it to the issuer or substituting another Hand cost.");
            g = Cold(g, registry); Continue(g);
            if (zone == CardZoneKind.Equipment)
            {
                Reach(g, p => IsContinue(p, Hp));
                var root = Parent(g); var hpWindow = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
                Require(root.ForeignSelectedCardSlash is { Stage: ForeignSelectedCardSlashStage.SettlementChildren, RequestedDraw: 0 } &&
                    hpWindow.ResumeFrameId == root.Id && hpWindow.Change.ParentFrameId == use.Id && hpWindow.Change.TargetSeat == 2 &&
                    E<RecoveryAppliedEvent>(g).Skip(recoveryBefore).Count(e => e.SourceSeat == 2 && e.TargetSeat == 2 && e.Amount == 1) == 1 && V(g, 2).Hp == 4,
                    "Native Silver Lion removal heals its actual foreign owner once; the original use-parent HP child returns to the paid issuer before reward drawing.");
                g = Cold(g, registry);
            }
            Reach(g, p => IsContinue(p, Gain));
            AssertOutcome(g, returned, true, 2); g = Cold(g, registry); Reach(g, IsPlay);
            AssertFinished(g, returned, 2);
            Require(g.CardMovements.Count(m => m.CardId == returned.CardId && m.From == new CardLocation(zone, 2) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                g.CardMovements.Count(m => m.CardId == returned.CardId && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                "Each genuine HEJ material leaves its original region and finishes the same native use once across cold restoration.");
            _ = Cold(g, registry);
        }

        var (weapon, weaponRegistry) = Start(generalWeapons: true);
        var generated = V(weapon, 2).Equipment.Single(c => c.Kind == CardKind.GeneralWeapon).Id;
        Require(weapon.CardMovements.Any(m => m.CardId == generated && m.From == CardLocation.OutsideGame && m.To == CardLocation.Equipment(2)),
            "The selected weapon is genuinely produced and equipped by the native sampled-general command path, without host state injection.");
        Begin(weapon, 2); weapon = Cold(weapon, weaponRegistry); Answer(weapon, c => c.Cards.SequenceEqual([generated]));
        Reach(weapon, p => IsContinue(p, Before)); var generatedUse = NativeUse(weapon); var generatedReturn = generatedUse.ForeignSelectedCardSlashReturn!;
        AssertNative(weapon, generatedUse, CardLocation.Equipment(2));
        Require(generatedReturn.GeneralWeapon && generatedReturn.PrintedKind == CardKind.GeneralWeapon &&
            weapon.CreateCardZoneDiagnostics().Single(c => c.CardId == generated).Location == CardLocation.OutsideGame &&
            E<ForeignSelectedCardSlashPaidEvent>(weapon).Single().To == CardLocation.OutsideGame,
            "The genuine general-weapon material pays one native Equipment-to-OutsideGame cost while its exact Slash Use stays alive.");
        weapon = Cold(weapon, weaponRegistry); Continue(weapon); Reach(weapon, p => IsContinue(p, Gain));
        AssertOutcome(weapon, generatedReturn, true, 2); weapon = Cold(weapon, weaponRegistry); Reach(weapon, IsPlay);
        AssertFinished(weapon, generatedReturn, 2);
        Require(weapon.CreateCardZoneDiagnostics().Single(c => c.CardId == generated).Location == CardLocation.OutsideGame &&
            weapon.CardMovements.Count(m => m.CardId == generated && m.From == CardLocation.Equipment(2) && m.To == CardLocation.OutsideGame && m.Reason == CardMoveReasons.Use) == 1 &&
            !weapon.CardMovements.Any(m => m.CardId == generated && m.Reason == CardMoveReasons.UseFinished),
            "Native finalization preserves the removed general weapon instead of resurrecting or discarding it; its issuer reward and owning return still settle once.");
        _ = Cold(weapon, weaponRegistry);

        var (ox, oxRegistry) = Start("classic:wooden-ox");
        UseDriver(ox, "equip", 0); Reach(ox, IsPlay);
        var oxId = V(ox, 0).Equipment.Single(c => c.Kind == CardKind.WoodenOx).Id;
        var grainId = V(ox, 0).Hand[0].Id;
        Accept(ox, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grainId], [2], ox.Revision, P(ox)!.PromptId)); Reach(ox, IsPlay);
        Require(V(ox, 2).Equipment.Single(c => c.Kind == CardKind.WoodenOx).Id == oxId && V(ox, 2).WoodenOxGrain!.Single().Id == grainId &&
            ox.CreateSnapshot(0).Players[2].WoodenOxGrain!.Count == 0,
            "A true equipment command stores one actual Hand card and transfers both native Ox and its private grain to the foreign actor.");
        Begin(ox, 2); Answer(ox, c => c.Cards.SequenceEqual([oxId])); Reach(ox, p => IsContinue(p, Before));
        var oxUse = NativeUse(ox); var oxReturn = oxUse.ForeignSelectedCardSlashReturn!;
        AssertNative(ox, oxUse, CardLocation.Equipment(2)); ox = Cold(ox, oxRegistry); Continue(ox);
        Reach(ox, p => IsContinue(p, Grain));
        var oxRoot = Parent(ox); var grainBatch = ox.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.Movements.Any(m => m.CardId == grainId));
        Require(oxRoot.ForeignSelectedCardSlash is { Stage: ForeignSelectedCardSlashStage.SettlementChildren, RequestedDraw: 0 } paid &&
            grainBatch.ResumeProgramFrameId == oxRoot.Id && grainBatch.Batch.ParentFrameId == oxUse.Id &&
            grainBatch.Batch.ParentBatchId == paid.PaymentBatchId && grainBatch.Batch.Movements is [var grainMove] &&
            grainMove.CardId == grainId && grainMove.From == CardLocation.WoodenOxGrain(2) && grainMove.To == CardLocation.DiscardPile && grainMove.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
            V(ox, 2).WoodenOxGrainCount == 0 && !E<ForeignSelectedCardSlashDrawIssuedEvent>(ox).Any(),
            "The exact paid Ox's genuine grain-discard child retains its native use and parent batch, and drains before the once-issued outcome draw.");
        Frozen(grainBatch.Batch.Movements); ox = Cold(ox, oxRegistry); Continue(ox); Reach(ox, p => IsContinue(p, Gain));
        AssertOutcome(ox, oxReturn, true, 2); ox = Cold(ox, oxRegistry); Reach(ox, IsPlay); AssertFinished(ox, oxReturn, 2);
        Require(ox.CardMovements.Count(m => m.CardId == grainId && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1,
            "Cold return does not discard the same native stored grain or pay the foreign Ox material twice.");
        _ = Cold(ox, oxRegistry);
    }

    public static void RedirectAndLostSourceRetainOnlyOwnUseReward()
    {
        foreach (var sourceLoss in new[] { false, true })
        {
            var (g, registry) = Start(redirect: !sourceLoss, sourceLoss: sourceLoss);
            var sourceState = sourceLoss ? V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Actual).BooleanStates!
                .Single(s => s.StateId == QualificationProbe) : null;
            Require(!sourceLoss || sourceState is { Value: true }, "The actual current Xuanbei instance is qualified before the foreign card is paid.");
            Begin(g, 1); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            if (!sourceLoss)
            {
                Reach(g, p => p?.SkillPrompt?.SkillId == Redirect && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
                g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3]));
                Reach(g, p => p?.SkillPrompt?.SkillId == Redirect && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
                g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1);
            }
            else
            {
                Reach(g, p => IsContinue(p, Before)); g = Cold(g, registry); Continue(g);
                Reach(g, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            }
            Reach(g, p => IsContinue(p, sourceLoss ? Suppress : Gain));
            var root = Parent(g); var returned = root.ForeignSelectedCardSlash!.SlashReturn!;
            AssertOutcome(g, returned, sourceLoss, sourceLoss ? 2 : 1);
            Require(sourceLoss ? sourceState is { Value: true } original && returned.Source.SkillInstanceId == original.SkillInstanceId &&
                    V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Actual).BooleanStates!.All(s => s.SkillInstanceId != original.SkillInstanceId) :
                !E<ForeignSelectedCardSlashDamageRecordedEvent>(g).Any(e => e.ProgramFrameId == root.Id) &&
                    E<DamageAppliedEvent>(g).All(e => e.TargetSeat != 0),
                "Paid completion survives genuine source suppression; a native redirected Slash never borrows damage to another target for the issuer reward.");
            g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, returned, sourceLoss ? 2 : 1); _ = Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string card = "standard:dodge", bool redirect = false, bool sourceLoss = false, bool generalWeapons = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(card, redirect, sourceLoss, generalWeapons));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, IsPlay); return (g, registry);
    }
    private static void Begin(GameEngine g, int actor)
    {
        Require(g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Actual && a.SelectableTargetSeats.Contains(actor)), "The actual registered current activation publishes this legal foreign actor before spending its Play quota.");
        Accept(g, new UseProgramSkillCommand(0, Actual, "xuanbei", [], [actor], g.Revision, P(g)!.PromptId));
        Reach(g, p => p?.SkillPrompt?.SkillId == Actual && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "foreign-selected-slash"));
    }
    private static CardUseFrame NativeUse(GameEngine g) => g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ForeignSelectedCardSlashReturn is not null);
    private static ProgramSkillFrame Parent(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ForeignSelectedCardSlash is not null);
    private static void AssertNative(GameEngine g, CardUseFrame use, CardLocation from)
    {
        var root = Parent(g); var r = root.ForeignSelectedCardSlash!; var returned = use.ForeignSelectedCardSlashReturn!;
        Require(root.OwnerSeat == 0 && root.InstructionIndex == 1 && r.Stage == ForeignSelectedCardSlashStage.UseIssued && r.SlashReturn == returned &&
            returned.ProgramFrameId == root.Id && returned.CardUseFrameId == use.Id && returned.ActionId == use.Action!.ActionId && returned.From == from &&
            use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } action &&
            action.ActorSeat == r.ActorSeat && action.ProviderSeat == r.ActorSeat && action.TargetSeats.SequenceEqual([0]) &&
            action.PhysicalCards is [var cost] && cost.CardId == returned.CardId && cost.From == from && action.ConversionChain.SequenceEqual([returned.Source]) &&
            E<ForeignSelectedCardSlashIssuedEvent>(g).Count(e => e.Return == returned) == 1 &&
            E<ForeignSelectedCardSlashPaidEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.CardId == returned.CardId && e.From == from && e.Before == r.PaymentBefore && e.After == r.PaymentAfter) == 1 &&
            !E<ForeignSelectedCardSlashDrawIssuedEvent>(g).Any(), "The exact parent frame owns one frozen actual foreign-use receipt, original HEJ payment and no premature draw.");
        Frozen(use.Action!.PhysicalCards); Frozen(use.Action.ConversionChain); Private(g);
    }
    private static void AssertOutcome(GameEngine g, ForeignSelectedCardSlashReturn returned, bool damage, int draw)
    {
        var root = Parent(g); var r = root.ForeignSelectedCardSlash!;
        Require(r.Stage == ForeignSelectedCardSlashStage.DrawChildren && r.SlashReturn == returned && r.DamagedIssuer == damage &&
            r.RequestedDraw == draw && r.ActualDraw == draw && E<ForeignSelectedCardSlashResolvedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.DamagedIssuer == damage) == 1 &&
            E<ForeignSelectedCardSlashDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.CardUseFrameId == returned.CardUseFrameId && e.Requested == draw && e.Actual == draw) == 1 &&
            E<ForeignSelectedCardSlashDamageRecordedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.TargetSeat == 0 && e.Amount > 0) == (damage ? 1 : 0) &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == returned.CardUseFrameId) == 1 && !g.ResolutionStack.Any(f => f.Id == returned.CardUseFrameId) &&
            g.CardMovements.Count(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter && m.To == CardLocation.Hand(0) && m.From == CardLocation.DrawPile && m.Reason.Value == DrawReason) == draw,
            "Whole native use settlement precedes one frozen issuer reward; its exact real gain children retain the paid owning frame and cannot reissue the Slash or draw.");
        Private(g); RejectWrongActor(g);
    }
    private static void AssertFinished(GameEngine g, ForeignSelectedCardSlashReturn r, int draw)
    {
        Require(E<ForeignSelectedCardSlashFinishedEvent>(g).Count(e => e.ProgramFrameId == r.ProgramFrameId && e.UseIssued) == 1 &&
            E<ForeignSelectedCardSlashDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == r.ProgramFrameId && e.Requested == draw) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == r.CardUseFrameId) == 1 &&
            g.CardMovements.Count(m => m.CardId == r.CardId && m.From == r.From && m.Reason == CardMoveReasons.Use) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == r.ProgramFrameId || f.Id == r.CardUseFrameId), "The original material, full use, draw and typed parent return all finish exactly once.");
    }
    private static void UseDriver(GameEngine g, string id, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [target], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)
    { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsContinue(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Require(p.PlayerSeat == 0, "Only the actual human issuer submits an answer."); Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> predicate)
    {
        for (var i = 0; i < 128; i++)
        {
            var p = P(g); if (predicate(p)) return;
            if (p is { PlayerSeat: 0 })
            {
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
                else if (p.Kind == DecisionKind.RespondDodge) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
                else throw new InvalidOperationException("Unexpected selected foreign Slash boundary: " + SnapshotJson.Serialize(g.CreateSnapshot(0)));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed small HEJ fixture did not reach its native selected-Slash boundary.");
    }
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Accept(GameEngine g, GameCommand c)
    { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(r.Accepted, r.Error?.Message ?? "A native selected-Slash command was rejected."); }
    private static void RejectWrongActor(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && before == State(g), "A wrong actor cannot mutate the paid native use, private choice, gain invoice or command prefix."); }
    private static void RejectUnpublished(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("foreign-unpublished"), g.Revision)).Accepted && before == State(g), "An unpublished HEJ entity choice is rejected atomically."); }
    private static void Private(GameEngine g)
    { foreach (var viewer in Enumerable.Range(0, 4)) Require(g.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0), "Player-private snapshots never reveal a foreign physical Hand face."); }
    private static void Frozen<T>(IReadOnlyList<T> list)
    { Require(list is IList<T> { IsReadOnly: true }, "Every exposed native collection is read-only."); if (list.Count > 0) { try { ((IList<T>)list)[0] = list[0]; throw new InvalidOperationException("A frozen collection allowed mutation."); } catch (NotSupportedException) { } } }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Require(State(g) == State(restored), "Cold accepted-prefix replay preserves exact HEJ use/payment/return, native children, private views and all persistent facts."); Private(restored); return restored; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void RejectMalformedContracts()
    {
        var original = JsonNode.Parse("""{"schemaVersion":0,"skills":[{"id":"fixture:foreign-slash-contract","revision":1,"activations":[{"id":"use","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"useSelectedForeignCardAsSlash","target":"selectedTarget","condition":{"kind":"always"}}]}]}]}""")!.AsObject();
        original["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { ["fixture:foreign-slash-contract"] = new { name = "Foreign Slash", description = "strict actual HEJ use" } } });
        _ = SkillProgramCatalog.Load(original.ToJsonString(), presentation);
        foreach (var change in new Action<JsonObject>[] { a => a["targetKind"] = "anyLiving", a => a["maxCards"] = 1,
            a => a["usesPerPhase"] = 2, a => a["usesPerTurn"] = 1, a => a["effects"]![0]!["target"] = "owner",
            a => a["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}""")) })
        {
            var copy = original.DeepClone(); change(copy["skills"]![0]!["activations"]![0]!.AsObject());
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("Malformed selected foreign-card Slash activation was accepted.");
        }
    }

    private sealed class Fixture(string cardId, bool redirect, bool sourceLoss, bool generalWeapons) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-selected-foreign-slash", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (cardId is "classic:silver-lion" or "classic:wooden-ox") b.AddCard(Standard.Value.GetCard(cardId));
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                 {"id":"{{{Driver}}}","revision":1,"activations":[
                   {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                   {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
                 {"id":"{{{Before}}}","revision":1,"triggers":[{"id":"before","window":"cardUseBeforeTargetEffects","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{Suppress}}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{Grain}}}","revision":1,"triggers":[{"id":"grain","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perBatch","movementReasons":["equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grain","options":[{"id":"continue"}]}]}]},
                 {"id":"{{{Weapon}}}","revision":1,"triggers":[{"id":"real-weapon","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]}]},
                 {"id":"{{{Redirect}}}","revision":1,"triggers":[{"id":"redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[
                   {"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},
                   {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},
                   {"op":"redirectCurrentAttack","target":"selectedTarget"}]}]}]}
                """;
            var ids = new[] { Driver, Before, Gain, Hp, Redirect, Suppress, Grain, Weapon };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => { var fields = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实选备原生子窗" };
                    if (id is Before or Gain or Hp or Suppress or Grain) fields["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" }; return fields; }) }));
            foreach (var id in ids) b.AddSkill(new(id, id, "真实选牌、原生子结算与一次返回") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id], Tags = SkillTag.Locked,
                SuppressionRule = id == Suppress && sourceLoss ? new(3) : null });
            var formal = Standard.Value.Generals[Owner];
            foreach (var id in formal.SkillIds)
            {
                var definition = Standard.Value.GetSkill(id);
                b.AddSkill(sourceLoss && id == Actual ? WithQualificationProbe(definition) : definition);
            }
            b.AddGeneral(formal with { AdditionalSkillIds = formal.SkillIds.Where(id => id != formal.SkillId).Concat(new[] { Driver, Before, Gain, Grain })
                .Concat(redirect ? new[] { Redirect } : []).Concat(sourceLoss ? new[] { Suppress } : []).ToArray() });
            var peers = Enumerable.Range(1, generalWeapons ? 7 : 3).Select(i => "fixture:selected-foreign-slash-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定真实区域持有者", "supporter", Hp, "wei", 4, generalWeapons ? [Weapon] : []));
            const string deck = "fixture:selected-foreign-slash-deck";
            b.AddDeck(new(deck, "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard(cardId, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "正式原生选备小夹具", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, deck,
                GeneralCandidateCount: peers.Length + 1, GeneralPoolIds: [Owner, .. peers]));
        }

        private static ContentSkillDefinition WithQualificationProbe(ContentSkillDefinition definition)
        {
            using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-yang-yan.rules.json")!;
            using var reader = new StreamReader(stream);
            var actual = JsonNode.Parse(reader.ReadToEnd())!["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Actual)!.DeepClone();
            actual["states"] = JsonNode.Parse("""[{"id":"qualification-probe","initialValue":true,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}]""");
            var rules = new JsonObject { ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion, ["skills"] = new JsonArray(actual) };
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Actual] = new { name = "选备", description = "实际程序的公开资格探针" } } });
            return definition with { Program = SkillProgramCatalog.Load(rules.ToJsonString(), presentation).Programs[Actual] };
        }
    }
}
