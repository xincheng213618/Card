using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramEffect SuitBenefitEffect(ProgramSkillFrame f) =>
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private IReadOnlyList<Card> SuitBenefitCosts(int ownerSeat, string skillId, string instance, IReadOnlyList<Suit>? suits = null) =>
        GetHand(_players[ownerSeat]).Concat(GetEquipment(_players[ownerSeat]))
            .Where(c => (suits is null || suits.Contains(EffectiveSuit(_players[ownerSeat], c))) &&
                !c.IsGeneralWeapon && !IsActiveProgramSourceEquipmentCard(ownerSeat, skillId, instance, c) &&
                !IsForeignEquipmentDiscardPrevented(ownerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();

    private bool CanOfferSuitPreventionBenefit(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit)) return true;
        return context is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target, Amount: > 0 } &&
            target == candidate.OwnerSeat && _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(w =>
                w.Id == context.ParentFrameId && !w.Prevented && w.TargetSeat == target) &&
            _players.Any(p => p.IsAlive && p.Seat != target) &&
            SuitBenefitCosts(target, candidate.SkillId, candidate.SkillInstanceId, trigger.Effects[0].Suits).Count > 0;
    }

    private SkillProgramStepOutcome BeginSuitPreventionBenefit(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SuitPreventionBenefit is not null || f.WindowContext is not
            { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target } context || target != f.OwnerSeat ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Prevented || parent.Amount <= 0 ||
            CurrentDamageAttempt is not { } attack || attack.ResolutionId != (parent.ContinuationAttackResolutionId ?? parent.ParentFrameId))
            throw new InvalidOperationException("Suit prevention lost its exact pending damage owner.");
        ReplaceRuntimeTop(f with { SuitPreventionBenefit = new(f.InstructionIndex, parent.Id, attack.ResolutionId,
            parent.SourceSeat, context.SourceSeat is null, target, parent.Amount, SuitPreventionBenefitStage.ChoosingCost) });
        PublishSuitPreventionBenefit(GetActiveProgramFrame(f.Id)); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> SuitPreventionBenefitChoices(ProgramSkillFrame f)
    {
        var r = f.SuitPreventionBenefit!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Params(string branch) => new()
        { ["program-action"] = "suit-prevention-benefit", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == SuitPreventionBenefitStage.ChoosingCost)
            foreach (var card in SuitBenefitCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId, SuitBenefitEffect(f).Suits))
                choices.Add(new(new($"suit-benefit.{f.Id}.cost.{card.Id}"), $"弃置【{card.DisplayName}】", Array.AsReadOnly(new[] { card.Id }), [], Params("cost")));
        else if (r.Stage == SuitPreventionBenefitStage.ChoosingRecipient)
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).OrderBy(p => p.Seat))
            {
                var targets = Array.AsReadOnly(new[] { target.Seat });
                choices.Add(new(new($"suit-benefit.{f.Id}.damage.{target.Seat}"), $"令 {target.Name} 受到来源的{SuitBenefitEffect(f).Amount}点伤害后按已损失体力摸牌", [], targets, Params("damage-draw")));
                choices.Add(new(new($"suit-benefit.{f.Id}.loss.{target.Seat}"), $"令 {target.Name} 失去{SuitBenefitEffect(f).Amount}点体力后获得原弃牌", [], targets, Params("lose-hp-gift")));
            }
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishSuitPreventionBenefit(ProgramSkillFrame f)
    {
        var choices = SuitPreventionBenefitChoices(f);
        if (choices.Count == 0) { CancelProgramBindingAndCleanup(f, "没有合法未付款选择，原伤害继续。"); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.SuitPreventionBenefit!.Stage == SuitPreventionBenefitStage.ChoosingCost ? "选择一张符合花色的手牌或装备牌。" : "选择受益角色与防伤后的分支。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()),
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
          SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveSuitPreventionBenefit(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.SuitPreventionBenefit is not { } r ||
            r.Stage is not (SuitPreventionBenefitStage.ChoosingCost or SuitPreventionBenefitStage.ChoosingRecipient) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], [SuitPreventionBenefitChoices(f).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("Suit prevention answer lost its private owning choice.");
        AssertSuitPreventionBenefit(f);
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || _winner != Winner.None)
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "防伤付款前来源失效，原伤害继续。"); return; }
        if (r.Stage == SuitPreventionBenefitStage.ChoosingCost)
        {
            var card = SuitBenefitCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId, SuitBenefitEffect(f).Suits).Single(c => c.Id == choice.Cards.Single());
            ClearPendingDecision(); ReplaceRuntimeTop(f with { SuitPreventionBenefit = r with
            { CostCardId = card.Id, CostFrom = _cardZones.GetLocation(card.Id), CostEffectiveSuit = EffectiveSuit(_players[f.OwnerSeat], card), Stage = SuitPreventionBenefitStage.ChoosingRecipient } });
            PublishSuitPreventionBenefit(GetActiveProgramFrame(f.Id)); return;
        }
        var recipient = choice.Targets.Single();
        if (!_players[recipient].IsAlive || recipient == f.OwnerSeat || r.CostCardId is not { } id || r.CostFrom is not { } from ||
            _cardZones.GetLocation(id) != from || !SuitBenefitCosts(f.OwnerSeat, f.SkillId, f.SkillInstanceId, SuitBenefitEffect(f).Suits).Any(c => c.Id == id))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "原费用或受益角色在付款前失效。"); return; }
        var branch = choice.Parameters["branch"] switch
        { "damage-draw" => SuitPreventionBenefitBranch.DamageAndDraw, "lose-hp-gift" => SuitPreventionBenefitBranch.LoseHpAndGift,
          _ => throw new InvalidOperationException("Unknown suit prevention branch.") };
        var cost = _cardZones.CardsAt(from).Single(c => c.Id == id);
        ClearPendingDecision(); ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null),
            SuitPreventionBenefit = r with { RecipientSeat = recipient, Branch = branch, Stage = SuitPreventionBenefitStage.CostPaid } });
        MoveCard(cost, from, CardLocation.DiscardPile, new($"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit}"), moved =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { SuitPreventionBenefit = active.SuitPreventionBenefit! with { CostMovementSequence = moved.Sequence } });
            PreventProgramCurrentDamage(GetActiveProgramFrame(f.Id));
            AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionPaymentEvent(f.Id, r.BeforeDamageFrameId, r.OriginalAttackFrameId,
                f.OwnerSeat, id, from, r.CostEffectiveSuit!.Value, recipient, branch, moved.Sequence));
        });
        AdvanceRuntimeProgram(f.Id);
    }

    private bool DrainSuitPlacementChildren(ProgramSkillFrame f)
    {
        var continuation = f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement;
        return TryBeginQueuedRecoveryReplacement(f.Id, continuation) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, continuation) || TryBeginCardsMovedProgramWindow(f.Id);
    }

    private bool ResumeSuitPreventionBenefit(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.SuitPreventionBenefit is not { } r) return false;
        AssertSuitPreventionBenefit(f);
        if (r.Stage is SuitPreventionBenefitStage.ChoosingCost or SuitPreventionBenefitStage.ChoosingRecipient) return true;
        if (f.AttackAttempt is not null) return true; // Only its actual damage pipeline may complete this paid instruction.
        if (DrainSuitPlacementChildren(f)) return true;
        f = GetActiveProgramFrame(id); r = f.SuitPreventionBenefit!;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        var recipient = _players[r.RecipientSeat!.Value]; var effect = SuitBenefitEffect(f);
        if (r.Stage == SuitPreventionBenefitStage.CostPaid)
        {
            if (_winner != Winner.None || !recipient.IsAlive)
            { ReplaceRuntimeTop(f with { SuitPreventionBenefit = r with { Stage = SuitPreventionBenefitStage.Finished } }); AdvanceRuntimeProgram(id); return true; }
            if (r.Branch == SuitPreventionBenefitBranch.DamageAndDraw)
            {
                ReplaceRuntimeTop(f with { SuitPreventionBenefit = r with { Stage = SuitPreventionBenefitStage.Damaging } });
                AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionBenefitIssuedEvent(f.Id, recipient.Seat, r.Branch.Value,
                    r.SourceLess ? null : r.SourceSeat, effect.Amount));
                BeginProgramSkillDamage(GetActiveProgramFrame(id), recipient.Seat, effect.Amount,
                    r.SourceLess ? null : new ProgramParticipantReference(ProgramParticipantRef.EventSource), DamageNature.Normal, r.SourceLess);
                return true;
            }
            ReplaceRuntimeTop(f with { SuitPreventionBenefit = r with { Stage = SuitPreventionBenefitStage.LosingHp, HpBefore = recipient.Hp } });
            AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionBenefitIssuedEvent(f.Id, recipient.Seat, r.Branch!.Value, null, effect.Amount));
            if (new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, recipient.Seat, effect.Amount) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(id);
            return true;
        }
        if (r.Stage == SuitPreventionBenefitStage.Damaging)
        {
            var count = _winner == Winner.None && recipient.IsAlive ? Math.Min(effect.MaximumCards, Math.Max(0, recipient.MaxHp - recipient.Hp)) : 0;
            var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), SuitPreventionBenefit = r with
                { Stage = SuitPreventionBenefitStage.Drawing, RequestedDrawCount = count, MovementSequenceBefore = before } });
            if (count > 0) DrawCards(recipient, count, true, new($"skill-program.{f.SkillId}.suit-prevention-draw"));
            var after = _cardMovements.LastOrDefault()?.Sequence ?? before;
            var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(recipient.Seat) && m.Reason.Value == $"skill-program.{f.SkillId}.suit-prevention-draw");
            var active = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(active with { SuitPreventionBenefit = active.SuitPreventionBenefit! with { MovementSequenceAfter = after, ActualDrawCount = actual } });
            AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionDrawEvent(id, recipient.Seat, count, actual, before, after));
            AdvanceRuntimeProgram(id); return true;
        }
        if (r.Stage == SuitPreventionBenefitStage.LosingHp)
        {
            ReplaceRuntimeTop(f with { SuitPreventionBenefit = r with { Stage = SuitPreventionBenefitStage.Gifting }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (_winner == Winner.None && recipient.IsAlive && _cardZones.GetLocation(r.CostCardId!.Value) == CardLocation.DiscardPile)
            {
                var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(c => c.Id == r.CostCardId);
                MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(recipient.Seat), new($"skill-program.{f.SkillId}.suit-prevention-gift"), moved =>
                {
                    var active = GetActiveProgramFrame(id);
                    ReplaceRuntimeTop(active with { SuitPreventionBenefit = active.SuitPreventionBenefit! with { GiftMovementSequence = moved.Sequence } });
                    AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionGiftEvent(id, recipient.Seat, card.Id, moved.Sequence, false));
                });
            }
            else
            {
                var active = GetActiveProgramFrame(id);
                ReplaceRuntimeTop(active with { SuitPreventionBenefit = active.SuitPreventionBenefit! with { GiftUnavailable = true } });
                AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionGiftEvent(id, recipient.Seat, r.CostCardId!.Value, 0, true));
            }
            AdvanceRuntimeProgram(id); return true;
        }
        ReplaceRuntimeTop(f with { SuitPreventionBenefit = null }); return false;
    }

    private void CaptureSuitPreventionDamageCompleted(ProgramSkillFrame f, bool applied)
    {
        if (f.SuitPreventionBenefit is not { Stage: SuitPreventionBenefitStage.Damaging, Branch: SuitPreventionBenefitBranch.DamageAndDraw } r) return;
        if (!IsValidSuitPreventionBenefit(f, true) || f.AttackReturn?.ParentAttackOwnerFrameId != r.OriginalAttackFrameId)
            throw new InvalidOperationException("Suit benefit damage returned to a different original owner.");
        AdvanceEventRulesAndQueueFact(new ProgramSuitPreventionDamageCompletedEvent(f.Id, r.OriginalAttackFrameId, r.RecipientSeat!.Value, applied));
    }
    private bool ReturnSuitPlacementMovement(ProgramSkillFrame f)
    {
        if (f.SuitPreventionBenefit is null && f.MatchedJudgmentPlacement is null) return false;
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("Suit placement lost its typed movement return.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); return true;
    }
    private PromptChoice SelectAiSuitPreventionBenefit(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.SuitPreventionBenefit!.Stage == SuitPreventionBenefitStage.ChoosingCost)
            return decision.Choices.OrderBy(c => CardCatalog.Get(_cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(x => x.Id == c.Cards[0]).Kind).HandKeepValue).ThenBy(c => c.Cards[0]).First();
        var view = CreateSnapshot(decision.PlayerSeat);
        return decision.Choices.OrderByDescending(c => _aiBrains[decision.PlayerSeat].ScoreProgramTarget(view, c.Targets.Single(),
            new SkillProgramAiHint(0, 0, 0, c.Parameters["branch"] == "damage-draw" ? Math.Min(5, _players[c.Targets[0]].MaxHp - _players[c.Targets[0]].Hp + SuitBenefitEffect(f).Amount) : 1,
                0, SuitBenefitEffect(f).Amount, false, false))).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    private bool AllowsSuitPreventionNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) =>
        f.SuitPreventionBenefit is { Stage: SuitPreventionBenefitStage.Damaging, Branch: SuitPreventionBenefitBranch.DamageAndDraw } r &&
        target == r.RecipientSeat && amount == SuitBenefitEffect(f).Amount && nature == DamageNature.Normal && sourceLess == r.SourceLess &&
        (r.SourceLess ? source is null : source?.Kind == ProgramParticipantRef.EventSource) &&
        CurrentDamageAttempt?.ResolutionId == r.OriginalAttackFrameId && IsValidSuitPreventionBenefit(f, requirePayment: true);

    private bool IsValidSuitPreventionBenefit(ProgramSkillFrame f, bool requirePayment)
    {
        if (f.SuitPreventionBenefit is not { } r || f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            r.InstructionIndex != f.InstructionIndex || SuitBenefitEffect(f).Op != SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit ||
            context.ParentFrameId != r.BeforeDamageFrameId || context.TargetSeat != f.OwnerSeat || context.Amount != r.OriginalAmount ||
            context.SourceSeat != (r.SourceLess ? null : r.SourceSeat) || r.OriginalTargetSeat != f.OwnerSeat || r.OriginalAmount <= 0 ||
            !IsValidPlayerSeat(r.SourceSeat) || !_resolutionStack.Any(p => p.Id == r.OriginalAttackFrameId) ||
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w => w.Id == r.BeforeDamageFrameId) is not { } parent ||
            (parent.ContinuationAttackResolutionId ?? parent.ParentFrameId) != r.OriginalAttackFrameId || parent.SourceSeat != r.SourceSeat || parent.TargetSeat != f.OwnerSeat ||
            parent.Amount != r.OriginalAmount || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex].Candidate)) return false;
        var paid = r.Stage is not (SuitPreventionBenefitStage.ChoosingCost or SuitPreventionBenefitStage.ChoosingRecipient);
        if (parent.Prevented != paid || requirePayment && !paid) return false;
        if (!paid) return r.CostMovementSequence == 0 && r.RecipientSeat is null && r.Branch is null &&
            (r.Stage == SuitPreventionBenefitStage.ChoosingCost ? r.CostCardId is null && r.CostFrom is null && r.CostEffectiveSuit is null :
             r.CostCardId is { } chosen && r.CostFrom is { } held && held.OwnerSeat == f.OwnerSeat && held.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
             _cardZones.GetLocation(chosen) == held && SuitBenefitEffect(f).Suits.Contains(r.CostEffectiveSuit!.Value));
        if (r.CostCardId is not { } cardId || r.CostFrom is not { } from || from.OwnerSeat != f.OwnerSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !SuitBenefitEffect(f).Suits.Contains(r.CostEffectiveSuit!.Value) ||
            r.RecipientSeat is not { } seat || !IsValidPlayerSeat(seat) || seat == f.OwnerSeat || r.Branch is null ||
            !_cardMovements.Any(m => m.Sequence == r.CostMovementSequence && m.CardId == cardId && m.From == from && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit}")) return false;
        return CompleteProgramEventHistory().OfType<ProgramSuitPreventionPaymentEvent>().Count(e => e.ProgramFrameId == f.Id &&
            e.BeforeDamageFrameId == parent.Id && e.OriginalAttackFrameId == r.OriginalAttackFrameId && e.OwnerSeat == f.OwnerSeat && e.CostCardId == cardId &&
            e.CostFrom == from && e.EffectiveSuit == r.CostEffectiveSuit && e.RecipientSeat == seat && e.Branch == r.Branch && e.MovementSequence == r.CostMovementSequence) == 1 &&
            CompleteProgramEventHistory().OfType<ProgramDamagePreventedEvent>().Count(e => e.FrameId == parent.Id && e.SkillId == f.SkillId &&
                e.BindingId == f.TriggerId && e.OwnerSeat == f.OwnerSeat && e.Amount == parent.Amount) == 1;
    }
    private bool HasExactSuitBenefitRedirect(ProgramSkillFrame f, ProgramSuitPreventionBenefitReceipt r, AttackAttemptState attack) =>
        attack.DamageRedirected && attack.TransferOwnerSeat == r.RecipientSeat && attack.TransferTargetSeat == attack.TargetSeat &&
        CompleteProgramEventHistory().OfType<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == f.Id && e.OwnerSeat == r.RecipientSeat &&
            e.SourceSeat == attack.SourceSeat && e.TargetSeat == attack.TargetSeat);
    private void AssertSuitPreventionBenefit(ProgramSkillFrame f)
    {
        if (f.SuitPreventionBenefit is not { } r) return;
        if (!Enum.IsDefined(r.Stage) || r.Branch is { } chosenBranch && !Enum.IsDefined(chosenBranch) || !IsValidSuitPreventionBenefit(f, false) ||
            r.Stage == SuitPreventionBenefitStage.Finished && _winner == Winner.None && _players[r.RecipientSeat!.Value].IsAlive ||
            r.Stage == SuitPreventionBenefitStage.Damaging && f.AttackAttempt is { } attack &&
                (f.AttackReturn?.ParentAttackOwnerFrameId != r.OriginalAttackFrameId || attack.SourceSeat != (r.SourceLess ? f.OwnerSeat : r.SourceSeat) ||
                 attack.TargetSeat != r.RecipientSeat && !HasExactSuitBenefitRedirect(f, r, attack) || attack.Nature != DamageNature.Normal || attack.SourceLess != r.SourceLess) ||
            r.Stage is SuitPreventionBenefitStage.Damaging or SuitPreventionBenefitStage.Drawing &&
                (r.Branch != SuitPreventionBenefitBranch.DamageAndDraw || f.AttackAttempt is null &&
                 CompleteProgramEventHistory().OfType<ProgramSuitPreventionDamageCompletedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                    e.OriginalAttackFrameId == r.OriginalAttackFrameId && e.RecipientSeat == r.RecipientSeat) != 1) ||
            r.Stage is SuitPreventionBenefitStage.LosingHp or SuitPreventionBenefitStage.Gifting && r.Branch != SuitPreventionBenefitBranch.LoseHpAndGift ||
            r.Stage is SuitPreventionBenefitStage.Damaging or SuitPreventionBenefitStage.Drawing or SuitPreventionBenefitStage.LosingHp or SuitPreventionBenefitStage.Gifting &&
                CompleteProgramEventHistory().OfType<ProgramSuitPreventionBenefitIssuedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                    e.RecipientSeat == r.RecipientSeat && e.Branch == r.Branch && e.Amount == SuitBenefitEffect(f).Amount &&
                    e.SourceSeat == (r.Branch == SuitPreventionBenefitBranch.DamageAndDraw && !r.SourceLess ? r.SourceSeat : null)) != 1 ||
            r.Stage == SuitPreventionBenefitStage.Gifting && (r.GiftUnavailable ? r.GiftMovementSequence != 0 :
                !_cardMovements.Any(m => m.Sequence == r.GiftMovementSequence && m.CardId == r.CostCardId && m.From == CardLocation.DiscardPile &&
                    m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == $"skill-program.{f.SkillId}.suit-prevention-gift")) ||
            r.Stage == SuitPreventionBenefitStage.Gifting && CompleteProgramEventHistory().OfType<ProgramSuitPreventionGiftEvent>().Count(e =>
                e.ProgramFrameId == f.Id && e.RecipientSeat == r.RecipientSeat && e.CostCardId == r.CostCardId &&
                e.MovementSequence == r.GiftMovementSequence && e.Unavailable == r.GiftUnavailable) != 1 ||
            r.Stage is SuitPreventionBenefitStage.LosingHp or SuitPreventionBenefitStage.Gifting &&
                (r.HpBefore <= 0 || CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(e =>
                    e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == r.RecipientSeat &&
                    e.Amount == Math.Min(r.HpBefore, SuitBenefitEffect(f).Amount) && e.RemainingHp == Math.Max(0, r.HpBefore - SuitBenefitEffect(f).Amount)) != 1) ||
            r.Stage == SuitPreventionBenefitStage.Drawing && (CompleteProgramEventHistory().OfType<ProgramSuitPreventionDrawEvent>().Count(e =>
                    e.ProgramFrameId == f.Id && e.RecipientSeat == r.RecipientSeat && e.Requested == r.RequestedDrawCount && e.Actual == r.ActualDrawCount &&
                    e.FirstMovementSequence == r.MovementSequenceBefore && e.LastMovementSequence == r.MovementSequenceAfter) != 1 || r.RequestedDrawCount < 0 || r.RequestedDrawCount > SuitBenefitEffect(f).MaximumCards ||
                r.ActualDrawCount < 0 || r.ActualDrawCount > r.RequestedDrawCount || r.MovementSequenceAfter < r.MovementSequenceBefore ||
                _cardMovements.Count(m => m.Sequence > r.MovementSequenceBefore && m.Sequence <= r.MovementSequenceAfter && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == $"skill-program.{f.SkillId}.suit-prevention-draw") != r.ActualDrawCount))
            throw new InvalidOperationException("Suit prevention lost its exact cost, source, branch or benefit receipt.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) && r.Stage is SuitPreventionBenefitStage.ChoosingCost or SuitPreventionBenefitStage.ChoosingRecipient &&
            (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, SuitPreventionBenefitChoices(f))))
            throw new InvalidOperationException("Suit prevention lost its private frozen prompt.");
    }
    private sealed partial class ProgramSkillHost : ISuitPreventionAndJudgmentPlacementHost
    {
        public SkillProgramStepOutcome DiscardSuitPreventDamageAndBenefit(ProgramSkillFrame f) => engine.BeginSuitPreventionBenefit(f);
        public SkillProgramStepOutcome PlaceMatchedJudgmentCard(ProgramSkillFrame f, string bind, IReadOnlyList<Suit> suits) => engine.BeginMatchedJudgmentPlacement(f, bind, suits);
    }
}
