using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouJiangWeiMobileChecks
{
    private const string Hero = "mou:jiang-wei-mobile", Quiet = "fixture:moujw-quiet", Faction = "fixture:moujw-faction";
    public static void RealRequestedSlashGiftChargeAwakeningAndBeifaCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V().Hp == 4 && s.V().MaxHp == 4 && s.V().Armor == 1 && s.Charge() == 4, "The formal mobile revision starts at 4/4 HP, one real armor and charge4/4.");
        s.Taunt(); s.Choose("target", 1); s.Choose("target", 3); s.Choose("confirm");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold(); s.Dodge(); s.Play();
        var first = s.E<MouJiangWeiSelectionClosedEvent>().Single(); var issued = s.E<MouJiangWeiSlashIssuedEvent>().Single().Origin;
        s.Need(first.Count == 2 && s.Charge() == 2 && s.E<MouJiangWeiGiftPaidEvent>() is [{ GiverSeat: 1 } gift] && gift.Payment.From == CardLocation.Hand(1) && gift.Payment.To == CardLocation.Hand(0) &&
            s.G.CardMovements.Count(m => m.CardId == gift.Payment.CardId && m.Sequence == gift.Payment.Sequence && m.Reason.Value == "skill-program.mou-mobile:tiaoxin.give") == 1 &&
            issued.ActorSeat == 3 && issued.ProviderSeat == 3 && issued.TargetSeat == 0 && s.E<MouJiangWeiSlashReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1,
            "The ally independently gives one actual Hand entity; the enemy independently pays and uses its native Slash. The human's real Dodge and Cold return the same ordered request once, then the selected-person count debits two charge.");
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mou-mobile:tiaoxin"), "The real first Play spends Tiaoxin's one native activation budget.");
        s.End(); s.Reach(() => s.G.State.TurnNumber >= 5 && s.IsPlay());
        s.Need(s.Charge() == 4 && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 0 && e.ActualCount > 0 && e.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Discard, ActorSeat: 0 }) && s.E<MouJiangWeiChargeChangedEvent>().Any(e => e.DiscardInvocationId > 0 && e.After > e.Before),
            "Native discard-phase invoices, carrying the phase frozen before their actual payment, replenish real charge up to four; normal use payments do not.");
        s.Taunt(); s.Choose("target", 1); s.Choose("target", 2); s.Choose("confirm");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash, SourceSeat: 2 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash" && c.Cards.Count == 1 && s.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Slash));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var suppliedUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.MouJiangWeiRequest is not null);
        s.Need(suppliedUse.Action is { ActorSeat: 2, ProviderSeat: 0, RequesterSeat: 2 } && suppliedUse.TargetSeats.SequenceEqual([0]), "The distant responder remains the native actor/requester; only the real human provider supplies its own material, and the invitation grants distance rather than changing attack range.");
        s.Dodge(); s.Play();
        var supplied = s.E<MouJiangWeiSlashIssuedEvent>().Last().Origin;
        s.Need(s.E<MouJiangWeiSelectionClosedEvent>().Sum(e => e.Count) == 4 && s.E<MouJiangWeiTargetAcceptedEvent>().Count(e => e.TargetSeat == 1) == 2 &&
            s.E<MouJiangWeiFactionRequestedEvent>() is [{ ActorSeat: 2, TargetSeat: 0 }] && s.E<MouJiangWeiFactionProvidedEvent>() is [{ ActorSeat: 2, ProviderSeat: 0, MaterialCount: 1 }] && supplied.ActorSeat == 2 && supplied.ProviderSeat == 0 &&
            s.E<FactionSlashResolvedEvent>().Any(e => e.OwnerSeat == 2 && e.Succeeded && e.ProviderSeat == 0) && s.E<MouJiangWeiSlashReturnedEvent>().Count(e => e.UseId == supplied.UseId) == 1,
            "A repeated ally counts again toward Zhiji. The distant empty-handed enemy independently invokes its real faction ability. The human answers only its own published supplier choice; actor2/provider0/requester2 are preserved and the actual Slash returns once without requiring the responder to supply its own card.");
        s.End(); s.Reach(() => s.Branch("target") && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().MouJiangWei?.Stage == MouJiangWeiStage.MarkTargets);
        s.Need(s.V().MaxHp == 3 && s.V().Hp == 3 && s.E<MouJiangWeiAwakenedEvent>().Length == 1 && s.E<MaximumHpChangedEvent>().Count(e => e.SkillId == "mou-mobile:zhiji" && e.Delta == -1) == 1,
            "The real next Preparation awakens once, actually decreases maximum HP, and drains the native state change before choosing marks.");
        s.Choose("target", 0); s.Choose("target", 2); s.Choose("confirm"); s.Play();
        s.Need(s.Beifa(0) == 1 && s.Beifa(2) == 1 && s.V().Hand.Any(c => c.Kind == CardKind.Slash) && !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Any number includes the issuer itself: public Beifa marks are issued to zero and two, and a self-marked issuer cannot select another character with its real Hand Slash.");
        var before = s.Charge(); s.Taunt(); s.Choose("confirm"); s.Play();
        s.Need(s.Charge() == before && s.E<MouJiangWeiSelectionClosedEvent>().Last().Count == 0, "Selecting zero is a genuine once-per-Play activation and spends zero charge.");
        s.End(); s.Reach(() => s.G.State.TurnNumber >= 13 && s.IsPlay());
        s.Need(s.Beifa(0) == 0 && s.Beifa(2) == 0 && s.E<MouJiangWeiBeifaExpiredEvent>().Length == 2 && s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) && s.E<MouJiangWeiAwakenedEvent>().Length == 1,
            "Both paid marks expire at their issuer's next actual TurnStarted, restoring actual target legality without awakening again.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i = 0) => G.CreateSnapshot(i).Players[i];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Charge() => V().Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouJiangWeiCharge)?.Count ?? 0;
        public int Beifa(int owner) => V(owner).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouJiangWeiBeifa)?.Count ?? 0;
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public bool Branch(string branch) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-jiang-wei" && c.Parameters.GetValueOrDefault("branch") == branch);
        public void Play() => Reach(IsPlay);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Taunt() => Accept(new UseProgramSkillCommand(0, "mou-mobile:tiaoxin", "taunt", [], [], G.Revision, P!.PromptId));
        public void Choose(string branch, int? target = null) => Answer(c => c.Parameters.GetValueOrDefault("branch") == branch && (target is null || c.Targets.SequenceEqual([target.Value])));
        public void Dodge() { var ids = V().Hand.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).ToHashSet(); Answer(c => c.Cards.Count == 1 && ids.Contains(c.Cards[0])); }
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published human-owned choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand c) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 260; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected actual cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind == DecisionKind.RespondDodge) Dodge();
                else throw new InvalidOperationException("Unexpected real human prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real Jiang Wei main flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The original request, chosen-person order, reservation and native physical Slash survive Cold without paying again."); }
        public void Need(bool b, string text) { if (!b) throw new InvalidOperationException(text + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Owner = V(), Actions = G.GetHumanLegalActions(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouJiangWei", StringComparison.Ordinal) || e.Payload is NativeLogicalDiscardInvocationRecordedEvent or CardUseDeclaredEvent or TurnStartedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "moujw-real", Hero, 4, 0, 0, 0, 0, [], Enumerable.Range(0, 8).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i + 1)).ToArray(), [],
                [new("fixture:moujw-enemy-a", Hp: 12) { StartingHand = [] }, new("fixture:moujw-enemy-b", Hp: 12) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7)] }], [new("fixture:moujw-ally", Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7)] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry); var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-jiang-wei-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:moujw-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var defs = new object[] {
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Faction, revision = 1, modifiers = new[] { new { id = "no-normal-draw", query = "drawCount", operation = "set", value = 0, priority = 0 } }, cardPolicies = new[] { new { id = "allied-slash", kind = "factionResponseRequest", requiredCardKinds = new[] { "slash" }, factionId = "shu" } }, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "原生跳过同行出牌阶段" }, [Faction] = new { name = "真实势力供杀", description = "由同阵营角色实际提供杀；本人正常摸牌数为零并跳过出牌" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var id in new[] { "fixture:moujw-ally", "fixture:moujw-enemy-a", "fixture:moujw-enemy-b" }) b.AddGeneral(new(id, "真实同行", "", id.EndsWith("enemy-a", StringComparison.Ordinal) ? Faction : Quiet, id.EndsWith("enemy-b", StringComparison.Ordinal) ? "wei" : "shu", 12));
            b.AddDeck(new("fixture:moujw-deck", "固定真实杀牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("fixture:moujw-card-Slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋姜维真实主流程", 4, 4, new Dictionary<string, int>(), "fixture:moujw-deck", GeneralPoolIds: [Hero, "fixture:moujw-ally", "fixture:moujw-enemy-a", "fixture:moujw-enemy-b"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
