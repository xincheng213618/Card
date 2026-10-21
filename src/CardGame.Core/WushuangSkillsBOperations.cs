using System.Text.Json.Serialization;

namespace CardGame.Core;

// Pending selections and native child returns belong to the originating program.
public sealed record WushuangBReceipt(int InstructionIndex, SkillProgramEffectOp Operation, string Stage,
    int TargetSeat = -1, bool? Red = null, long Before = 0, long After = 0, long? ChildUseId = null,
    CardKind? OutputKind = null, int Damage = 0, bool EnteredDying = false)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangBReceipt? WushuangB { get; init; }
}
public sealed record WushuangBNativeReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, long CardUseFrameId, CardKind OutputKind);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangBNativeReturn? WushuangBReturn { get; init; }
}
public sealed record WushuangWeiyiUsedEvent(int OwnerSeat, string SkillId, int TargetSeat, long ProgramFrameId) : IGameEvent;
public sealed record WushuangZhendanDisabledEvent(int OwnerSeat, string SkillId, int RoundNumber, long ProgramFrameId) : IGameEvent;
public sealed record WushuangCardUseNameRecordedEvent(int TurnNumber, int RoundNumber, int ActorSeat, CardKind Name,
    long CardActionId, long OriginUseId) : IGameEvent;
public sealed record WushuangYinglueArmedEvent(long ProgramFrameId, int OwnerSeat, int TargetSeat, TurnPhase Phase, int Amount) : IGameEvent;
public sealed record WushuangYinglueAppliedEvent(long ProgramFrameId, int TargetSeat, TurnPhase Phase, int Amount, int TurnNumber) : IGameEvent;
public sealed record WushuangBianyuMarkedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int CardId, long MovementSequence, long ActionSequence) : IGameEvent;
public sealed record WushuangXiaojieAcceptedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long IncomingUseId) : IGameEvent;
public sealed record WushuangDinganDrawnEvent(int OwnerSeat, int TargetSeat, int TurnNumber, long ProgramFrameId) : IGameEvent;
public sealed record WushuangBNativeIssuedEvent(WushuangBNativeReturn Return) : IGameEvent;
public sealed record WushuangBNativeReturnedEvent(WushuangBNativeReturn Return) : IGameEvent;

internal interface IWushuangSkillsBHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class WushuangBOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new WushuangBOperationHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveWushuangWeiyi or SkillProgramEffectOp.ResolveWushuangBianyu => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        SkillProgramEffectOp.DrawWushuangZhendan => [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.RoundEnding])],
        SkillProgramEffectOp.ResolveWushuangLiantao => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        SkillProgramEffectOp.ArmWushuangXiaojie => [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashBeforeResponse)],
        SkillProgramEffectOp.ResolveWushuangXiaojie => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)],
        SkillProgramEffectOp.ResolveWushuangDingan => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        SkillProgramEffectOp.RecoverWushuangChunlao => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpLost)],
        SkillProgramEffectOp.ResolveWushuangGuzheng => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseEnded)],
        SkillProgramEffectOp.ArmWushuangYinglue => [new ReadSelectedTarget()],
        _ => []
    };
}
internal sealed class WushuangWeiyiDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangWeiyi; }
internal sealed class WushuangZhendanDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawWushuangZhendan; }
internal sealed class WushuangYinglueDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWushuangYinglue; public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase, Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase); }
internal sealed class WushuangLiantaoDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangLiantao; }
internal sealed class WushuangBianyuDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangBianyu; }
internal sealed class WushuangXiaojieArmDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWushuangXiaojie; }
internal sealed class WushuangXiaojieUseDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangXiaojie; }
internal sealed class WushuangDinganDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangDingan; }
internal sealed class WushuangChunlaoRecoverDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverWushuangChunlao; }
internal sealed class WushuangBOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWushuangSkillsBHost>().Resolve(f, Op);
}

internal sealed class WushuangDinganCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "wushuang:skill-3335";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.Window != SkillProgramTriggerWindow.CardUseCommitted || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer ||
            !trigger.IncludeResponseUses || trigger.Optional || trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveWushuangDingan }])
            throw new InvalidOperationException("Dingan requires its mandatory committed-use observer, including true response uses.");
    }
}

internal sealed class WushuangGuzhengDescriptor : WushuangBOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangGuzheng; }
internal sealed class WushuangGuanchaoCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "wushuang:skill-3076";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.Window != SkillProgramTriggerWindow.CardUseCommitted || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !trigger.IncludeResponseUses || trigger.Optional || trigger.Effects is not [{ Op: SkillProgramEffectOp.GuanchaoRankDraw }])
            throw new InvalidOperationException("Official Guanchao must observe all actual own-turn card uses.");
    }
}
