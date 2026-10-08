namespace CardGame.Core;

/// <summary>
/// One character in one match. General definitions supply initial content;
/// mutable attributes and skill grants belong to this instance, not the engine.
/// </summary>
public sealed class CharacterState
{
    public const string PrimarySkillSource = "template:primary";
    public const string SecondarySkillSource = "template:secondary";
    private GeneralDefinition _general = null!;
    private GeneralDefinition? _secondaryGeneral;

    public required int Seat { get; init; }
    public required string Name { get; init; }
    public required bool IsHuman { get; init; }
    public required Role Role { get; init; }
    public string? TeamId { get; init; }
    public bool TeamRevealed { get; set; }
    public string? NationalFactionId { get; init; }
    public string? ChosenFactionId { get; set; }
    public bool FactionRevealed { get; set; }
    public required bool RoleRevealed { get; set; }
    public required GeneralDefinition General
    {
        get => _general;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            BindTemplateSkills(PrimarySkillSource, value);
            _general = value;
        }
    }
    public required bool GeneralSelected { get; set; }
    public required bool GeneralRevealed { get; set; }
    public GeneralDefinition? SecondaryGeneral
    {
        get => _secondaryGeneral;
        set
        {
            BindTemplateSkills(SecondarySkillSource, value);
            _secondaryGeneral = value;
        }
    }
    public bool SecondaryGeneralSelected { get; set; }
    public bool SecondaryGeneralRevealed { get; set; }
    public required int MaxHp { get; set; }
    public required int Hp { get; set; }
    public GeneralGender? GenderOverride { get; set; }
    internal Func<CharacterState,GeneralGender?>? GeneralLibraryGenderQuery {get;set;}
    public GeneralGender Gender => GeneralLibraryGenderQuery?.Invoke(this) ?? GenderOverride ?? General.Gender;
    public bool IsAlive { get; set; } = true;
    public bool HasAlcoholEffect { get; set; }
    public bool UsedPlayPhaseAlcoholThisTurn { get; set; }
    public bool IsChained { get; set; }
    public bool EquipmentAreaAbolished { get; set; }
    public Dictionary<EquipmentSlot, int> EquipmentSlotCapacities { get; } = [];
    public int EquipmentSlotCapacity(EquipmentSlot slot) => EquipmentAreaAbolished ? 0 : EquipmentSlotCapacities.GetValueOrDefault(slot, 1);
    public bool JudgmentAreaAbolished { get; set; }
    public Dictionary<PlayerMarkerKind, int> Markers { get; } = [];
    public Dictionary<(PlayerMarkerKind Marker, int SkillOwnerSeat), int> MarkerSourceCounts { get; } = [];
    public List<OneUseDamageShield> OneUseDamageShields { get; } = [];
    public CharacterSkillSet SkillGrants { get; } = new();
    public IReadOnlyList<string> AcquiredSkillIds => SkillGrants.Grants
        .Where(grant => grant.IsEnabled && grant.SourceId.StartsWith("acquired:", StringComparison.Ordinal))
        .Select(grant => grant.SkillId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> TurnGrantedSkillIds => SkillGrants.Grants
        .Where(grant => grant.IsEnabled && grant.SourceId.StartsWith("turn:", StringComparison.Ordinal))
        .Select(grant => grant.SkillId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> PhaseGrantedSkillIds => SkillGrants.Grants
        .Where(grant => grant.IsEnabled && grant.PhaseExpiry is not null)
        .Select(grant => grant.SkillId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public bool IsFaceDown { get; set; }

    private void BindTemplateSkills(string sourceId, GeneralDefinition? template)
    {
        var ids = (template?.Skills ?? []).Select(skill => skill.ContentId).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        // Validate the complete replacement before removing any current grants.
        foreach (var id in ids)
        {
            _ = new ContentId(id);
            var existing = SkillGrants.Grants.SingleOrDefault(grant => grant.GrantId == $"{sourceId}:{id}");
            if (existing is not null && (existing.SkillId != id || existing.SourceId != sourceId))
                throw new InvalidOperationException($"Default skill grant '{sourceId}:{id}' has a conflicting owner.");
        }
        // A real template replacement clears stale qualification even when
        // the replacement happens to retain the same skill/grant instance.
        foreach (var grant in SkillGrants.Grants.Where(grant => grant.SourceId == sourceId &&
                     grant.PrintedLordQualification is { } q && q.GeneralId != template?.Id))
            SkillGrants.SetPrintedLordQualification(grant.GrantId, null);
        foreach (var grant in SkillGrants.Grants.Where(grant => grant.SourceId == sourceId && !ids.Contains(grant.SkillId)))
            SkillGrants.RemoveGrant(grant.GrantId);
        foreach (var id in ids)
        {
            var grantId = $"{sourceId}:{id}";
            if (SkillGrants.Grants.Any(grant => grant.GrantId == grantId)) continue;
            SkillGrants.Grant(new SkillGrant(grantId, id, grantId, sourceId));
        }
    }
}
