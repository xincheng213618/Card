namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5LuoyiNumberPrefix = "shanhe-s5-luoyi-x:";

    private sealed partial class ProgramSkillHost : IShanheS5LuoyiProgramHost
    {
        public void ResolveShanheS5Luoyi(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ResolveShanheS5Luoyi(frame, effect);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5710, SkillProgramEffectOp.ResolveShanheS5Luoyi)]
    private bool CanOfferShanheS5Luoyi(ProgramTriggerLegalityContext c) =>
        c.Owner.HasHpValue && c.Owner.Hp > 0 && c.Owner.Seat == _currentSeat && _phase == TurnPhase.Draw &&
        c.Context.Window == SkillProgramTriggerWindow.DrawPhaseStarting;

    private ProgramLifecycleTriggerWindowFrame ShanheS5LuoyiDrawPlan(ProgramSkillFrame f)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 1 || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Draw ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } context ||
            _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id != context.ParentFrameId ||
            parent.OwnerSeat != f.OwnerSeat || parent.Window != context.Window ||
            parent.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase || parent.NormalDrawReplaced ||
            parent.ActualPhaseSubstitution is not null)
            throw new InvalidOperationException("S5 Luoyi lost its actual owner's pending draw phase.");
        return parent;
    }

    private void ResolveShanheS5Luoyi(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var parent = ShanheS5LuoyiDrawPlan(f);
        if (effect.StateId == "reveal")
        {
            var owner = _players[f.OwnerSeat];
            if (!owner.HasHpValue || owner.Hp <= 0) throw new InvalidOperationException("S5 Luoyi requires defined positive current HP.");
            var key = ShanheS5LuoyiNumberPrefix + effect.ResultBind;
            if (f.NumberBindings.Any(b => b.Name == key)) throw new InvalidOperationException("S5 Luoyi cannot freeze its reveal twice.");
            var x = owner.Hp;
            ReplaceRuntimeTop(f = f with { NumberBindings = f.NumberBindings.Append(new ProgramSkillNumberBinding(key, x)).ToArray() });
            AdvanceEventRulesAndQueueFact(new ShanheS5LuoyiFrozenEvent(f.Id, f.InstructionIndex, f.SkillId, f.SkillInstanceId,
                f.GameplayHash, effect.ResultBind!, parent.Id, f.OwnerSeat, _turnNumber, x));
            RevealProgramTopCards(f.Id, f.OwnerSeat, x, null, effect.ResultBind!, SkillProgramCardSetVisibility.Public);
            return;
        }

        var frozen = ShanheS5LuoyiFrozen(f, effect.SourceBind!);
        if (frozen.DrawPlanFrameId != parent.Id || frozen.ActualTurnNumber != _turnNumber ||
            frozen.InstructionIndex >= f.InstructionIndex ||
            ProgramEventHistory<CardDamageModifierGrantedEvent>().Any(e => e.Modifier.ParentFrameId == f.Id))
            throw new InvalidOperationException("S5 Luoyi lost its unique frozen reveal before draw substitution.");
        ReplaceRuntimeFrame(parent.Id, parent with { ActualPhaseSubstitution = new(parent.Id, f.Id, f.OwnerSeat,
            f.SkillId, f.SkillInstanceId, _turnNumber, SkillProgramTurnPhase.Draw) });
        AdvanceEventRulesAndQueueFact(new ProgramTurnPhaseSubstitutedEvent(f.Id, f.SkillId, GetProgramBindingId(f),
            f.SkillInstanceId, f.OwnerSeat, _turnNumber, SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramTurnPhase.Draw));
        GrantProgramTurnCardDamageModifier(f, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel], frozen.X,
            SkillProgramDamageModifierExpiration.NextOwnerTurnStart, SkillProgramDamageModifierSourceScope.DamageSource);
    }

    private ShanheS5LuoyiFrozenEvent ShanheS5LuoyiFrozen(ProgramSkillFrame f, string bind)
    {
        var values = f.NumberBindings.Where(b => b.Name == ShanheS5LuoyiNumberPrefix + bind).ToArray();
        var facts = ProgramEventHistory<ShanheS5LuoyiFrozenEvent>().Where(e => e.FrameId == f.Id && e.Bind == bind).ToArray();
        var revealed = ProgramEventHistory<ProgramCardsRevealedEvent>().Where(e => e.FrameId == f.Id && e.Bind == bind).ToArray();
        var set = GetProgramCardSet(f, bind);
        if (values is not [var value] || value.Value <= 0 || facts is not [var fact] ||
            fact.X != value.Value || fact.SkillId != f.SkillId || fact.SkillInstanceId != f.SkillInstanceId ||
            fact.GameplayHash != f.GameplayHash || fact.OwnerSeat != f.OwnerSeat || fact.InstructionIndex < 1 ||
            revealed is not [var shown] || shown.SkillId != f.SkillId || shown.OwnerSeat != f.OwnerSeat ||
            shown.BindingId != GetProgramBindingId(f) || shown.Cards.Count > fact.X ||
            !shown.Cards.Select(c => c.Id).SequenceEqual(set.CardIds) || set.Visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException("S5 Luoyi's current-HP value lost its owning public reveal.");
        return fact;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 5710)]
    private void AssertShanheS5LuoyiFrozen(ProgramSkillFrame f)
    {
        foreach (var value in f.NumberBindings.Where(b => b.Name.StartsWith(ShanheS5LuoyiNumberPrefix, StringComparison.Ordinal)))
            _ = ShanheS5LuoyiFrozen(f, value.Name[ShanheS5LuoyiNumberPrefix.Length..]);
    }
}
