using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouCaoPiStage { Choosing, DrawChildren, RecoveryChildren, MaximumChildren, StateChildren, Finished, DrawRequest }
public enum MouCaoPiControlKind { BasicHandOnly, CannotRespond, SkillsInvalid, TrickHandOnly, EquipmentHandOnly }
public sealed record MouCaoPiControl(long FrameId, int SourceSeat, int TargetSeat, int GrantedTurn, MouCaoPiControlKind Kind);
public sealed record MouCaoPiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, int PhaseInstance, MouCaoPiStage Stage, int TargetSeat = -1, string Option = "", int Cost = 0,
    int MarkerBefore = 0, int MarkerAfter = 0, long MovementBefore = 0, long MovementAfter = 0, int ActualDraw = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouCaoPiReceipt? MouCaoPi { get; init; }
}
public sealed record MouCaoPiPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, string Option, int Cost, int Before, int After) : IGameEvent;
public sealed record MouCaoPiControlGrantedEvent(MouCaoPiControl Control) : IGameEvent;
public sealed record MouCaoPiControlExpiredEvent(long FrameId, int TargetSeat, int ActualTurn) : IGameEvent;
public sealed record MouCaoPiBenefitCompletedEvent(long FrameId, int OwnerSeat, int TargetSeat, string Option) : IGameEvent;
public sealed record MouCaoPiInheritedEvent(long FrameId, int OwnerSeat, int DeadSeat, string GeneralId) : IGameEvent;

internal interface IMouCaoPiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouCaoPiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => op is SkillProgramEffectOp.MouCaoPiGainSong or SkillProgramEffectOp.MouCaoPiSongwei
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouCaoPiHandler(op);
}
internal sealed class MouCaoPiGainSongDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiGainSong) { }
internal sealed class MouCaoPiXingshangDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiXingshang) { }
internal sealed class MouCaoPiFangzhuDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiFangzhu) { }
internal sealed class MouCaoPiSongweiDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiSongwei) { }
internal sealed class MouCaoPiRemoveWeiSkillsDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiRemoveWeiSkills) { }
internal sealed class MouCaoPiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMouCaoPiHost>().Resolve(frame, op);
}
internal static class MouCaoPiComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is >= SkillProgramEffectOp.MouCaoPiGainSong and <= SkillProgramEffectOp.MouCaoPiRemoveWeiSkills;
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (!program.Activations.SelectMany(item => item.Effects).Concat(program.Triggers.SelectMany(item => item.Effects)).Any(effect => Handles(effect.Op))) return;
        if (program.Id is not ("mou:xingshang" or "mou:fangzhu" or "mou:songwei") || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.Contributions.Count != 0 || program.DamageModifiers.Count != 0)
            throw new InvalidOperationException(path + ": Mou Cao Pi requires his complete native contracts.");
        var expected = program.Id == "mou:xingshang" ? SkillProgramEffectOp.MouCaoPiXingshang :
            program.Id == "mou:fangzhu" ? SkillProgramEffectOp.MouCaoPiFangzhu : SkillProgramEffectOp.MouCaoPiRemoveWeiSkills;
        if (program.Activations is not [var activation] || activation.MinCards != 0 || activation.MaxCards != 0 ||
            activation.MinTargets != 0 || activation.MaxTargets != 0 || activation.UsesPerTurn is not null ||
            activation.TargetKind != SkillProgramTargetKind.AnyLiving || activation.Condition.Kind != SkillProgramConditionKind.Always || activation.ContinueAfterOwnerDeath ||
            activation.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } effect] || effect.Op != expected ||
            (program.Id == "mou:songwei" ? activation.UsesPerGame != 1 || activation.UsesPerPhase is not null :
                activation.UsesPerGame is not null || activation.UsesPerPhase != (program.Id == "mou:xingshang" ? 2 : 1)))
            throw new InvalidOperationException(path + ": Mou Cao Pi requires zero-input native play menus and exact usage limits.");
        var triggers = program.Triggers;
        if (program.Id == "mou:fangzhu" && triggers.Count != 0 || program.Id == "mou:xingshang" && triggers.Count != 2 || program.Id == "mou:songwei" && triggers.Count != 1)
            throw new InvalidOperationException(path + ": Mou Cao Pi trigger set is incomplete.");
        if (program.Id == "mou:xingshang" && (triggers.Count(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1 ||
            triggers.Count(t => t.Window == SkillProgramTriggerWindow.CharacterDied) != 1))
            throw new InvalidOperationException(path + ": Xingshang needs both the once-turn damage window and independent death window.");
        foreach (var trigger in triggers)
        {
            if (trigger.Optional || trigger.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } benefit] ||
                benefit.Op != (program.Id == "mou:songwei" ? SkillProgramEffectOp.MouCaoPiSongwei : SkillProgramEffectOp.MouCaoPiGainSong))
                throw new InvalidOperationException(path + ": Song gains require their exact mandatory instruction.");
            if (program.Id == "mou:songwei" ? trigger.Window != SkillProgramTriggerWindow.PlayPhaseStarting || trigger.Subject != SkillProgramTriggerSubject.Owner :
                trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied ?
                    trigger.Subject != SkillProgramTriggerSubject.Any || trigger.UsageScope != SkillUsageScope.Turn || trigger.UsageLimit != 1 || trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage :
                    trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.Window != SkillProgramTriggerWindow.CharacterDied || trigger.UsageLimit is not null)
                throw new InvalidOperationException(path + ": Song gains have changed timing, subject or the once-turn damage limit.");
        }
    }
}
