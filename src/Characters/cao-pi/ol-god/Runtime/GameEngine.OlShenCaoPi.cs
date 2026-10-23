namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource OlCaoPiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private PublicPersistentPileSource? OlChuPile(int owner, string instance) => ReferencedPublicPileSources(owner, "ol:chuyuan", instance)
        .OrderByDescending(p => PublicPileCards(p).Count).ThenBy(p => p.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40550, SkillProgramEffectOp.StoreOlChuyuanAfterDamage)]
    private bool CanOfferOlChuyuan(ProgramTriggerLegalityContext g) => g.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } target, Amount: > 0 } &&
        _players[target].IsAlive && (OlChuPile(g.OwnerSeat, g.Candidate.SkillInstanceId) is not { } pile || PublicPileCards(pile).Count < _players[g.OwnerSeat].MaxHp);
    [ProgramCapability] private sealed class OlShenCaoPiCapability(GameEngine e) : IOlShenCaoPiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffect effect) => e.BeginOlShenCaoPi(f, effect); }
    private SkillProgramStepOutcome BeginOlShenCaoPi(ProgramSkillFrame supplied, SkillProgramEffect e)
    {
        var f = GetActiveProgramFrame(supplied.Id); var storage = e.Op == SkillProgramEffectOp.StoreOlChuyuanAfterDamage;
        if (f.OlShenCaoPi is not null || f.InstructionIndex != (storage ? 1 : 2) || !OlCaoPiParentMatches(f, storage))
            throw new InvalidOperationException("OL Shen Cao Pi lost his original unpaid damage or preparation binding.");
        var target = storage ? f.WindowContext!.TargetSeat!.Value : f.OwnerSeat;
        var pile = storage ? null : OlChuPile(f.OwnerSeat, f.SkillInstanceId);
        var cards = pile is null ? [] : PublicPileCards(pile).Select(c => c.Id).ToArray();
        var r = new OlShenCaoPiReceipt(OlCaoPiSource(f), f.GameplayHash, e.Op, f.WindowContext!.ParentFrameId, target,
            storage ? OlShenCaoPiStage.Drawing : OlShenCaoPiStage.AwakeningChildren, pile, _movementSequence, _movementSequence) { Cards = cards };
        ReplaceRuntimeTop(f = f with { OlShenCaoPi = r });
        if (storage) { BeginOperationProgramDraw(f, target, 1, new("skill-program.ol:chuyuan.draw")); return SkillProgramStepOutcome.AwaitChild; }
        if (pile is not null && cards.Length > 0) MoveCards(PublicPileCards(pile).ToArray(), pile.Location, CardLocation.Hand(f.OwnerSeat), new("skill-program." + f.SkillId + ".obtain-chu"));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { OlShenCaoPi = r with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new OlCaoPiPileObtainedEvent(f.Id, r.Source, cards.Length, r.Before, _movementSequence));
        ResumeOlShenCaoPi(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool OlCaoPiParentMatches(ProgramSkillFrame f, bool storage)
    {
        if (f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat || f.TriggerId is null) return false;
        if (!storage) return ExactGameActivationAwakeningParent(f);
        return c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } target, Amount: > 0 } &&
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } parent && parent.TargetSeat == target &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex].ToProgramCandidate()) &&
            parent.TriggerWindow == c.Window && c.DamageFrameId == parent.ParentFrameId;
    }
    private bool DrainOlShenCaoPi(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.StoreOlChuyuanAfterDamage)] private void ReturnOlChuyuanDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        var r = f.OlShenCaoPi ?? throw new InvalidOperationException("Chuyuan's native draw lost its original frame receipt.");
        if (r.Stage != OlShenCaoPiStage.Drawing || o.RecipientSeat != r.TargetSeat || o.RequestedCount != 1 || o.SequenceAfter != _movementSequence ||
            ProgramEventHistory<OlChuyuanDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Chuyuan cannot repeat or redirect its real draw.");
        ReplaceRuntimeTop(f with { OlShenCaoPi = r with { Stage = OlShenCaoPiStage.DrawChildren, DrawOutcome = o } });
        AdvanceEventRulesAndQueueFact(new OlChuyuanDrawReturnedEvent(f.Id, r.TargetSeat, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter)); ResumeOlShenCaoPi(f.Id);
    }
    private IReadOnlyList<PromptChoice> OlCaoPiChoices(ProgramSkillFrame f)
    {
        var r = f.OlShenCaoPi!;
        if (r.Stage == OlShenCaoPiStage.DepositChoice)
            return Array.AsReadOnly(GetHand(_players[r.TargetSeat]).Where(c => PublicPileStorageCard(r.TargetSeat, f.SkillId, f.SkillInstanceId, c, CardLocation.Hand(r.TargetSeat)))
                .Select(c => new PromptChoice(new($"ol-chuyuan:{f.Id}:{c.Id}"), "将【" + c.DisplayName + "】置为神曹丕的“储”。", [c.Id], [],
                    new Dictionary<string, string> { ["program-action"] = "ol-shen-cao-pi", ["value"] = c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })).ToArray());
        if (r.Stage == OlShenCaoPiStage.SkillChoice)
            return Array.AsReadOnly(OlShenCaoPiComposition.TianxingReferences.Skip(1).Select(id => new PromptChoice(new($"ol-tianxing:{f.Id}:{id}"), "永久获得【" + _contentRegistry.GetSkill(id).Name + "】。", [], [],
                new Dictionary<string, string> { ["program-action"] = "ol-shen-cao-pi", ["value"] = id })).ToArray());
        return [];
    }
    private void PublishOlCaoPiChoice(ProgramSkillFrame f)
    {
        var r = f.OlShenCaoPi!; var choices = OlCaoPiChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || !_players[r.TargetSeat].IsAlive || _winner != Winner.None) { FinishOlShenCaoPi(f); return; }
        var chooser = r.Stage == OlShenCaoPiStage.DepositChoice ? r.TargetSeat : f.OwnerSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == OlShenCaoPiStage.DepositChoice ? "请选择一张手牌置为“储”。" : "请选择永久获得的技能。", choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == OlShenCaoPiStage.DepositChoice, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ol-shen-cao-pi")] private void AnswerOlCaoPi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlShenCaoPi: { } r } f || !OlCaoPiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("OL Shen Cao Pi rejects a stale private deposit or permanent skill choice.");
        AssertOlShenCaoPi(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == OlShenCaoPiStage.DepositChoice)
        {
            var id = choice.Cards.Single(); var pile = EnsurePublicPileSource(f, int.MaxValue); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { OlShenCaoPi = r with { Pile = pile, DepositedId = id, Before = before, After = before, Stage = OlShenCaoPiStage.DepositChildren } });
            MoveCard(GetHand(_players[r.TargetSeat]).Single(c => c.Id == id), CardLocation.Hand(r.TargetSeat), pile.Location, new("skill-program.ol:chuyuan.deposit"), disclosure: CardMovementDisclosure.FaceUp);
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { OlShenCaoPi = f.OlShenCaoPi! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new OlChuyuanDepositedEvent(f.Id, r.Source, r.TargetSeat, id, pile.Location, before, _movementSequence));
        }
        else
        {
            var skill = choice.Parameters["value"];
            ReplaceRuntimeTop(f = f with { OlShenCaoPi = r with { AcquiredSkillId = skill, Stage = OlShenCaoPiStage.SkillChildren } });
            GrantProgramSkills(f, [skill]); AdvanceEventRulesAndQueueFact(new OlTianxingGrantedEvent(f.Id, f.OwnerSeat, skill));
        }
        ResumeOlShenCaoPi(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.StoreOlChuyuanAfterDamage, SkillProgramEffectOp.AwakenOlTianxing)] private PromptChoice SelectOlCaoPiChoice(PendingDecision p, ProgramSkillFrame f) =>
        f.OlShenCaoPi!.Stage == OlShenCaoPiStage.DepositChoice ? p.Choices.OrderBy(c => GetKeepValue(GetHand(_players[f.OlShenCaoPi.TargetSeat]).Single(card => card.Id == c.Cards.Single()), _players[f.OlShenCaoPi.TargetSeat])).First() : p.Choices.First();
    [ProgramContinuation(ProgramContinuationKind.Resume, 40550)] private bool ResumeOlShenCaoPi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlShenCaoPi: { } r } f || f.Id != id) return false;
        if (r.Stage == OlShenCaoPiStage.Drawing) throw new InvalidOperationException("Chuyuan must resume through its exact native draw return.");
        if (DrainOlShenCaoPi(id)) return true;
        f = GetActiveProgramFrame(id); r = f.OlShenCaoPi!;
        if (r.Stage == OlShenCaoPiStage.DrawChildren)
            ReplaceRuntimeTop(f = f with { OlShenCaoPi = r = r with { Stage = OlShenCaoPiStage.DepositChoice } });
        if (r.Stage == OlShenCaoPiStage.AwakeningChildren && r.Operation == SkillProgramEffectOp.AwakenOlTianxing)
        {
            ReplaceRuntimeTop(f = f with { OlShenCaoPi = r = r with { Stage = OlShenCaoPiStage.SkillChoice } });
            var owner = _players[f.OwnerSeat]; foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
            foreach (var grant in owner.SkillGrants.Grants.Where(g => g.SkillId == "ol:chuyuan").ToArray()) owner.SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new CharacterSkillsLostEvent(owner.Seat, owner.Seat, Array.AsReadOnly(new[] { "ol:chuyuan" })));
            if (DrainOlShenCaoPi(id)) return true;
        }
        if (r.Stage is OlShenCaoPiStage.DepositChoice or OlShenCaoPiStage.SkillChoice) PublishOlCaoPiChoice(f); else FinishOlShenCaoPi(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40550)] private bool ReturnOlCaoPiMovement(ProgramSkillFrame f) => f.OlShenCaoPi is not null && ResumeOlShenCaoPi(f.Id);
    private void FinishOlShenCaoPi(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { OlShenCaoPi = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40550)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40550)] private bool RetainOlCaoPi(ProgramSkillFrame f) => f.OlShenCaoPi is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40550)] private void AssertOlShenCaoPi(ProgramSkillFrame f)
    {
        if (f.OlShenCaoPi is not { } r) return;
        var storage = r.Operation == SkillProgramEffectOp.StoreOlChuyuanAfterDamage;
        if (r.Source != OlCaoPiSource(f) || r.GameplayHash != f.GameplayHash || r.ParentId != f.WindowContext?.ParentFrameId || !Enum.IsDefined(r.Stage) ||
            f.InstructionIndex != (storage ? 1 : 2) || !OlCaoPiParentMatches(f, storage) || !IsValidPlayerSeat(r.TargetSeat) ||
            !storage && r.TargetSeat != f.OwnerSeat || storage && r.TargetSeat != f.WindowContext!.TargetSeat || r.Cards is not System.Collections.IList { IsReadOnly: true } ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == f.WindowContext!.Window) != 1)
            throw new InvalidOperationException("OL Shen Cao Pi lost his exact original binding, candidate, source or immutable payment receipt.");
        if (r.DrawOutcome is { } draw && (draw.RecipientSeat != r.TargetSeat || draw.RequestedCount != 1 || ProgramEventHistory<OlChuyuanDrawReturnedEvent>()
            .Count(e => e.FrameId == f.Id && e.RecipientSeat == r.TargetSeat && e.Actual == draw.ActualDrawIds.Count && e.Before == draw.SequenceBefore && e.After == draw.SequenceAfter) != 1))
            throw new InvalidOperationException("Chuyuan's post-draw choice must retain its exact once-paid native request.");
        if (r.DepositedId is { } deposited && (r.Pile is null || ProgramEventHistory<OlChuyuanDepositedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.CardId == deposited && e.Pile == r.Pile.Location && e.Before == r.Before && e.After == r.After) != 1 ||
            CardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray() is not [var move] || move.CardId != deposited || move.From != CardLocation.Hand(r.TargetSeat) || move.To != r.Pile.Location))
            throw new InvalidOperationException("Chuyuan's deposit must remain the real chosen recipient hand entity and original public pile movement.");
        if (!storage && (ProgramEventHistory<OlCaoPiPileObtainedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Count == r.Cards.Count && e.Before == r.Before && e.After == r.After) != 1 ||
            !CardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Select(m => m.CardId).SequenceEqual(r.Cards) ||
            CardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Any(m => r.Pile is null || m.From != r.Pile.Location || m.To != CardLocation.Hand(f.OwnerSeat))))
            throw new InvalidOperationException("Cao Pi's awakening must obtain its frozen original Chu entities exactly once.");
        if (r.AcquiredSkillId is { } acquired && (r.Stage != OlShenCaoPiStage.SkillChildren || !OlShenCaoPiComposition.TianxingReferences.Skip(1).Contains(acquired) ||
            ProgramEventHistory<OlTianxingGrantedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.AcquiredSkillId == acquired) != 1))
            throw new InvalidOperationException("Tianxing must retain its exact permanent formal skill choice.");
    }
}
