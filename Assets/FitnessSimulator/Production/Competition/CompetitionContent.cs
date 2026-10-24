using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Competition
{
    public enum ContestKind { Bodybuilding, ThinBuild }
    public sealed class JudgeDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ContestKind Kind { get; }
        public bool Balanced { get; }
        public IReadOnlyList<string> PreferredRegions { get; }
        public IReadOnlyList<int> WeightsPercent { get; }
        public int FatLower { get; }
        public int FatUpper { get; }
        public string CommentaryProfileId { get; }
        internal JudgeDefinition(JudgeContentDefinition d)
        {
            Id=d.Id; Name=d.Name; Description=d.Description; Kind=d.Kind; Balanced=d.Balanced;
            PreferredRegions=Array.AsReadOnly(Balanced?RegionIds.All.ToArray():(string[])d.PreferredRegions.Clone());
            WeightsPercent=Array.AsReadOnly((int[])d.WeightsPercent.Clone()); FatLower=d.FatLower; FatUpper=d.FatUpper;
            CommentaryProfileId=d.CommentaryProfileId;
        }
    }
    public static class CompetitionContent
    {
        public static string Version => CompetitionConfiguration.Read().Version;
        public static string ScoringVersion => CompetitionConfiguration.Read().ScoringVersion+"/"+BodyMetrics.Version+"/"+NumericKernel.SqrtVersion;
        public static string FreezePhase => CompetitionConfiguration.Read().FreezePhase;
        public static string ClosePhase => CompetitionConfiguration.Read().ClosePhase;
        public static int CycleDays => CompetitionConfiguration.Read().CycleDays;
        public static int OpenDay => CompetitionConfiguration.Read().OpenDay;
        public static int RegistrationCloseDay => CompetitionConfiguration.Read().RegistrationCloseDay;
        public static int ContestDay => CompetitionConfiguration.Read().ContestDay;
        public static int[] DisclosureDays => CompetitionConfiguration.Read().RevealStages.Select(s=>s.Day).ToArray();
        public static int DisclosureCount(int relativeDay) => CompetitionConfiguration.Read().RevealStages.Where(s=>relativeDay>=s.Day).Select(s=>s.Count).DefaultIfEmpty(0).Max();
        public static bool Discloses(string field,int level) => CompetitionConfiguration.Read().RevealStages.Any(s=>s.Count<=level&&s.Fields.Contains(field));
        public static IReadOnlyList<JudgeDefinition> Judges => Array.AsReadOnly(CompetitionConfiguration.Read().Judges.Select(j=>new JudgeDefinition(j)).ToArray());
        public static JudgeDefinition Judge(string id) => Judges.Single(x=>x.Id==id);
        public static ContestDefinition Contest(ContestKind kind) => CompetitionConfiguration.Read().Contests.Single(c=>c.Kind==kind);
        public static string Anchor(ContestKind kind) { CheckKind(kind); return Contest(kind).AnchorId; }
        public static void ValidateContent() => CompetitionConfiguration.ValidateContent();
        public static void CheckKind(ContestKind kind)
        { if(!Enum.IsDefined(typeof(ContestKind),kind))throw new ArgumentOutOfRangeException(nameof(kind)); }
        public static int Offset(int ordinal)
        { if(ordinal<1)throw new ArgumentOutOfRangeException(nameof(ordinal)); return checked((ordinal-1)*CycleDays); }
        public static string EditionId(string runId,int ordinal) => runId+"/"+CompetitionConfiguration.Read().ScheduleId+"/"+ordinal;
    }
}
