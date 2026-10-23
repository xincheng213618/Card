using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string JiedaoDiscardReason = "skill-program.anniversary:jiedao.discard";
    private static ProgramTriggerCandidate JiedaoCandidate(ProgramSkillFrame f) => new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0, f.WindowContext?.OccurrenceIndex ?? 0);
    [ProgramCapability] private sealed class AnniversaryJiedaoCapability(GameEngine engine) : IAnniversaryJiedaoHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginAnniversaryJiedao(frame, operation); }
    private SkillProgramStepOutcome BeginAnniversaryJiedao(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.AnniversaryJiedao is not null || f.WindowContext is not { } w)
            throw new InvalidOperationException("Jiedao requires its exact unpaid source choice or issued discard obligation.");
        if (operation == SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao)
        {
            if (JiedaoSourceWitness(JiedaoCandidate(f), w) is not { } r || !FirstActualJiedaoSourceDamage(f.OwnerSeat))
                throw new InvalidOperationException("Jiedao cannot modify a later, sourceless or already finalized damage instance.");
            ReplaceRuntimeTop(f = f with { AnniversaryJiedao = new(AnniversaryJiedaoStage.BonusChoice, r) });
        }
        else
        {
            if (!_players[f.OwnerSeat].IsAlive || w.TargetSeat is not { } target || !_players[target].IsAlive ||
                JiedaoCostWitness(JiedaoCandidate(f), w) is not { } paid || paid.Grant is not { } grant ||
                ProgramEventHistory<AnniversaryJiedaoCostBoundEvent>().Any(e => e.GrantFrameId == grant.FrameId))
                throw new InvalidOperationException("Jiedao can only pay once after its original native damage recipient survived.");
            var required = Math.Min(grant.Bonus, JiedaoDiscardableCards(f).Count);
            ReplaceRuntimeTop(f = f with { AnniversaryJiedao = paid with { Required = required } });
            AdvanceEventRulesAndQueueFact(new AnniversaryJiedaoCostBoundEvent(f.Id, grant.FrameId, paid.DamageFrameId!.Value));
            if (required == 0) { FinishAnniversaryJiedao(f); return SkillProgramStepOutcome.AwaitChild; }
        }
        PublishJiedaoChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<Card> JiedaoDiscardableCards(ProgramSkillFrame f) => Array.AsReadOnly(GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(f.OwnerSeat))
        .Where(c => c.Id > 0 && (_cardZones.Contains(CardLocation.Hand(f.OwnerSeat), c.Id) || _cardZones.Contains(CardLocation.Equipment(f.OwnerSeat), c.Id)) &&
            !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray());
    private IReadOnlyList<PromptChoice> JiedaoChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryJiedao!;
        PromptChoice C(string option, string label, int? card = null) => new(new($"jiedao.{f.Id}.{r.Stage}.{option}.{card}.{r.Selected.Count}"), label,
            card is { } id ? [id] : [], [], new Dictionary<string, string> { ["program-action"] = "anniversary-jiedao", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
        if (r.Stage == AnniversaryJiedaoStage.BonusChoice)
            return Array.AsReadOnly(Enumerable.Range(0, checked(r.Origin.MaximumBonus + 1)).Select(n => C(n.ToString(CultureInfo.InvariantCulture), $"本次伤害+{n}" )).ToArray());
        if (r.Stage != AnniversaryJiedaoStage.DiscardChoice) return [];
        var choices = JiedaoDiscardableCards(f).Where(c => !r.Selected.Contains(c.Id) && r.Selected.Count < r.Required)
            .Select(c => C("pick", $"弃置【{ToSnapshot(c).DisplayName}】", c.Id)).ToList();
        choices.AddRange(r.Selected.Select(id => C("unpick", $"撤回【{ToSnapshot(GetAdvancedCard(id)).DisplayName}】", id)));
        if (r.Selected.Count == r.Required) choices.Add(C("finish", $"弃置选中的{r.Required}张牌"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishJiedaoChoice(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, JiedaoChoices(f), f.AnniversaryJiedao!.Stage == AnniversaryJiedaoStage.BonusChoice
            ? "截刀：选择本次来源伤害的增加值。" : $"截刀：原受伤角色未阵亡，须弃置{f.AnniversaryJiedao.Required}张牌。", f.OwnerSeat);
    }
    [ProgramAnswer("anniversary-jiedao")]
    private void AnswerAnniversaryJiedao(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryJiedao: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], JiedaoChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Jiedao lost its original private chooser or current exact choice.");
        AssertJiedaoFrame(f); ClearPendingDecision();
        if (r.Stage == AnniversaryJiedaoStage.BonusChoice)
        {
            var bonus = int.Parse(choice.Parameters["option"], CultureInfo.InvariantCulture);
            AdvanceEventRulesAndQueueFact(new AnniversaryJiedaoChosenEvent(new(f.Id, r.Origin, bonus)));
            FinishAnniversaryJiedao(f); AdvanceRulesAndPublishState(); return;
        }
        var option = choice.Parameters["option"];
        if (option != "finish")
        {
            var id = choice.Cards.Single();
            ReplaceRuntimeTop(f = f with { AnniversaryJiedao = r with { Selected = option == "pick" ? r.Selected.Append(id).ToArray() : r.Selected.Where(c => c != id).ToArray() } });
            PublishJiedaoChoice(f); AdvanceRulesAndPublishState(); return;
        }
        if (r.Selected.Count != r.Required || r.Selected.Any(id => !JiedaoDiscardableCards(f).Any(c => c.Id == id)))
            throw new InvalidOperationException("Jiedao's actual self-discard entities changed before payment.");
        ReplaceRuntimeTop(f = f with { AnniversaryJiedao = r with { Stage = AnniversaryJiedaoStage.DiscardChildren, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Selected, new(JiedaoDiscardReason));
        f = GetActiveProgramFrame(f.Id); r = f.AnniversaryJiedao! with { After = _movementSequence };
        ReplaceRuntimeTop(f = f with { AnniversaryJiedao = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryJiedaoCostPaidEvent(f.Id, r.Grant!.FrameId, r.Before, r.After, r.Selected.Count));
        ContinueAnniversaryJiedao(f.Id); AdvanceRulesAndPublishState();
    }
    private void ContinueAnniversaryJiedao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryJiedao: { } r } f || f.Id != id) return;
        AssertJiedaoFrame(f);
        if (r.Stage != AnniversaryJiedaoStage.DiscardChildren) { if (_pendingDecision is null) PublishJiedaoChoice(f); return; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        FinishAnniversaryJiedao(GetActiveProgramFrame(id));
    }
    private void FinishAnniversaryJiedao(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { AnniversaryJiedao = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 44400)] private bool ResumeAnniversaryJiedao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryJiedao: not null } f || f.Id != id) return false;
        ContinueAnniversaryJiedao(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44400)] private bool ReturnAnniversaryJiedaoMovement(ProgramSkillFrame f)
    { if (f.AnniversaryJiedao is null) return false; ContinueAnniversaryJiedao(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao, SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao)]
    private PromptChoice AiAnniversaryJiedao(PendingDecision prompt, ProgramSkillFrame f) => f.AnniversaryJiedao?.Stage == AnniversaryJiedaoStage.BonusChoice
        ? prompt.Choices.Last() : prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "finish") ?? prompt.Choices.First();
}
