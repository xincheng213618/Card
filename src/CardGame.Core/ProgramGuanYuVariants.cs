namespace CardGame.Core;

// These facts are scalar. Issued policies survive skill loss and expire at their
// actual phase/turn/round boundary; unfinished choices remain on ProgramSkillFrame.
public sealed record GuanPhaseFocusEvent(long FrameId, int OwnerSeat, string SkillId, int TargetSeat, int TurnNumber, int PhaseSerial) : IGameEvent;
public sealed record GuanFocusedSlashEvent(long ActionId, int OwnerSeat, int TargetSeat, int TurnNumber, int PhaseSerial) : IGameEvent;
public sealed record GuanLethalSparedEvent(long FrameId, int OwnerSeat, int TargetSeat, int TurnNumber) : IGameEvent;
public sealed record GuanOpeningUseEvent(long ActionId, int OwnerSeat, int TurnNumber, bool IsRed, bool IsBasic, bool IsWeilin) : IGameEvent;
public sealed record WeilinHandColorEvent(long ActionId, CardConversionSource Source, int TargetSeat, int TurnNumber, bool IsRed) : IGameEvent;
public sealed record DemonRoundFocusEvent(long FrameId, CardConversionSource Source, int TargetSeat, int RoundNumber) : IGameEvent;
public sealed record DemonSupportGrantedEvent(long FrameId, int OwnerSeat, int RecipientSeat, string SkillId) : IGameEvent;

internal interface IGuanYuVariantHost
{
    SkillProgramStepOutcome ExecuteGuanYuVariant(ProgramSkillFrame frame, SkillProgramEffectOp op);
}

internal sealed class GuanYuVariantHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IGuanYuVariantHost)host).ExecuteGuanYuVariant(frame, op);
}

internal abstract class GuanYuVariantDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new GuanYuVariantHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy => Op switch
    {
        SkillProgramEffectOp.ArmPhaseSlashFocus => new(ProgramOperationAiSemantic.GainCards,
            static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, effect.Condition))),
        SkillProgramEffectOp.HoldRoundFocusCards => new(ProgramOperationAiSemantic.HoldTargetCards,
            static (effect, context) => context.HoldTargetCards(effect)),
        _ => new(ProgramOperationAiSemantic.GainCards, static (_, _) => { })
    };
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.ArmPhaseSlashFocus => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        SkillProgramEffectOp.HoldRoundFocusCards => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)],
        SkillProgramEffectOp.SpareFirstLethalTarget => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
        SkillProgramEffectOp.KillRoundFocusSlashVictim => [new RequireTriggerWindow(SkillProgramTriggerWindow.DamageAppliedBeforeDying)],
        SkillProgramEffectOp.GrantExclusiveRende or SkillProgramEffectOp.GrantExclusiveQingnang or SkillProgramEffectOp.GrantExclusiveLongyin =>
            [new ReadTargetSet(0, 1), new ReleaseTargetSet(), new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)],
        SkillProgramEffectOp.RecordOpeningTurnUse => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]
    };
}
internal sealed class ArmPhaseSlashFocusDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmPhaseSlashFocus; }
internal sealed class RecordPhaseFocusedSlashDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordPhaseFocusedSlash; }
internal sealed class SpareFirstLethalTargetDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpareFirstLethalTarget; }
internal sealed class CancelSparedCardTargetsDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CancelSparedCardTargets; }
internal sealed class RecordOpeningTurnUseDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordOpeningTurnUse; }
internal sealed class RewriteWeilinTargetHandDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewriteWeilinTargetHand; }
internal sealed class HoldRoundFocusCardsDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.HoldRoundFocusCards; }
internal sealed class KillRoundFocusSlashVictimDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.KillRoundFocusSlashVictim; }
internal sealed class GrantExclusiveRendeDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveRende; }
internal sealed class GrantExclusiveQingnangDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveQingnang; }
internal sealed class GrantExclusiveLongyinDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantExclusiveLongyin; }
