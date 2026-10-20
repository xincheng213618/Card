using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31600, SkillProgramEffectOp.ResolveMouLuBuWushuangBonus)]
    private bool CanMouLbBonus(int owner, ProgramSkillWindowContext c) => c.Window == SkillProgramTriggerWindow.BeforeDamageApplied && c.SourceSeat == owner && c.TargetSeat is { } victim &&
        victim != owner && c.Amount > 0 && CurrentDamageAttempt is { IsSourceLess: false } attack && attack.SourceSeat == owner && attack.TargetSeat == victim &&
        attack.EffectiveCardKind is { } kind && (IsSlashCard(kind) || kind == CardKind.Duel) &&
        !ProgramEventHistory<MouLuBuWushuangBonusEvent>().Any(e => e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber) &&
        (IsSlashCard(kind) ? attack is CardAttackHandle slash && slash.SuccessfulDodgeResponses == 0 :
            !ProgramEventHistory<DuelResponseEvent>().Any(e => e.ResolutionId == attack.ResolutionId && e.ResponderSeat == victim && e.UsedSlash));
    private SkillProgramStepOutcome ResolveMouLbBonus(ProgramSkillFrame f)
    {
        var c = f.WindowContext!;
        if (!CanMouLbBonus(f.OwnerSeat, c)) return SkillProgramStepOutcome.Continue;
        if (_resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not BeforeDamageProgramWindowFrame window || CurrentDamageAttempt is not { } attack)
            throw new InvalidOperationException("Mobile Wushuang lost the original damage window.");
        attack.IncreaseFinalizedDamageAmount(1);
        ReplaceRuntimeFrame(window.Id, window with { Amount = checked(window.Amount + 1) });
        AdvanceEventRulesAndQueueFact(new MouLuBuWushuangBonusEvent(f.Id, MouLbSource(f), attack.ResolutionId, window.Id, _turnNumber, attack.TargetSeat, attack.EffectiveCardKind!.Value, window.Amount, window.Amount + 1));
        return SkillProgramStepOutcome.Continue;
    }
}
