namespace CardGame.Core;

public interface IArmorProgramHost
{
    void GrantArmor(ProgramSkillFrame frame);
}

internal sealed class GrantArmorDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantArmor;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantArmorHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantArmor,
        static (effect, context) => context.GrantArmor(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = reader.RequiredInt("amount");
        if (target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.SelectedTarget or
                SkillProgramEffectTarget.SelectedTargets) || amount is < 1 or > 20)
            throw new InvalidOperationException("Armor grants require an owner or selected recipient and amount 1..20.");
        var effect = new SkillProgramEffect(Op, target, amount, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.Target == SkillProgramEffectTarget.SelectedTargets ? [new ReadTargetSet(0)] : WithSelectedTarget(effect);
}

public sealed class GrantArmorHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantArmor;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GetCapability<IArmorProgramHost>().GrantArmor(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private void GrantProgramArmor(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var program = _contentRegistry.GetSkill(active.SkillId).Program!;
        var effect = ProgramInstructionResolver.Default.Resolve(active, program)
            .GetPausedInstruction(active.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.GrantArmor || effect.Amount is < 1 or > 20 ||
            effect.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException("An armor grant lost its exact configured instruction.");
        var index = active.InstructionIndex - 1;
        if (ProgramEventHistory<ArmorGrantedEvent>().Any(grant => grant.ProgramFrameId == active.Id &&
                grant.InstructionIndex == index))
            throw new InvalidOperationException("An armor grant instruction cannot be paid twice.");
        IReadOnlyList<int> targets = effect.Target switch
        {
            SkillProgramEffectTarget.Owner => [active.OwnerSeat],
            SkillProgramEffectTarget.SelectedTarget when active.SelectedTargetSeats.Count == 1 => active.SelectedTargetSeats,
            SkillProgramEffectTarget.SelectedTargets => active.SelectedTargetSeats,
            _ => throw new InvalidOperationException("An armor grant lost its selected recipient set.")
        };
        if (targets.Distinct().Count() != targets.Count || targets.Any(seat => !IsValidPlayerSeat(seat)))
            throw new InvalidOperationException("An armor grant requires distinct actual recipients.");
        if (IsWushuangMode) return;
        var payments = targets.Where(seat => _players[seat].IsAlive).Select(seat =>
            (Seat: seat, Before: _players[seat].Armor, After: checked(_players[seat].Armor + effect.Amount))).ToArray();
        var source = CreateProgramTurnEffectSource(active);
        foreach (var payment in payments)
        {
            _players[payment.Seat].Armor = payment.After;
            AdvanceEventRulesAndQueueFact(new ArmorGrantedEvent(active.Id, index, source, payment.Seat,
                effect.Amount, payment.Before, payment.After));
            AddLog("ArmorGranted", $"{_players[payment.Seat].Name} 获得 {effect.Amount} 点护甲，现有 {payment.After} 点。",
                active.OwnerSeat, payment.Seat);
        }
    }

    [ProgramCapability]
    private sealed class ArmorProgramCapability(GameEngine engine) : IArmorProgramHost
    {
        public void GrantArmor(ProgramSkillFrame frame) => engine.GrantProgramArmor(frame);
    }
}
