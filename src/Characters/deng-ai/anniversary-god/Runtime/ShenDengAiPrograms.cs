using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ShenDengAiDomain { Qingqu = 1, Fengtian = 2, Junshan = 3 }
public enum ShenDengAiStage { Allocate, Develop, DrawAttempt, DrawChildren, MoveSeat, RepeatChoice, NativeUse, NativeChildren }
public sealed record ShenDengAiReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int InstructionIndex, ShenDengAiStage Stage, int DamageCount = 0, int CardId = 0,
    long OriginalUseId = 0, long OriginalActionId = 0, CardKind? OutputKind = null,
    int TargetSeat = -1, long UseId = 0, long ActionId = 0, bool Returned = false,
    bool OriginalAttackActive = false, bool OriginalDuelActive = false, bool OriginalGroupActive = false,
    int CarrierId = 0, int DrawCount = 0, NativeDrawOutcome? DrawOutcome = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenDengAiReceipt? ShenDengAi { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenDengAiIssuedDomain? ShenDengAiDomain { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenDengAiNativeOrigin? ShenDengAiCuixin { get; init; }
}
public sealed record ShenDengAiIssuedDomain(long ActionId, int ActorSeat, int DomainMask);
public sealed record ShenDengAiNativeOrigin(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalUseId, long OriginalActionId, long UseId, long ActionId, int ActorSeat, int TargetSeat, CardKind Kind, int CarrierId = 0);
public sealed record ShenDengAiInitializedEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record ShenDengAiDevelopedEvent(long FrameId, int OwnerSeat, ShenDengAiDomain Domain) : IGameEvent;
public sealed record ShenDengAiAssignedEvent(long FrameId, int OwnerSeat, int CardId, ShenDengAiDomain Domain,
    int MovementSequence) : IGameEvent;
public sealed record ShenDengAiXianjinReservedEvent(long FrameId, int OwnerSeat, int DamageCount) : IGameEvent;
public sealed record ShenDengAiDrawReturnedEvent(long FrameId, int OwnerSeat, int RequestedCount, int ActualCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record ShenDengAiAwakenedEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record ShenDengAiDomainIssuedEvent(long UseId, long ActionId, int ActorSeat, int DomainMask) : IGameEvent;
public sealed record ShenDengAiNativeIssuedEvent(ShenDengAiNativeOrigin Origin) : IGameEvent;
public sealed record ShenDengAiNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record ShenDengAiCursorSuspendedEvent(long FrameId, long OriginalUseId, long OriginalActionId) : IGameEvent;
public sealed record ShenDengAiCursorRestoredEvent(long FrameId, long OriginalUseId, long OriginalActionId) : IGameEvent;
public sealed record ShenDengAiNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record ShenDengAiCarrierCreatedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int CardId, CardKind Kind) : IGameEvent;
public sealed record ShenDengAiCarrierDestroyedEvent(int CardId, int MovementSequence, CardLocation From, CardMoveReason Reason) : IGameEvent;

internal interface IShenDengAiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShenDengAiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShenDengAiHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => Op == SkillProgramEffectOp.RepeatShenDengAiCuixin
        ? new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeActionAppearance) : default;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows(Op switch {
            SkillProgramEffectOp.InitializeShenDengAiTuoYu => [SkillProgramTriggerWindow.GameStarting],
            SkillProgramEffectOp.AllocateShenDengAiTuoYu => [SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTriggerWindow.PlayEnding],
            SkillProgramEffectOp.DevelopShenDengAiXianjin => [SkillProgramTriggerWindow.AfterDamageApplied],
            SkillProgramEffectOp.AwakenShenDengAiQijing => [SkillProgramTriggerWindow.AfterTurnEnded],
            _ => [SkillProgramTriggerWindow.CardUseCompleted] })];
}
internal sealed class InitializeShenDengAiDescriptor : ShenDengAiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeShenDengAiTuoYu; }
internal sealed class AllocateShenDengAiDescriptor : ShenDengAiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AllocateShenDengAiTuoYu; }
internal sealed class DevelopShenDengAiDescriptor : ShenDengAiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DevelopShenDengAiXianjin; }
internal sealed class AwakenShenDengAiDescriptor : ShenDengAiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenShenDengAiQijing; }
internal sealed class RepeatShenDengAiDescriptor : ShenDengAiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatShenDengAiCuixin; }
internal sealed class ShenDengAiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShenDengAiHost>().Resolve(frame, op);
}

internal static class ShenDengAiComposition
{
    [ProgramCatalogValidation(38500)]
    internal static void Validate(string path, SkillProgram p)
    {
        var native = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.InitializeShenDengAiTuoYu or SkillProgramEffectOp.AllocateShenDengAiTuoYu or
                SkillProgramEffectOp.DevelopShenDengAiXianjin or SkillProgramEffectOp.AwakenShenDengAiQijing or SkillProgramEffectOp.RepeatShenDengAiCuixin);
        if (!native) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.Triggers.Any(t => t.Optional || t.UsageLimit is not null || t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path + ": Shen Deng Ai requires its complete native locked occurrence composition.");
        var valid = p.Id switch
        {
            "anniversary-god:tuoyu" => p.Triggers is [var init, var start, var end] &&
                Exact(init, "initialize-domains", SkillProgramTriggerWindow.GameStarting, SkillProgramEffectOp.InitializeShenDengAiTuoYu) &&
                Exact(start, "allocate-play-start", SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramEffectOp.AllocateShenDengAiTuoYu) &&
                Exact(end, "allocate-play-end", SkillProgramTriggerWindow.PlayEnding, SkillProgramEffectOp.AllocateShenDengAiTuoYu) &&
                start.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
            "anniversary-god:xianjin" => p.Triggers is [var damage] &&
                Exact(damage, "two-damage-occurrences", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.DevelopShenDengAiXianjin, SkillProgramTriggerSubject.Any) &&
                damage.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage,
            "anniversary-god:qijing" => p.Triggers is [{ Id: "all-domains-developed", Window: SkillProgramTriggerWindow.AfterTurnEnded,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Effects:
                [{ Op: SkillProgramEffectOp.AwakenShenDengAiQijing, Target: SkillProgramEffectTarget.Owner },
                 { Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: -1 },
                 { Op: SkillProgramEffectOp.GrantSkills, Target: SkillProgramEffectTarget.Owner, SkillIds: ["anniversary-god:cuixin"] },
                 { Op: SkillProgramEffectOp.AwakenShenDengAiQijing, Target: SkillProgramEffectTarget.Owner }] }],
            "anniversary-god:cuixin" => p.Triggers is [var use] &&
                Exact(use, "repeat-neighbor-hand-use", SkillProgramTriggerWindow.CardUseCompleted, SkillProgramEffectOp.RepeatShenDengAiCuixin, null) &&
                use.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && use.CardKinds.Count == 0 && use.CardCategories.Count == 0,
            _ => false
        };
        if (!valid || p.Triggers.SelectMany(t => t.Effects).Any(e => e.Condition.Kind != SkillProgramConditionKind.Always || e.TargetReference is not null || e.ActorReference is not null))
            throw new InvalidOperationException(path + ": Shen Deng Ai lost its exact initialization, allocation, combined damage counting, awakening pipeline or completed-hand-use origin.");
    }
    private static bool Exact(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op,
        SkillProgramTriggerSubject? subject = SkillProgramTriggerSubject.Owner) => t.Id == id && t.Window == window && t.Subject == subject &&
        t.Effects is [{ Target: SkillProgramEffectTarget.Owner } e] && e.Op == op;
}
