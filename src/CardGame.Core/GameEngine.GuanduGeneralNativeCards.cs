using System.Globalization;

namespace CardGame.Core;

public sealed record GuanduDamagingCardRecordedEvent(long DamageFrameId, long UseId, long ActionId,
    int TargetSeat, CardKind Kind) : IGameEvent;
public sealed record GuanduChoulueArmedEvent(long ProgramFrameId, int OwnerSeat, string InstanceId, int ActualTurn,
    long PhaseBoundaryId, int ProviderSeat, int GiftCardId, int Before, int After, CardKind Kind) : IGameEvent;
public sealed record GuanduYuanlveGiftEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwner, long ActualPhase, int OwnerSeat, int RecipientSeat, int CardId, long Before, long After) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44003)]
    private void CaptureGuanduDamagingCards(IGameEvent payload)
    {
        if (!IsGuanduActivity || payload is not DamageAppliedEvent { Amount: > 0 } applied ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || CurrentDamageAttempt is not { } attack ||
            !attack.DamageWasApplied || attack.IsDelayedJudgmentDamage || damage.ParentFrameId != attack.ResolutionId ||
            damage.TargetSeat != applied.TargetSeat || LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.EffectiveKind != use.CardKind || (!IsSlashCard(use.CardKind) && !IsOrdinaryTrick(use.CardKind)) ||
            ProgramEventHistory<GuanduDamagingCardRecordedEvent>().Any(e => e.DamageFrameId == damage.Id)) return;
        AdvanceEventRulesAndQueueFact(new GuanduDamagingCardRecordedEvent(damage.Id, use.Id, action.ActionId, applied.TargetSeat, use.CardKind));
    }
    private CardKind? GuanduLastDamageCard(int seat) => ProgramEventHistory<GuanduDamagingCardRecordedEvent>()
        .LastOrDefault(e => e.TargetSeat == seat)?.Kind;
    private static string GuanduKindName(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_GuanduGeneralNativeCards.GuanduKindName(kind);

    private bool IsGuanduChoiceResult(ProgramSkillFrame f, ProgramChoiceResultBinding binding)
    {
        if (!IsGuanduActivity || f.SkillId != "guandu:choulue" || f.InstructionIndex < 2 || binding.Name != "choulue-kind" ||
            binding.ChooserSeat != f.OwnerSeat || f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
            !ProgramEventHistory<GuanduGeneralCompletedEvent>().Any(e => e.FrameId == f.Id && e.SkillId == f.SkillId) ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions[0].Op != SkillProgramEffectOp.ResolveGuanduChoulue)
            return false;
        var armed = ProgramEventHistory<GuanduChoulueArmedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (binding.OptionId == "none") return armed.Length == 0 && ProgramEventHistory<GuanduGeneralCompletedEvent>().Any(e => e.FrameId == f.Id && !e.Completed);
        return armed is [var receipt] && receipt.OwnerSeat == f.OwnerSeat && receipt.InstanceId == f.SkillInstanceId &&
            receipt.ActualTurn == _turnNumber && receipt.PhaseBoundaryId == f.WindowContext.ParentFrameId && binding.OptionId == GuanduKindName(receipt.Kind) &&
            _cardMovements.Count(m => m.CardId == receipt.GiftCardId && m.Sequence > receipt.Before && m.Sequence <= receipt.After &&
                m.From.OwnerSeat == receipt.ProviderSeat && m.To == CardLocation.Hand(f.OwnerSeat)) == 1;
    }

    private CardKind? GuanduChoulueFrozenBasicKind(ProgramSkillFrame f)
    {
        if (!IsGuanduActivity || f.SkillId != "guandu:choulue" || f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting) return null;
        var armed = ProgramEventHistory<GuanduChoulueArmedEvent>().SingleOrDefault(e => e.ProgramFrameId == f.Id &&
            e.OwnerSeat == f.OwnerSeat && e.InstanceId == f.SkillInstanceId && e.ActualTurn == _turnNumber &&
            e.PhaseBoundaryId == f.WindowContext.ParentFrameId && IsSlashCard(e.Kind));
        return armed is not null && f.ChoiceBindings.Count(b => b.Name == "choulue-kind" && b.OptionId == GuanduKindName(armed.Kind) &&
            b.ChooserSeat == f.OwnerSeat) == 1 ? armed.Kind : null;
    }

    private void ContinueGuanduChoulue(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage is "begin" or "choulue-target")
        {
            f = GuanduStage(f, "choulue-target");
            PublishGuanduGeneralChoice(f, f.OwnerSeat, _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && GuanduOwnedCards(p.Seat).Length > 0)
                .Select(p => GuanduChoice(f, "target", $"请 {p.Name} 交给你一张牌", targets: [p.Seat]))
                .Append(GuanduChoice(f, "skip", "不发动")).ToArray()); return;
        }
        if (r.Stage == "choulue-gift")
        {
            if (!_players[r.TargetSeat].IsAlive || !_players[f.OwnerSeat].IsAlive) { FinishGuanduGeneral(f, false); return; }
            PublishGuanduGeneralChoice(f, r.TargetSeat, GuanduOwnCardChoices(f, r.TargetSeat, "gift")); return;
        }
        if (r.Stage != "choulue-arm") throw new InvalidOperationException("Choulue lost its paid physical gift.");
        var kind = GuanduLastDamageCard(f.OwnerSeat);
        if (kind is null || !_players[f.OwnerSeat].IsAlive || f.ChoiceBindings.Any(b => b.Name == "choulue-kind"))
        { FinishGuanduGeneral(f, false); return; }
        if (!_cardMovements.Any(m => m.CardId == r.CardId && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
            m.From.OwnerSeat == r.TargetSeat && m.To == CardLocation.Hand(f.OwnerSeat)))
            throw new InvalidOperationException("Choulue cannot arm without its exact paid gift.");
        AdvanceEventRulesAndQueueFact(new GuanduChoulueArmedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, _turnNumber,
            f.WindowContext!.ParentFrameId, r.TargetSeat, r.CardId, r.MovementBefore, r.MovementAfter, kind.Value));
        ReplaceRuntimeTop(f = f with { ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding(
            "choulue-kind", GuanduKindName(kind.Value), f.OwnerSeat)).ToArray()) });
        FinishGuanduGeneral(f);
    }
    private bool IsGuanduChoulueVirtualOrdinaryProgram(ProgramSkillFrame f, SkillProgramEffect effect) => IsGuanduActivity &&
        f.SkillId == "guandu:choulue" && effect.Op == SkillProgramEffectOp.UseVirtualOrdinaryTrick &&
        effect.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "choulue-kind" } &&
        effect.OutputKind is { } kind && IsOrdinaryTrick(kind) && effect.ActorReference?.Kind == ProgramParticipantRef.Owner &&
        effect.TargetReference?.Kind == ProgramParticipantRef.Owner && f.WindowContext?.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
        f.ChoiceBindings.Count(b => b.Name == "choulue-kind" && b.OptionId == GuanduKindName(kind) && b.ChooserSeat == f.OwnerSeat) == 1 &&
        ProgramEventHistory<GuanduChoulueArmedEvent>().Count(e => e.ProgramFrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
            e.InstanceId == f.SkillInstanceId && e.ActualTurn == _turnNumber && e.PhaseBoundaryId == f.WindowContext.ParentFrameId && e.Kind == kind) == 1;

    private IReadOnlyList<PromptChoice> GuanduGiftChoices(ProgramSkillFrame f, bool nonEquipment)
    {
        var cards = GetHand(_players[f.OwnerSeat]).Where(c => !nonEquipment || !EquipmentCatalog.IsEquipment(c.Kind));
        return cards.SelectMany(c => _players.Where(p => p.IsAlive).Select(p => GuanduChoice(f, "gift",
            $"将【{c.DisplayName}】交给 {p.Name}", [c.Id], [p.Seat])))
            .Append(GuanduChoice(f, "skip", "不执行")).ToArray();
    }
    private void ContinueGuanduYuanlve(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage is "begin" or "yuanlve-give")
        { f = GuanduStage(f, "yuanlve-give"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduGiftChoices(f, true)); return; }
        if (r.Stage == "yuanlve-offer")
        {
            if (!_players[r.TargetSeat].IsAlive || _cardZones.GetLocation(r.CardId) != CardLocation.Hand(r.TargetSeat))
            { FinishGuanduGeneral(f); return; }
            var source = HuangQuanSource(f);
            AdvanceEventRulesAndQueueFact(new GuanduYuanlveGiftEvent(f.Id, source, f.GameplayHash, _turnNumber, _currentSeat,
                _cardUseDebitPhaseInstanceId, f.OwnerSeat, r.TargetSeat, r.CardId, r.MovementBefore, r.MovementAfter));
            var pending = new HuangQuanPending(source, f.GameplayHash, f.InstructionIndex, "jianji", r.TargetSeat, _turnNumber,
                _currentSeat, _cardUseDebitPhaseInstanceId, r.MovementBefore, r.MovementAfter, Array.AsReadOnly(new[] { r.CardId }), null, "offer");
            ReplaceRuntimeTop(f = f with { GuanduGeneral = r with { Stage = "native-use" }, HuangQuan = pending });
            PublishJianjiChoice(f); return;
        }
        if (r.Stage == "yuanlve-reward")
        { if (r.Used && _players[f.OwnerSeat].IsAlive) DrawGuanduGeneral(f, f.OwnerSeat, 1, "finish"); else FinishGuanduGeneral(f); return; }
        throw new InvalidOperationException("Yuanlve lost its exact offered entity or completed native return.");
    }
    private bool ValidGuanduYuanlveOffer(ProgramSkillFrame f)
    {
        if (!IsGuanduActivity || f.SkillId != "guandu:yuanlve" || f.InstructionIndex != 1 ||
            f.GuanduGeneral is not { Operation: SkillProgramEffectOp.ResolveGuanduYuanlve, Stage: "native-use" } r ||
            f.HuangQuan is not { Kind: "jianji", CardIds: [var id], Stage: "offer" or "issuing" or "native-use" or "returned" } p ||
            p.Source != HuangQuanSource(f) || p.GameplayHash != f.GameplayHash || p.InstructionIndex != f.InstructionIndex ||
            p.RecipientSeat != r.TargetSeat || id != r.CardId || p.ActualTurn != _turnNumber || p.TurnOwner != _currentSeat ||
            p.ActualPhase != _cardUseDebitPhaseInstanceId || p.Before != r.MovementBefore || p.After != r.MovementAfter ||
            p.After > _movementSequence || p.Occurrence is not null || f.PendingMovementContinuation is not null) return false;
        return ProgramEventHistory<GuanduYuanlveGiftEvent>().Count(e => e == new GuanduYuanlveGiftEvent(f.Id, p.Source,
            f.GameplayHash, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId, f.OwnerSeat, p.RecipientSeat, id, p.Before, p.After)) == 1 &&
            (p.RecipientSeat == f.OwnerSeat && p.Before == p.After || _cardMovements.Count(m => m.CardId == id && m.Sequence > p.Before && m.Sequence <= p.After &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(p.RecipientSeat)) == 1);
    }
    private bool FinishGuanduYuanlveUse(ProgramSkillFrame f)
    {
        if (f.SkillId != "guandu:yuanlve") return false;
        if (!ValidGuanduYuanlveOffer(f) || f.HuangQuan is not { } p || p.Stage is not ("offer" or "returned"))
            throw new InvalidOperationException("Yuanlve must return from its exact native use or declined offer.");
        var used = p.Stage == "returned" && p.NativeUse is { } native && ProgramEventHistory<JianjiUseReturnedEvent>().Count(e => SameJianjiReceipt(e.Receipt, native)) == 1;
        AdvanceEventRulesAndQueueFact(new HuangQuanCompletedEvent(f.Id, p.Source, p.Kind, p.NativeUse?.ActionId));
        ReplaceRuntimeTop(f = f with { HuangQuan = null, PendingMovementContinuation = null,
            GuanduGeneral = f.GuanduGeneral! with { Stage = "yuanlve-reward", Used = used } });
        RunGuanduGeneral(f.Id); return true;
    }

    private GuanduMoushiGiftedEvent? GuanduMoushiOccurrence(int owner, string instance, ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.Amount <= 0 || c.SourceSeat is not { } source || c.TargetSeat is not { } victim ||
            source != _currentSeat || _phase != TurnPhase.Play || !_players[owner].IsAlive || CurrentActualDiscardRecoveryPhase() is not
                { Kind: ActualDiscardRecoveryPhaseKind.Play } current || current.ActorSeat != source) return null;
        var history = CompleteProgramEventHistory().ToArray();
        foreach (var gift in history.OfType<GuanduMoushiGiftedEvent>().Where(e => e.OwnerSeat == owner && e.InstanceId == instance && e.RecipientSeat == source))
        {
            var index = Array.FindIndex(history, e => e is GuanduMoushiGiftedEvent candidate && candidate == gift);
            var nextTurn = history.Skip(index + 1).OfType<TurnStartedEvent>().FirstOrDefault(e => e.ActorSeat == source);
            if (nextTurn?.TurnNumber == current.TurnNumber && !history.OfType<GuanduMoushiDrawnEvent>().Any(d =>
                d.ProducerFrameId == gift.FrameId && d.ActualPlayToken == current.Token && d.VictimSeat == victim)) return gift;
        }
        return null;
    }
    private bool GuanduMoushiQualifies(int owner, string instance, ProgramSkillWindowContext c) => GuanduMoushiOccurrence(owner, instance, c) is not null;
    private void ContinueGuanduMoushi(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (f.TriggerId is not null)
        {
            var gift = GuanduMoushiOccurrence(f.OwnerSeat, f.SkillInstanceId, f.WindowContext!);
            if (gift is null) { FinishGuanduGeneral(f, false); return; }
            AdvanceEventRulesAndQueueFact(new GuanduMoushiDrawnEvent(gift.FrameId, f.OwnerSeat, _turnNumber,
                _cardUseDebitPhaseInstanceId, f.WindowContext!.TargetSeat!.Value, f.WindowContext.DamageFrameId!.Value,
                CurrentActualDiscardRecoveryPhase()!.Token));
            DrawGuanduGeneral(f, f.OwnerSeat, 1, "finish"); return;
        }
        if (r.Stage is "begin" or "moushi-give")
        { f = GuanduStage(f, "moushi-give"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduGiftChoices(f, false)); return; }
        if (r.Stage != "moushi-arm") throw new InvalidOperationException("Moushi lost its gift payment.");
        AdvanceEventRulesAndQueueFact(new GuanduMoushiGiftedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, r.TargetSeat, r.CardId, _turnNumber));
        FinishGuanduGeneral(f);
    }
}
