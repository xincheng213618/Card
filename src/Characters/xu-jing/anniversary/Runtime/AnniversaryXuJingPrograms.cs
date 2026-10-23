using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryXuJingHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryXuJingDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new AnniversaryXuJingHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class ResolveAnniversaryXuJingShangyuDescriptor() : AnniversaryXuJingDescriptor(SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu);
internal sealed class ResolveAnniversaryXuJingCaixiaDescriptor() : AnniversaryXuJingDescriptor(SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia);
internal sealed class AnniversaryXuJingHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryXuJingHost>().Begin(frame, operation);
}
public enum AnniversaryXuJingStage { GiftChoice, CountChoice, MovementChildren, DrawReady, Drawing, DrawChildren }
public sealed record AnniversaryXuJingStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, string Mode, int CardId, int UserSeat, long DamageId) : IGameEvent;
public sealed record AnniversaryXuJingMarkedEvent(long FrameId, CardConversionSource Source, int CardId) : IGameEvent;
public sealed record AnniversaryXuJingGiftIssuedEvent(long FrameId, CardConversionSource Source, int CardId, int TargetSeat, int TurnNumber, int TurnOwner) : IGameEvent;
public sealed record AnniversaryXuJingMovedEvent(long FrameId, int CardId, CardLocation From, CardLocation To, long Before, long After) : IGameEvent;
public sealed record AnniversaryXuJingDrawReturnedEvent(long FrameId, int Ordinal, int RecipientSeat, int RequestedCount, int ActualCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record AnniversaryXuJingCaixiaIssuedEvent(long FrameId, CardConversionSource Source, int Count) : IGameEvent;
public sealed record AnniversaryXuJingCaixiaUseEvent(long DebtFrameId, int OwnerSeat, long OriginFrameId, long? ActionId, int? InstructionIndex) : IGameEvent;
public sealed record AnniversaryXuJingReceipt(CardConversionSource Source, string Hash, long ParentId, string Mode, AnniversaryXuJingStage Stage, int CardId, int UserSeat, long DamageId)
{
    public int TargetSeat { get; init; } = -1;
    public int Count { get; init; }
    public int RewardIndex { get; init; }
    public CardLocation? MovementSource { get; init; }
    public CardLocation? MovementDestination { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<NativeDrawOutcome> _draws = [];
    public IReadOnlyList<NativeDrawOutcome> Draws { get => _draws; init => _draws = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryXuJingReceipt? AnniversaryXuJing { get; init; } }

internal static class AnniversaryXuJingComposition
{
    internal const string Shangyu = "anniversary:shangyu", Caixia = "anniversary:caixia";
    [ProgramCatalogValidation(43100)] internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu or SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia)) return;
        var valid = program.Id switch
        {
            Shangyu => program.Triggers.Count == 3 && program.Triggers.All(t => !t.Optional && t.UsageScope is null && t.MarkerCost is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]) &&
                program.Triggers.Any(t => t.Id == "opening-slash" && t.Window == SkillProgramTriggerWindow.GameStarting && t.Subject == SkillProgramTriggerSubject.Owner) &&
                program.Triggers.Any(t => t.Id == "marked-slash-damage" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Any && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage) &&
                program.Triggers.Any(t => t.Id == "marked-slash-discard" && t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.Subject == SkillProgramTriggerSubject.Owner && t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard && !t.MovementDiscardOnly),
            Caixia => program.Triggers is [{ Id: "damage-draw", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Any, Optional: true,
                DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, UsageScope: null, MarkerCost: null, Condition.Kind: SkillProgramTriggerConditionKind.Always, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }],
            _ => false
        };
        if (!valid || program.Activations.Count != 0 || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Anniversary Xu Jing requires the complete opening/entity-damage/entity-discard Shangyu and actual-use-priced Caixia pipelines.");
    }
}
