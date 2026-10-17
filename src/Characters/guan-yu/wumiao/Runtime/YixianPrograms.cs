using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum YixianStage { BranchChoice, EquipmentChildren, RecipientChoice, Drawing, DrawChildren, RecoveryChildren }
public enum YixianBranch { Field, DiscardPile }
public sealed record YixianEntity(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long MovementSequence);
public sealed record YixianRecipient(int Seat, int ObtainedCount);
public sealed record YixianReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int TurnOwner, YixianStage Stage)
{
    public bool Paid { get; init; }
    public YixianBranch? Branch { get; init; }
    public long BatchId { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public int Cursor { get; init; }
    public bool BenefitAccepted { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<YixianEntity> _entities = Array.AsReadOnly(Array.Empty<YixianEntity>());
    public IReadOnlyList<YixianEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<YixianRecipient> _recipients = Array.AsReadOnly(Array.Empty<YixianRecipient>());
    public IReadOnlyList<YixianRecipient> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YixianReceipt? Yixian { get; init; }
}
public sealed record YixianPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwner, YixianBranch Branch, long BatchId, int EntityCount, int RecipientCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record YixianEntityObtainedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record YixianRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat, int ObtainedCount) : IGameEvent;
public sealed record YixianRecipientDecisionEvent(long FrameId, int Index, int RecipientSeat, int ObtainedCount, bool Accepted) : IGameEvent;
public sealed record YixianDrawReturnedEvent(long FrameId, int Index, int RecipientSeat, int RequestedCount,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record YixianRecoveryIssuedEvent(long FrameId, int Index, int RecipientSeat, bool RecipientAlive) : IGameEvent;
public sealed record YixianCompletedEvent(long FrameId, bool Paid, YixianBranch? Branch, int EntityCount, int AcceptedRecipients) : IGameEvent;

internal interface IWuYixianHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveYixianAllEquipmentObtainAndCompensateDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new WuYixianHandler();
}
internal sealed class WuYixianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IWuYixianHost>().Resolve(frame);
}
internal static class WuYixianComposition
{
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var owns = program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate);
        if (!owns && program.Id != "wumiao:yixian") return;
        if (program.Id != "wumiao:yixian" || program.Triggers.Count != 0 || program.ViewAs.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 ||
            program.DamageModifiers.Count != 0 || program.HpSkillGrants.Count != 0 || program.Contributions.Count != 0 ||
            program.BooleanStates.Count != 0 || program.Activations is not [{ } a] || a.Id != "all-equipment" ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null ||
            a.CardKinds.Count != 0 || a.CardCategories.Count != 0 || a.CardSuits.Count != 0 ||
            a.EquipmentSlots.Count != 0 || a.CardCountExpression is not null || a.UsageGroup != a.Id ||
            a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || a.ContinueAfterOwnerDeath ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Yixian requires its complete own-Play limited equipment obtain and native per-owner compensation.");
    }
}
