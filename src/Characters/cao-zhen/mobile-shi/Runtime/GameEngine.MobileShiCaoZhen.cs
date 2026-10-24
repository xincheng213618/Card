using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class ShiCaoZHost(GameEngine engine) : IShiCaoZHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp operation) => engine.BeginShiCaoZ(f, operation); }
    private SkillProgramStepOutcome BeginShiCaoZ(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != ShiCaoZSifeng || f.ShiCaoZhen is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) throw new InvalidOperationException("Sifeng requires its original issued native trigger.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace && (f.TriggerId != "place-forecast" || context.Window != SkillProgramTriggerWindow.TurnEnding || f.OwnerSeat != _currentSeat || !_players[f.OwnerSeat].IsAlive) ||
            operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse && !IsIssuedShiCaoZUseCandidate(candidate, context) ||
            operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengEnd && !IsIssuedShiCaoZEndCandidate(candidate, context)) throw new InvalidOperationException("Sifeng lost its own ending, completed native Use or source-independent issued end.");
        var stage = operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace ? ShiCaoZStage.TopChildren : operation == SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse ? ShiCaoZStage.UseRemovalChildren : ShiCaoZStage.EndChoice;
        var r = new ShiCaoZReceipt(ShiCaoZSource(f), f.GameplayHash, _turnNumber, _currentSeat, context.ParentFrameId, operation, stage)
        { Before = _movementSequence, After = _movementSequence, Paid = operation != SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace };
        ReplaceRuntimeTop(f = f with { ShiCaoZhen = r });
        AdvanceEventRulesAndQueueFact(new ShiCaoZStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId, operation));
        if (stage == ShiCaoZStage.TopChildren)
        {
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Paid = true }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            for (var i = 0; i < 3 && EnsureDrawPile(); i++)
            {
                var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
                MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, new(ShiCaoZTopReason), m => AdvanceEventRulesAndQueueFact(new ShiCaoZTopTakenEvent(f.Id, i, card.Id, m.Sequence)), disclosure: CardMovementDisclosure.FaceDown);
                f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { ShiCaoZhen = f.ShiCaoZhen! with { Pool = f.ShiCaoZhen.Pool.Append(card.Id).ToArray(), After = _movementSequence } });
            }
        }
        else if (stage == ShiCaoZStage.UseRemovalChildren)
        {
            if (!TryShiCaoZCompletedUse(context, out var action, out var origin)) throw new InvalidOperationException("Sifeng cannot replace its completed native use.");
            var first = LiveShiCaoZMaterials().First(m => m.Holder == action.ActorSeat && ShiCaoZSameSource(m, f.OwnerSeat, f.SkillInstanceId, f.GameplayHash));
            var different = ShiCaoZColor(first.Suit) != ShiCaoZColor(action.EffectiveSuit ?? Suit.None);
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Paid = true, Holder = action.ActorSeat, UseId = origin, ActionId = action.ActionId, UsedSuit = action.EffectiveSuit ?? Suit.None, Materials = [first] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new ShiCaoZUseIssuedEvent(f.Id, r.Source, origin, action.ActionId, action.ActorSeat, first.PlacementId, action.EffectiveSuit ?? Suit.None, different));
            MoveShiCaoZMaterial(f, first, CardLocation.DiscardPile, ShiCaoZRemoveReason);
        }
        else
        {
            var holder = context.SourceSeat!.Value;
            var materials = LiveShiCaoZMaterials().Where(m => m.Holder == holder && m.Source.OwnerSeat == f.OwnerSeat).ToArray();
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Holder = holder, Materials = materials } });
            AdvanceEventRulesAndQueueFact(new ShiCaoZEndIssuedEvent(f.Id, r.Source, r.Turn, holder, materials.Length));
        }
        ContinueShiCaoZ(f.Id); return _pendingDecision is null ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.AwaitChoice;
    }
    private static int ShiCaoZColor(Suit suit) => suit is Suit.Heart or Suit.Diamond ? 1 : suit is Suit.Spade or Suit.Club ? 2 : 0;
    private int ShiCaoZChooser(ProgramSkillFrame f) => f.ShiCaoZhen!.Stage == ShiCaoZStage.Discard ? f.ShiCaoZhen.Holder : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> ShiCaoZChoices(ProgramSkillFrame f)
    {
        var r = f.ShiCaoZhen!; var choices = new List<PromptChoice>();
        void Add(string branch, string token, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => choices.Add(new(new($"shi-cao-zhen.{f.Id}.{branch}.{token}"), label, cards ?? [], targets ?? [],
            new Dictionary<string, string> { ["program-action"] = "mobile-shi-cao-zhen", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) }));
        if (r.Stage == ShiCaoZStage.Assign)
        {
            foreach (var id in r.Pool.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing))
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
                Add("assign", $"{id}.{target.Seat}", $"将【{PublicPileCardLabel(GetAdvancedCard(id))}】放在{target.Name}的伺锋队尾", [id], [target.Seat]);
            return PresentCardDistribution(f, choices, "shi-cao-zhen-forecast", r.Pool.Count, label: c => "扣置于 " + _players[c.Targets.Single()].Name);
        }
        if (r.Stage == ShiCaoZStage.Discard)
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, r.Holder, r.Holder, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard, destination: SkillProgramCardDestination.DiscardPile)
                .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mobile-shi-cao-zhen", ["branch"] = "discard", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) } }).ToArray());
        if (r.Stage == ShiCaoZStage.EndChoice && _players[f.OwnerSeat].IsAlive && _players[r.Holder].IsAlive)
        {
            Add("obtain", "all", $"获得{_players[r.Holder].Name}剩余的{r.Materials.Count}张伺锋");
            Add("damage", "all", $"移去{_players[r.Holder].Name}剩余的伺锋并对其造成1点伤害");
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishShiCaoZ(ProgramSkillFrame f)
    {
        var choices = ShiCaoZChoices(f); if (choices.Count == 0) { ContinueShiCaoZ(f.Id); return; }
        var chooser = ShiCaoZChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, f.ShiCaoZhen!.Stage switch
        {
            ShiCaoZStage.Assign => "伺锋：依序选择真实顶牌和其他角色；同一角色按放置顺序移去首张。",
            ShiCaoZStage.Discard => "伺锋颜色不同：弃置一张手牌。",
            _ => "伺锋：选择获得剩余牌，或移去后造成1点伤害。"
        }, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = chooser, IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mobile-shi-cao-zhen")]
    private void AnswerShiCaoZ(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoZhen: { } r } f || _pendingDecision?.PlayerSeat != ShiCaoZChooser(f) ||
            !ShiCaoZChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Sifeng lost its exact private order, recipient, hand-only payment or end choice.");
        RequireShiCaoZ(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = selected.Parameters["branch"];
        if (r.Stage == ShiCaoZStage.Assign)
        {
            var id = selected.Cards.Single(); var holder = selected.Targets.Single(); var card = GetAdvancedCard(id); var placement = ++_resolutionSequence;
            var location = new CardLocation(CardZoneKind.PrivatePersistentPile, holder, $"shi-cao-zhen:{placement}"); _cardZones.EnsurePrivatePersistentPile(location);
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.AssignmentChildren, Pool = r.Pool.Where(c => c != id).ToArray(), Assigned = r.Assigned + 1, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(card, CardLocation.Processing, location, new(ShiCaoZPlaceReason), m => AdvanceEventRulesAndQueueFact(new ShiCaoZPlacedEvent(new(placement, f.Id, r.Assigned, r.Source, r.Hash, holder, id, card.Kind, card.Suit, location, m.Sequence))), disclosure: CardMovementDisclosure.FaceDown);
            SetShiCaoZAfter(f.Id); ContinueShiCaoZ(f.Id); AdvanceRulesAndPublishState(); return;
        }
        if (r.Stage == ShiCaoZStage.Discard)
        {
            var id = selected.Cards.Single(); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.DiscardChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, r.Holder, [id], new(ShiCaoZDiscardReason)); SetShiCaoZAfter(f.Id);
            AdvanceEventRulesAndQueueFact(new ShiCaoZDiscardPaidEvent(f.Id, r.Holder, id, before, _movementSequence));
            ContinueShiCaoZ(f.Id); AdvanceRulesAndPublishState(); return;
        }
        BeginShiCaoZEndMove(f, branch == "obtain"); ContinueShiCaoZ(f.Id); AdvanceRulesAndPublishState();
    }
    private void SetShiCaoZAfter(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { ShiCaoZhen = f.ShiCaoZhen! with { After = _movementSequence } }); }
    private void MoveShiCaoZMaterial(ProgramSkillFrame f, ShiCaoZMaterial material, CardLocation destination, string reason)
    {
        if (_cardZones.GetLocation(material.CardId) != material.Location) return;
        MoveCard(GetAdvancedCard(material.CardId), material.Location, destination, new(reason), m => AdvanceEventRulesAndQueueFact(new ShiCaoZRemovedEvent(material.PlacementId, f.Id, material.CardId, m.Sequence, reason)),
            disclosure: destination.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        SetShiCaoZAfter(f.Id);
    }
    private void BeginShiCaoZEndMove(ProgramSkillFrame supplied, bool obtain)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShiCaoZhen!; var alive = _players[f.OwnerSeat].IsAlive;
        obtain &= alive;
        ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.EndChildren, Paid = true, Damage = !obtain && alive && _players[r.Holder].IsAlive, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiCaoZEndChoiceEvent(f.Id, obtain, alive, r.Materials.Count));
        foreach (var material in r.Materials) MoveShiCaoZMaterial(f, material, obtain ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile, obtain ? ShiCaoZObtainReason : ShiCaoZRemoveReason);
    }
    private void ContinueShiCaoZ(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoZhen: { } r } f || f.Id != id || _pendingDecision is not null) return;
        RequireShiCaoZ(f);
        if (f.PendingMovementContinuation is not null)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShiCaoZhen!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        }
        if (r.Stage is ShiCaoZStage.TopChildren or ShiCaoZStage.AssignmentChildren)
        { ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.Assign } }); r = f.ShiCaoZhen!; }
        if (r.Stage == ShiCaoZStage.Assign)
        {
            if (r.Pool.Count == 0) { FinishShiCaoZ(f); return; }
            if (!_players[f.OwnerSeat].IsAlive || !_players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat))
            {
                ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.CleanupChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                foreach (var card in r.Pool.Where(card => _cardZones.GetLocation(card) == CardLocation.Processing)) MoveCard(GetAdvancedCard(card), CardLocation.Processing, CardLocation.DiscardPile, new(ShiCaoZRemoveReason), disclosure: CardMovementDisclosure.FaceUp);
                SetShiCaoZAfter(id); ContinueShiCaoZ(id); return;
            }
            PublishShiCaoZ(f); return;
        }
        if (r.Stage == ShiCaoZStage.UseRemovalChildren)
        {
            var issued = ProgramEventHistory<ShiCaoZUseIssuedEvent>().Single(e => e.FrameId == id);
            if (issued.DifferentColor && _players[r.Holder].IsAlive)
            { ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.Discard } }); r = f.ShiCaoZhen!; }
            else { FinishShiCaoZ(f); return; }
        }
        if (r.Stage == ShiCaoZStage.Discard)
        { if (ShiCaoZChoices(f).Count == 0) FinishShiCaoZ(f); else PublishShiCaoZ(f); return; }
        if (r.Stage == ShiCaoZStage.EndChoice)
        {
            if (!_players[f.OwnerSeat].IsAlive || !_players[r.Holder].IsAlive) { BeginShiCaoZEndMove(f, false); ContinueShiCaoZ(id); return; }
            PublishShiCaoZ(f); return;
        }
        if (r.Stage == ShiCaoZStage.EndChildren && r.Damage && _players[f.OwnerSeat].IsAlive && _players[r.Holder].IsAlive)
        {
            ReplaceRuntimeTop(f = f with { ShiCaoZhen = r with { Stage = ShiCaoZStage.DamageChildren } });
            AdvanceEventRulesAndQueueFact(new ShiCaoZDamageIssuedEvent(f.Id, f.OwnerSeat, r.Holder));
            new ProgramSkillHost(this).Damage(f, r.Holder, 1); return;
        }
        FinishShiCaoZ(f);
    }
    private void FinishShiCaoZ(ProgramSkillFrame f)
    {
        if (f.ShiCaoZhen!.Stage == ShiCaoZStage.Complete) return;
        ReplaceRuntimeTop(f = f with { ShiCaoZhen = f.ShiCaoZhen with { Stage = ShiCaoZStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new ShiCaoZCompletedEvent(f.Id, f.ShiCaoZhen.Operation)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 49900)] private bool ResumeShiCaoZ(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoZhen: not null } f || f.Id != id) return false; ContinueShiCaoZ(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49900)] private bool ReturnShiCaoZMovement(ProgramSkillFrame f)
    { if (f.ShiCaoZhen is null) return false; ContinueShiCaoZ(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace, SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse, SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengEnd)]
    private PromptChoice ShiCaoZAi(PendingDecision decision, ProgramSkillFrame f) => decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "damage" && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.ShiCaoZhen!.Holder])) ?? decision.Choices[0];
}
