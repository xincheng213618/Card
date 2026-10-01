namespace CardGame.Core;

internal sealed class DrawAllHandSelectedBonusProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAllHandSelectedBonus;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawAllHandSelectedBonusProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: the bonus belongs to owner.");
        return new(Op, SkillProgramEffectTarget.Owner, DrawProgramOperationDescriptor.Amount(reader, 20), reader.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class DrawAllHandSelectedBonusProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAllHandSelectedBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.DrawAllHandSelectedBonus(frame, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private bool BypassesSlashLimitBySuit(CharacterState owner, Card card, CardKind effectiveKind) =>
        HasPhaseSuitAllowance(owner,card) || CardPolicies(owner, SkillProgramCardPolicyKind.BypassSlashLimitBySuit, effectiveKind)
            .Any(item => item.Policy.InputSuit == EffectiveSuit(owner, card));
    private bool CanUsePeachToRescue(int responderSeat, int victimSeat) =>
        !(responderSeat == victimSeat && HasSelfCardTargetProhibition(responderSeat)) &&
        (responderSeat == _currentSeat || responderSeat == victimSeat ||
        !_players[_currentSeat].IsAlive ||
        !HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.ExclusiveDyingPeachRescue, CardKind.Peach));

    private sealed partial class ProgramSkillHost
    {
        public void DrawAllHandSelectedBonus(ProgramSkillFrame frame, int amount)
        {
            if (frame.SelectedAllOwnerHandCards is not true) return;
            engine.DrawProgramCards(frame.Id, frame.OwnerSeat, amount, null, null,
                SkillProgramCardSetVisibility.Private,
                new CardMoveReason($"skill-program.{frame.SkillId}.{frame.ActivationId}.all-hand-bonus"));
        }
    }
}
