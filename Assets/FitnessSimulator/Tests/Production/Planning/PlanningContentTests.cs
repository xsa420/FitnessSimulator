using System;
using System.Linq;
using System.Collections.Generic;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Body;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Planning
{
    public sealed class PlanningContentTests
    {
        private static Dictionary<string,string> Documents() => ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
        private static IDisposable Replace<T>(string id,T value) where T:class
        {
            var documents=Documents(); documents[id]=JsonUtility.ToJson(value);
            return ContentRuntime.Push(ContentPackage.FromDocuments(documents));
        }
        [Test] public void InstalledDocumentsValidateAndDefensiveReadsDoNotChangeDefaults()
        {
            PlanningContent.ValidateContent(new[]{"early","morning","noon","afternoon","evening","late"});
            TemplateCatalog.ValidateContent(); CompetitionContent.ValidateContent();
            string anchor=PlanCatalog.CreateBenchPressSlice().Slots[0].AnchorId;
            var mutable=PlanningContent.Read(); mutable.Templates.Single(t=>t.Id==mutable.SliceTemplateId).Draft.Slots[0].AnchorId="invalid";
            Assert.That(PlanCatalog.CreateBenchPressSlice().Slots[0].AnchorId,Is.EqualTo(anchor));
        }
        [Test] public void ChangedTemplateRegionIsUsedByCompleteInitializationWithoutChangingExistingBody()
        {
            var before=TemplateCatalog.PrepareInitialization(TemplateCatalog.DefaultId,Array.Empty<string>(),"r","player").CreateBody();
            var d=TemplateContent.Read(); var initial=d.Initializations.Single(x=>x.Id==d.Templates.Single(t=>t.Id==d.DefaultId).InitializationId);
            string region=initial.Body.Regions[0].RegionId; long original=initial.Body.Regions[0].StrengthMicro.Value;
            initial.Body.Regions[0].StrengthMicro.Value=original+1000000;
            using(Replace("templates",d)) {
                TemplateCatalog.ValidateContent();
                var changed=TemplateCatalog.PrepareInitialization(TemplateCatalog.DefaultId,Array.Empty<string>(),"new","player").CreateBody();
                Assert.That(changed.Region(region).StrengthMicro,Is.EqualTo(original+1000000));
                Assert.That(before.Region(region).StrengthMicro,Is.EqualTo(original));
            }
        }
        [Test] public void AlteredPlanOnlyChangesNewDraftAndPackageScopeRestores()
        {
            var original=PlanCatalog.CreateBenchPressSlice(); string anchor=original.Slots[0].AnchorId;
            var d=PlanningContent.Read(); d.Templates.Single(t=>t.Id==d.SliceTemplateId).Draft.Slots[0].AnchorId="evening";
            using(Replace("plans",d)) {
                Assert.That(PlanCatalog.CreateBenchPressSlice().Slots[0].AnchorId,Is.EqualTo("evening"));
                Assert.That(original.Slots[0].AnchorId,Is.EqualTo(anchor));
            }
            Assert.That(PlanCatalog.CreateBenchPressSlice().Slots[0].AnchorId,Is.EqualTo(anchor));
        }
        [Test] public void TemplateMissingRegionAndUnknownGrantFactAreRejected()
        {
            var d=TemplateContent.Read(); d.Initializations[0].Body.Regions=d.Initializations[0].Body.Regions.Skip(1).ToArray();
            using(Replace("templates",d))Assert.Throws<ArgumentException>(()=>TemplateCatalog.ValidateContent());
            d=TemplateContent.Read(); d.Templates[1].Availability.Key="body.Strength";
            using(Replace("templates",d))Assert.Throws<ContentException>(()=>TemplateCatalog.ValidateContent());
        }
        [Test] public void ShortScheduleAndJudgeWeightsAreConsumedByDomainNotInjectedIntoState()
        {
            var d=CompetitionConfiguration.Read(); d.OpenDay=1; d.RegistrationCloseDay=4; d.ContestDay=5; d.CycleDays=5;
            for(int i=0;i<3;i++)d.RevealStages[i].Day=i+2;
            var player=BodyState.Create("player",Enumerable.Repeat(20000000L,21).ToArray(),20000000,50000000,0);
            var rival=BodyState.Create("rival",Enumerable.Repeat(18000000L,21).ToArray(),20000000,50000000,0);
            var ids=d.Contests[0].JudgeIds.Take(3).ToArray();
            long baseline=CompetitionScoring.Score(ContestKind.Bodybuilding,player,rival,ids).Participants[0].TotalScoreUnits4;
            var judge=d.Judges.Single(j=>j.Id==ids[0]); judge.WeightsPercent=new[]{100,0,0,0};
            using(Replace("competition",d)) {
                CompetitionContent.ValidateContent();
                var state=CompetitionRules.Create("short-calendar-fixture","player","rival");
                Assert.That(state.Current.ContestDay,Is.EqualTo(5));
                state=CompetitionRules.Open(state,1,new[]{0,0,0},new[]{0,0,0}).State;
                state=CompetitionRules.AdvanceDisclosure(state,2).State;
                Assert.That(state.Current.RevealedJudges,Is.EqualTo(1));
                var result=CompetitionScoring.Score(ContestKind.Bodybuilding,player,rival,ids);
                Assert.That(result.Participants[0].TotalScoreUnits4,Is.Not.EqualTo(baseline));
                Assert.That(result.Participants[0].Judges.All(j=>!string.IsNullOrWhiteSpace(j.CommentaryTextRef)),Is.True);
            }
        }
        [Test] public void CommentaryRuleAndInvalidJudgeWeightsUseConfiguration()
        {
            var d=CompetitionConfiguration.Read(); var body=BodyState.Create("p",Enumerable.Repeat(20000000L,21).ToArray(),20000000,50000000,0);
            var rival=body.Clone(); rival.ActorId="r"; var ids=d.Contests[0].JudgeIds.Take(3).ToArray();
            var first=d.Judges.Single(j=>j.Id==ids[0]); var profile=d.CommentaryProfiles.Single(p=>p.Id==first.CommentaryProfileId);
            profile.Rules=Array.Empty<CommentaryRuleDefinition>();
            using(Replace("competition",d))Assert.That(CompetitionScoring.Score(ContestKind.Bodybuilding,body,rival,ids).Participants[0].Judges[0].CommentaryTextRef,Is.EqualTo(profile.FallbackTextRef));
            d=CompetitionConfiguration.Read(); d.Judges[0].WeightsPercent[0]++;
            using(Replace("competition",d))Assert.Throws<ArgumentException>(()=>CompetitionContent.ValidateContent());
        }
    }
}
