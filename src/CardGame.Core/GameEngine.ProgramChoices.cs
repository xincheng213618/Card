namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ChooseProgramOption(frame, chooserSeat, resultBind, options);

    private void ResolveProgramOptionChoice(PromptChoice selected) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ResolveProgramOptionChoice(selected);

    private bool IsClaimableProgramOptionStillAvailable(PromptChoice selected) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().IsClaimableProgramOptionStillAvailable(selected);

    private bool IsProgramOptionAvailable(ProgramSkillFrame frame, int chooserSeat, SkillProgramChoiceOption option) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().IsProgramOptionAvailable(frame, chooserSeat, option);

    private int CountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().CountChooserProgramBoundCards(frame, bind, chooserSeat);

    private static int ResolveProgramEffectTarget(ProgramSkillFrame frame, SkillProgramEffectTarget target) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramChoices.ResolveProgramEffectTarget(frame,target);

    [ProgramAiChoice(SkillProgramEffectOp.ChooseOption)]
    private PromptChoice SelectAiProgramOption(PendingDecision decision, ProgramSkillFrame frame)
    {
        var program = _contentRegistry!.GetSkill(frame.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        var effect = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        var alternating = plan.Instructions.Skip(frame.InstructionIndex).FirstOrDefault(e=>e.Op==SkillProgramEffectOp.ApplyAlternatingChoiceBenefit && e.SourceBind==effect.ResultBind);
        if(alternating is not null)
        {
            var last=CompleteProgramEventHistory().OfType<AlternatingChoiceBenefitResolvedEvent>().LastOrDefault(e=>e.OwnerSeat==frame.OwnerSeat && e.SkillId==frame.SkillId && e.SkillInstanceId==frame.SkillInstanceId && e.StateId==alternating.StateId);
            var preferred=last?.PendingOptionId=="draw"?"targets":"draw";
            return decision.Choices.First(c=>c.Parameters.GetValueOrDefault("option-id")==preferred);
        }
        var owner = CreateSkillContext(_players[frame.OwnerSeat]);
        var chooser = CreateSkillContext(_players[decision.PlayerSeat]);
        var context = CreateProgramAiPublicContext(_players[frame.OwnerSeat]) with
        {
            SelectedTarget = frame.SelectedTargetSeats.Count == 1 ? CreateSkillContext(_players[frame.SelectedTargetSeats[0]]) : null,
            Actor = frame.WindowContext?.CardUse is { } card ? CreateSkillContext(_players[card.ActorSeat]) : null,
            BooleanState = stateId => GetProgramBooleanState(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId),
            PindianWon = bind => frame.PindianResultBindings.SingleOrDefault(item => item.Name == bind)?.SourceWon ?? false,
            PindianOutcome = bind => frame.PindianResultBindings.SingleOrDefault(item => item.Name == bind)?.SourceWon,
            HasClaimableDamageCards = effect.Options.Any(option => option.Condition.ContainsHasClaimableDamageCards()) &&
                GetClaimableProgramDamageCards(frame).Length > 0,
            HasOwnedCardCategory = (zones, categories, kinds) =>
                (zones.Contains(CardZoneKind.Equipment) &&
                 GetEquipment(_players[decision.PlayerSeat]).Any(card =>
                     MatchesProgramCardFilter(card.Kind, categories, kinds))) ||
                (zones.Contains(CardZoneKind.Hand) &&
                 GetHand(_players[decision.PlayerSeat]).Any(card =>
                     MatchesProgramCardFilter(card.Kind, categories, kinds)))
        };
        return decision.Choices.Select(choice =>
        {
            var facts = context with { ChoiceResult = bind => bind == choice.Parameters["result-bind"]
                ? choice.Parameters["option-id"] : frame.ChoiceBindings.SingleOrDefault(item => item.Name == bind)?.OptionId };
            var score = ProgramChoiceAi.Score(plan.Instructions.Skip(frame.InstructionIndex), owner, chooser,
                facts, bind => TryCountChooserProgramBoundCards(frame, bind, decision.PlayerSeat));
            return (Choice: choice, Score: score);
        }).OrderByDescending(item => item.Score).ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).First().Choice;
    }

    private int? TryCountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().TryCountChooserProgramBoundCards(frame, bind, chooserSeat);
}
