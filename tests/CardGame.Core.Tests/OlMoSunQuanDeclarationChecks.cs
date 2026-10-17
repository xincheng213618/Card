using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OlMoSunQuanDeclarationChecks
{
    private const string MdcQuanyu = "ol:mo-quanyu", MdcDriver = "fixture:msq-declaration-driver";
    private const string MdcGrant = "fixture:msq-declaration-grant", MdcWatch = "fixture:msq-declaration-watch";
    private const string MdcQuiet = "fixture:msq-declaration-quiet", MdcOwner = "fixture:msq-declaration-owner";
    private const string MdcMode = "identity:classic-mo-sun-quan-declaration";

    public static void MeteorPrepaidDeclarationKeepsInitialSingletonQuotaAndFalseAdmissionCold()
    {
        foreach (var regain in new[] { false, true })
        {
            var s = MdcScenario.Start(regain);
            s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Single(e => e.IssuerSeat == 0 && e.ParticipantSeat == 0)
                .Effect == MoQuanyuEffect.Liuxing, "The actual first-round private choice supplies Meteor; no injected policy supplies quota.");
            if (regain)
            {
                s.Use("lose-before"); s.Play();
                s.Need(!s.HasQuanyu && s.V().Hp == 12, "A real HP child removes conditional Quanyu before the still-ordinary legal declaration.");
            }
            else
            {
                var ordinary = s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
                s.Native(ordinary); s.Play();
                s.Need(s.E<CardUseFinishedEvent>().Count(e => e.CardId == ordinary.CardId && e.CardKind == CardKind.Slash &&
                    s.E<CardUseDeclaredEvent>().Any(d => d.ResolutionId == e.ResolutionId && d.SourceSeat == 0)) == 1,
                    "A real first Slash finishes and debits ordinary quota before Meteor authorizes the declaration.");
            }
            var action = s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]) &&
                a.ConversionSource is { SkillId: "classic:guhuo", BindingId: "declare-slash" });
            var material = action.CardId!.Value; s.RejectForgedSource(action); s.Native(action);
            s.Reach(() => s.Hold("paid"));
            var declaration = s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single();
            var original = declaration.Payment.MoQuanyuDeclarationQuota;
            var captured = s.E<MoQuanyuDeclarationQuotaCapturedEvent>().Single(e => e.DeclarationId == declaration.Id);
            var invoice = s.E<MoQuanyuDeclarationPaymentRecordedEvent>().Single(e => e.DeclarationId == declaration.Id);
            s.Need(original is not null && original == captured.Receipt && original.Eligible == !regain &&
                captured.OwnerFrameId == declaration.Id && original.DeclarationId == declaration.Id &&
                original.ActorSeat == 0 && original.ProviderSeat == 0 && original.TargetSeat == 1 &&
                original.CardId == material && original.From == CardLocation.Hand(0) &&
                original.DeclaredKind == CardKind.Slash && original.OutputKind == CardKind.Slash &&
                original.DeclarationSource == action.ConversionSource && declaration.Payment.Cost.CardId == material &&
                declaration.Payment.Cost.CardKind == CardKind.Slash && declaration.TargetSeats.SequenceEqual([1]) &&
                declaration.TargetSeats is IList { IsReadOnly: true } && !declaration.IsRevealed &&
                invoice.MovementSequence > original.SequenceBefore &&
                s.G.CardMovements.Count(m => m.Sequence == invoice.MovementSequence && m.CardId == material &&
                    m.CardKind == CardKind.Slash && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing &&
                    m.Reason.Value == "conversion.declaration.pay") == 1 &&
                !s.E<MoQuanyuDeclarationQuotaBoundEvent>().Any(e => e.DeclarationId == declaration.Id),
                "The real initial singleton declaration freezes true or false quota before its one original Hand payment, without an invented native Use.");
            s.Need(regain ? original!.Source is null && original.GameplayHash is null && original.ChoiceMask == 0 :
                original!.Source is { SkillId: MdcQuanyu, OwnerSeat: 0 } && original.GameplayHash is not null &&
                (original.ChoiceMask & (1 << ((int)MoQuanyuEffect.Liuxing - 1))) != 0,
                "An unavailable original source records explicit false; a genuine Meteor source keeps its precise immutable source and mask.");
            s.Private(); s.RejectPrompt(); var retired = s.P!; s.Cold(); s.Continue(); s.RejectOld(retired);
            s.Reach(() => s.Hold("changed"));
            s.Need(s.HasQuanyu == regain && s.V().Hp == (regain ? 13 : 12) &&
                s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single().Payment.MoQuanyuDeclarationQuota == original &&
                s.E<MoQuanyuDeclarationQuotaCapturedEvent>().Count(e => e.DeclarationId == declaration.Id) == 1 &&
                !s.E<MoQuanyuDeclarationQuotaBoundEvent>().Any(e => e.DeclarationId == declaration.Id),
                "Native payment children genuinely lose or reacquire Quanyu before dispatch, without replacing either the original true or false admission.");
            s.Private(); s.Cold(); s.Continue(); s.Play();
            var bound = s.E<MoQuanyuDeclarationQuotaBoundEvent>().Single(e => e.DeclarationId == declaration.Id);
            var declared = s.E<CardUseDeclaredEvent>().Single(e => e.ResolutionId == bound.UseId);
            s.Need(bound.Eligible == !regain && bound.ActorSeat == 0 && bound.TargetSeat == 1 && bound.OutputKind == CardKind.Slash &&
                declared.SourceSeat == 0 && declared.CardId == material && declared.CardKind == CardKind.Slash &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == bound.UseId && e.CardId == material && e.CardKind == CardKind.Slash) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                !s.G.ResolutionStack.Any(f => f.Id == declaration.Id || f.Id == bound.UseId),
                "One successful native declaration becomes one original singleton Use and finishes once after Cold, with one material payment and one retirement.");
            var quota = s.E<MoQuanyuQuotaAcceptedEvent>().Where(e => e.Receipt.UseId == bound.UseId).ToArray();
            s.Need(regain ? quota.Length == 0 : quota is [var q] && q.Receipt.ActorSeat == 0 &&
                q.Receipt.OriginalTargetSeat == 1 && q.Receipt.Source == original!.Source && q.Receipt.GameplayHash == original.GameplayHash &&
                q.Receipt.ChoiceMask == original.ChoiceMask && q.Receipt.Entered == original.Entered && q.Receipt.ActionId == bound.ActionId,
                "Only an original true declaration inherits Meteor into its exact native Use; later acquisition cannot upgrade a previously false admission.");
            s.Need(s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1])) == regain,
                "Ordinary Slash debit is retained: losing Meteor prevents a fresh additional Slash, while reacquisition authorizes future queries without changing the prior declaration.");
            s.Cold();
        }
    }

    private sealed class MdcScenario(bool regain, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V() => G.CreateSnapshot(0).Players[0];
        public bool HasQuanyu => V().Skills?.Any(s => s.ContentId == MdcQuanyu) == true;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool Hold(string id) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == MdcWatch &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        private PlayCardCommand Command(LegalAction a) => new(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId,
            a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources };
        public void Native(LegalAction a) => Accept(Command(a));
        public void Use(string id) => Accept(new UseProgramSkillCommand(0, MdcDriver, id, [], [], G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> match)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(match);
            Need(p.PlayerSeat == 0 && c is not null, "Only the real human chooses a currently published native option.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 150; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal before actual native boundary " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-choice"))
                    Answer(c => c.Parameters.GetValueOrDefault("effect") == "6");
                else if (p.Kind == DecisionKind.PlayCard) Accept(new EndPlayPhaseCommand(0, G.Revision, p.PromptId));
                else if (p.SkillPrompt?.SkillId == MdcWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass"))
                    Answer(c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                    Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected declaration boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded declaration boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null),
                "Only the payment observer's real chooser sees its suspended private menu.");
            foreach (var viewer in Enumerable.Range(0, 4))
            {
                var d = G.CreateSnapshot(viewer).CardDeclarations!.Single();
                Need(!d.IsRevealed && d.RevealedCard is null && (viewer == 0 ? d.OwnerCost is not null : d.OwnerCost is null),
                    "The new quota receipt never leaks the unrevealed physical declaration face to challengers or other viewers.");
            }
        }
        public void RejectForgedSource(LegalAction a)
        {
            var before = State(); var forged = Command(a) with { ConversionSource = a.ConversionSource! with { SkillInstanceId = "forged-meteor-declaration" } };
            Need(!G.Submit(forged).Accepted && before == State(), "A forged declaration instance cannot borrow Meteor or consume the original entity.");
        }
        public void RejectPrompt()
        {
            var p = P!; var before = State();
            Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && before == State() &&
                !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-meteor-declaration"), G.Revision)).Accepted && before == State(),
                "Foreign and unpublished payment-child input cannot change the prepaid right.");
        }
        public void RejectOld(PendingDecision old)
        { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && before == State(), "A retired cost prompt cannot repeat payment or change its source."); }
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves each viewer, original declaration permission, exact physical invoice and native challenge cursor.");
        }
        public void Accept(GameCommand command)
        {
            try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + ": " + r.Error?.Message); }
            catch (Exception e) { throw new InvalidOperationException("regain=" + regain + " " + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Diagnostic() => JsonSerializer.Serialize(new { Regain = regain, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Hp = V().Hp, HasQuanyu, Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(4) } : null,
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Captured = E<MoQuanyuDeclarationQuotaCapturedEvent>().TakeLast(2),
            Invoices = E<MoQuanyuDeclarationPaymentRecordedEvent>().TakeLast(2), Bound = E<MoQuanyuDeclarationQuotaBoundEvent>().TakeLast(2),
            Finished = E<CardUseFinishedEvent>().TakeLast(3) });
        public static MdcScenario Start(bool regain)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new MdcFixture(regain));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = MdcMode, UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new MdcScenario(regain, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, MdcOwner, g.Revision, s.P!.PromptId)); s.Play();
            s.Need(s.V().Hp == 13 && s.HasQuanyu, "The real identity role bonus makes the original conditional Quanyu source live."); return s;
        }
    }

    private sealed class MdcFixture(bool regain) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mo-sun-quan-declaration", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static void Production(IContentRegistryBuilder b, string bundle)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual embedded bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            }
            Production(b, "ol-mo-sun-quan"); Production(b, "classic-yu-ji");
            static JsonNode N(string s) => JsonNode.Parse(s)!;
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var effects = Choice("paid") + "," + (regain ? "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}") + "," + Choice("changed");
            var defs = new JsonArray(
                N("{\"id\":\"" + MdcDriver + "\",\"revision\":1,\"activations\":[{\"id\":\"lose-before\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]}"),
                N("{\"id\":\"" + MdcGrant + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-meteor\",\"skillId\":\"" + MdcQuanyu + "\",\"minimumHp\":13}]}"),
                N("{\"id\":\"" + MdcQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"),
                N("{\"id\":\"" + MdcWatch + "\",\"revision\":1,\"triggers\":[{\"id\":\"native-declaration-payment\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"conversion.declaration.pay\"],\"optional\":false,\"effects\":[" + effects + "]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实流星预付声明",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray())
                    .SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in fixture.Programs) b.AddSkill(new(id, id, "真实流星预付声明") { Program = p, ProgramPresentation = fixture.Presentations[id] });
            const string pick = "fixture:msq-declaration-own-pick";
            b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(MdcOwner, "真实流星声明本人", "supporter", pick, "wu", 12, [MdcDriver, MdcGrant, MdcWatch, "classic:guhuo"]));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:msq-declaration-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pp = "fixture:msq-declaration-peer-pick-" + i;
                b.AddSkill(new(pp, "其他角色", "固定真实候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pp, "shu", 12, [MdcQuiet]));
            }
            b.AddDeck(new("fixture:msq-declaration-deck", "原生固定小牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(MdcMode, "流星真实声明与失源", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:msq-declaration-deck", GeneralCandidateCount: 4, GeneralPoolIds: [MdcOwner, .. peers]));
        }
    }
}
