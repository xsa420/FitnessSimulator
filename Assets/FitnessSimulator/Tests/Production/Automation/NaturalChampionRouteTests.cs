using FitnessSimulator.Automation;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using NUnit.Framework;
using UnityEngine;
namespace FitnessSimulator.Game.Tests.Automation {
// Candidate derived from CompetitionAndStartSpec 8.1; no promise seed 1 wins.
public sealed class NaturalChampionRouteTests {
 [Serializable] private sealed class WorldStamp { public int Day;public string Phase,BarrierId; }
 private static RootEnvelope ObserveFreeze(GameSession s,RootEnvelope ready,ref bool verified){
  var root=s.InspectSnapshot();if(root==null)return ready;var sf=root.FindFragment("session","player");if(sf==null)return ready;
  var state=JsonUtility.FromJson<SessionState>(sf.Json);if(state.Day!=42)return ready;
  var c=JsonUtility.FromJson<CompetitionState>(root.FindFragment("competition","player").Json);
  var wf=root.FindFragment("world","shared");var world=wf==null?null:JsonUtility.FromJson<WorldStamp>(wf.Json);
  if(c.Current.FrozenActors.Length==0&&world!=null&&world.Day==42&&world.Phase=="DayOpenReady"){
   foreach(string actor in new[]{"player","rival","beginner","veteran","coach"}){var actorState=JsonUtility.FromJson<SessionState>(root.FindFragment("session",actor).Json);Assert.That(actorState.Day,Is.EqualTo(42));Assert.That(actorState.Phase,Is.EqualTo("DayOpenReady"));}
   return JsonUtility.FromJson<RootEnvelope>(JsonUtility.ToJson(root));
  }
  if(!verified&&c.Current.FrozenActors.Length>0){Assert.That(ready,Is.Not.Null,"Missing observed pre-freeze committed barrier");Assert.That(c.Current.BarrierId,Is.EqualTo(JsonUtility.FromJson<WorldStamp>(ready.FindFragment("world","shared").Json).BarrierId));Assert.That(c.Current.FrozenActors.Select(x=>x.ActorId).OrderBy(x=>x).ToArray(),Is.EqualTo(new[]{"player","rival"}));foreach(var actor in c.Current.FrozenActors){var body=JsonUtility.FromJson<FitnessSimulator.Game.Body.BodyState>(ready.FindFragment("body",actor.ActorId).Json);Assert.That(actor.Day,Is.EqualTo(42));Assert.That(actor.Phase,Is.EqualTo("DayOpenReady"));Assert.That(actor.SourceCommitId,Is.EqualTo(ready.LastCommitId));Assert.That(actor.ActorRevision,Is.EqualTo(body.Revision));Assert.That(actor.SourceKind,Is.EqualTo(actor.ActorId=="player"?CompetitionSourceKind.PlayerProduction:CompetitionSourceKind.NpcProduction));Assert.That(root.FindFragment("body",actor.ActorId).Json,Is.EqualTo(ready.FindFragment("body",actor.ActorId).Json));Assert.That(JsonUtility.ToJson(actor.Body),Is.EqualTo(JsonUtility.ToJson(body)));}verified=true;}
  return ready;
 }
 // Independent audit fraction, not the production numeric implementation.
 private readonly struct Fraction:IComparable<Fraction> {
  public readonly System.Numerics.BigInteger N,D;
  public Fraction(System.Numerics.BigInteger n,System.Numerics.BigInteger d){if(d.IsZero)throw new DivideByZeroException();if(d.Sign<0){n=-n;d=-d;}var gcd=System.Numerics.BigInteger.GreatestCommonDivisor(System.Numerics.BigInteger.Abs(n),d);N=n/gcd;D=d/gcd;}
  public static implicit operator Fraction(long n)=>new Fraction(n,1);
  public static Fraction operator +(Fraction a,Fraction b)=>new Fraction(a.N*b.D+b.N*a.D,a.D*b.D);
  public static Fraction operator -(Fraction a,Fraction b)=>new Fraction(a.N*b.D-b.N*a.D,a.D*b.D);
  public static Fraction operator *(Fraction a,Fraction b)=>new Fraction(a.N*b.N,a.D*b.D);
  public static Fraction operator /(Fraction a,Fraction b)=>new Fraction(a.N*b.D,a.D*b.N);
  public int CompareTo(Fraction b)=>(N*b.D).CompareTo(b.N*D);
  public long Rounded(){var abs=System.Numerics.BigInteger.Abs(N);var whole=System.Numerics.BigInteger.DivRem(abs,D,out var remainder);if(remainder*2>=D)whole++;return checked((long)(N.Sign<0?-whole:whole));}
 }
 private static Fraction Clamp(Fraction x)=>x.CompareTo(0)<0?(Fraction)0:x.CompareTo(1)>0?(Fraction)1:x;
 private static long Micro(Fraction x)=>(x*1000000).Rounded();
 private static string FragmentKey(VersionedFragment f)=>f.Owner.Length+":"+f.Owner+f.Scope.Length+":"+f.Scope;
 private static Fraction ThinRegion(long strength){Fraction x=new Fraction(strength,1000000);return x.CompareTo(18)<0?x/18:x.CompareTo(26)<=0?(Fraction)1:x.CompareTo(48)<0?(48-x)/22:(Fraction)0;}
 private static long IndependentThinIndex(FitnessSimulator.Game.Body.BodyState body){
  System.Numerics.BigInteger sum=0,squares=0;Fraction adaptation=0;foreach(var r in body.Regions){var x=new System.Numerics.BigInteger(r.StrengthMicro);sum+=x;squares+=x*x;adaptation+=ThinRegion(r.StrengthMicro);}
  var numerator=(21*squares-sum*sum)*System.Numerics.BigInteger.Pow(10,12);var denominator=new System.Numerics.BigInteger(441);
  System.Numerics.BigInteger low=0,high=System.Numerics.BigInteger.Pow(10,15);while(high-low>1){var middle=(low+high)/2;if(middle*middle*denominator<=numerator)low=middle;else high=middle;}
  if(4*numerator>=denominator*(2*low+1)*(2*low+1))low++;
  Fraction std=new Fraction(low,System.Numerics.BigInteger.Pow(10,12));Fraction e=1-Clamp(std/14);return Micro(adaptation/21*(new Fraction(4,5)+e/5));
 }
 private static int CompareKeys(long[] a,long[] b){for(int i=0;i<a.Length;i++){int c=a[i].CompareTo(b[i]);if(c!=0)return c;}return 0;}
 private static void AuditMathSelfCheck(){
  Assert.That(new Fraction(1,2).Rounded(),Is.EqualTo(1));Assert.That(new Fraction(499999,1000000).Rounded(),Is.EqualTo(0));Assert.That(new Fraction(-1,2).Rounded(),Is.EqualTo(-1));
  Assert.That(Micro(new Fraction(1,2000000)),Is.EqualTo(1));Assert.That((new Fraction(1,3)+new Fraction(1,6)).Rounded(),Is.EqualTo(1));
  Assert.That((new Fraction(1,20000)*10000).Rounded(),Is.EqualTo(1));Assert.That(new Fraction(150,300).Rounded(),Is.EqualTo(1));
  Assert.That(CompareKeys(new long[]{100,200,300},new long[]{100,200,300}),Is.Zero);Assert.That(CompareKeys(new long[]{100,200,301},new long[]{100,200,300}),Is.GreaterThan(0));
 }
 internal static void AuditScores(GameSession s,ContestKind targetKind,bool requireChampion=true) {
  var c=JsonUtility.FromJson<CompetitionState>(s.InspectSnapshot().FindFragment("competition","player").Json);
  var edition=c.Current;var contest=edition.Contest(targetKind);Assert.That(edition.RevealedJudges,Is.EqualTo(3));Assert.That(contest.Results.Length,Is.EqualTo(1));
  Assert.That(c.ScoringVersion,Is.EqualTo(CompetitionContent.ScoringVersion));Assert.That(c.ContentVersion,Is.EqualTo(CompetitionContent.Version));var result=contest.Results[0].Score;Assert.That(result.ScoringVersion,Is.EqualTo(c.ScoringVersion));
  var keys=new System.Collections.Generic.Dictionary<string,long[]>();
  foreach(var participant in result.Participants){var body=edition.FrozenActors.Single(a=>a.ActorId==participant.ActorId).Body;
   Fraction m=Clamp(new Fraction(body.Regions.Sum(r=>r.StrengthMicro),2100000000)),u=Clamp(new Fraction(body.Regions.Min(r=>r.StrengthMicro),100000000)),b=new Fraction(body.BodyFatMicro,1000000),charm=Clamp(new Fraction(body.BaseCharmMicro,100000000));
   long lMicro=IndependentThinIndex(body);Fraction l=new Fraction(lMicro,1000000),totalT=0;
   Assert.That(participant.Judges.Length,Is.EqualTo(3));Assert.That(participant.Judges.Select(j=>j.JudgeId).Distinct().Count(),Is.EqualTo(3));Assert.That(participant.Judges.Select(j=>j.JudgeId).OrderBy(id=>id).ToArray(),Is.EqualTo(contest.JudgeIds.OrderBy(id=>id).ToArray()));
   long total=0;foreach(var score in participant.Judges){var j=CompetitionContent.Judge(score.JudgeId);Fraction t=Clamp(b.CompareTo(j.FatLower)<0?(b-5)/(j.FatLower-5):b.CompareTo(j.FatUpper)<=0?(Fraction)1:(50-b)/(50-j.FatUpper));totalT+=t;Fraction[] parts;
    if(targetKind==ContestKind.Bodybuilding){Fraction g=j.Balanced?m:Clamp(new Fraction(j.PreferredRegions.Sum(id=>body.Region(id).StrengthMicro),j.PreferredRegions.Count*100000000L));parts=new[]{m,u,g,t};}
    else {Fraction h=l;if(!j.Balanced){h=0;foreach(string id in j.PreferredRegions)h+=ThinRegion(body.Region(id).StrengthMicro);h/=j.PreferredRegions.Count;}parts=new[]{l,h,charm,t};}
    Fraction points=0;for(int i=0;i<4;i++)points+=parts[i]*j.WeightsPercent[i];long units=(points*10000).Rounded();Assert.That(score.ScoreUnits4,Is.EqualTo(units));Assert.That(score.ComponentMicro,Is.EqualTo(parts.Select(Micro).ToArray()));total+=units;
   }
   Assert.That(participant.TotalScoreUnits4,Is.EqualTo(total));Assert.That(participant.DisplayMeanCents,Is.EqualTo(new Fraction(total,300).Rounded()));
   long[] ties=targetKind==ContestKind.Bodybuilding?new[]{Micro(u),Micro(m)}:new[]{lMicro,Micro(totalT/3),Micro(charm)};Assert.That(participant.TieKeysMicro,Is.EqualTo(ties));keys.Add(participant.ActorId,new[]{total}.Concat(ties).ToArray());
  }
  var highest=keys.Values.Aggregate((a,b)=>CompareKeys(a,b)>=0?a:b);
  foreach(var participant in result.Participants)Assert.That(participant.Rank,Is.EqualTo(CompareKeys(keys[participant.ActorId],highest)==0?1:2));
  Assert.That(result.SharedChampionship,Is.EqualTo(keys.Values.All(k=>CompareKeys(k,highest)==0)));
  if(requireChampion)Assert.That(CompareKeys(keys[c.PlayerId],highest),Is.EqualTo(0),"Independent score/tie ranking did not win");
  if(requireChampion)Assert.That(result.Participants.Single(x=>x.ActorId==c.PlayerId).Rank,Is.EqualTo(1),"Natural route did not win");
 }
 [Test,Timeout(7260000)] public void SeedOneNaturalBodybuildingCandidate()=>RunCandidate(ContestKind.Bodybuilding);
 [Test,Explicit("Select thin-build route explicitly"),Timeout(7260000)] public void SeedOneNaturalThinBuildCandidate()=>RunCandidate(ContestKind.ThinBuild);
 private void RunCandidate(ContestKind targetKind) {
  AuditMathSelfCheck();
  bool longRun=Environment.GetEnvironmentVariable("FITNESS_CHAMPION_LONG")=="1";
  int seconds=longRun?int.Parse(Environment.GetEnvironmentVariable("FITNESS_CHAMPION_SECONDS")??throw new InvalidOperationException("Long route needs explicit measured budget")):600;
  if(seconds<1||seconds>7200)throw new ArgumentOutOfRangeException("FITNESS_CHAMPION_SECONDS","Must fit declared framework timeout");
  string dir=Path.Combine(AutomationEvidence.OutputRoot,"Fitness-Natural-Champion",Guid.NewGuid().ToString("N"));AutomationEvidence.RegisterDirectory(dir);
  var s=AutomationEvidenceSession.OpenAutomation(Path.Combine(dir,"save"),1);var watch=Stopwatch.StartNew();int commands=0,day=1,inspectedDay=0;bool enrolled=false,rested=false,firstSet=true,hadSet=false;string exit="BudgetExhausted",exception="",oldResults=null;bool continued=false,d43=false;int inputCommands=0,inputClockCalls=0;var exercises=new System.Collections.Generic.HashSet<string>();int manual=0,automatic=0,challenges=0;RootEnvelope freezeReady=null;bool freezeVerified=false;
  using(var trace=new EvidenceTrace(Path.Combine(dir,"trace.jsonl"))) {
   Action<FrontendOption> send=o=>{var v=s.ReadView();var a=new FrontendAction{OperationId="champion/"+(++commands),RunId=v.RunId,ExpectedRevision=v.Revision,ActionId=o.Id,StringValue=o.StringValue,IntValue=o.IntValue};trace.WriteLine(JsonUtility.ToJson(v));trace.WriteLine(JsonUtility.ToJson(a));var reply=AutomationEvidenceSession.Execute(s,a);trace.WriteLine(JsonUtility.ToJson(reply));Assert.That(reply.Accepted,Is.True,reply.Message);if(o.Id=="ending-continue"){string committed=JsonUtility.ToJson(s.InspectSnapshot());Assert.That(AutomationEvidenceSession.Execute(s,a).Accepted,Is.True);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(committed));}};
   try {
    for(int step=0;step<500000 && watch.Elapsed.TotalSeconds<seconds;step++) {
     freezeReady=ObserveFreeze(s,freezeReady,ref freezeVerified);var v=s.ReadView();trace.WriteLine(JsonUtility.ToJson(v));Assert.That(v.ScreenId,Is.Not.EqualTo("error"),v.Body);
     var match=Regex.Match(v.Status??"",@"第\s*(\d+)\s*日");if(match.Success)day=int.Parse(match.Groups[1].Value);Assert.That(day,Is.LessThanOrEqualTo(43),"CoverageMissing: no champion before D43");
     if(continued && day==43){
      var competition=JsonUtility.FromJson<CompetitionState>(s.InspectSnapshot().FindFragment("competition","player").Json);
      if(competition.Current.Ordinal==2){
       var previous=JsonUtility.FromJson<CompetitionEdition>(oldResults);var retained=competition.Editions.Single(e=>e.Ordinal==1);
       foreach(var contest in previous.Contests){var savedResults=retained.Contest(contest.Kind).Results;Assert.That(savedResults.Length,Is.EqualTo(contest.Results.Length));for(int i=0;i<savedResults.Length;i++)Assert.That(JsonUtility.ToJson(savedResults[i]),Is.EqualTo(JsonUtility.ToJson(contest.Results[i])));}
       Assert.That(competition.Current.ContestDay,Is.EqualTo(84));Assert.That(competition.Current.Opened,Is.False);Assert.That(competition.Current.ActualAttendanceCount,Is.Zero);Assert.That(competition.Current.Facts,Is.Empty);Assert.That(competition.Current.FrozenActors,Is.Empty);Assert.That(competition.Current.RegistrationClosed,Is.False);
       Assert.That(competition.Current.Contests.All(c=>!c.Enrolled&&c.JudgeIds.Length==0&&c.Results.Length==0),Is.True);
       Assert.That(freezeVerified,Is.True);d43=true;exit="NaturalChampionD43";break;
      }
     }
     if(v.TrainingInput!=null){exercises.Add(v.TrainingInput.ExerciseId);Assert.That(PublicEightInputDriver.DriveActiveSet(s,v.TrainingInput.ExerciseId,line=>{if(watch.Elapsed.TotalSeconds>=seconds)throw new InvalidOperationException("GlobalWallBudgetExceeded");if(line.StartsWith("advance-ms="))inputClockCalls++;if(line.Contains("\"ActionId\":\"training-input\""))inputCommands++;trace.WriteLine(line);}),Is.EqualTo("AwaitProductionResult"));continue;}
     var opts=v.Actions.Where(a=>a.Enabled).ToArray();FrontendOption o=null;
     if(v.ScreenId.StartsWith("summary/")){firstSet=true;hadSet=false;}
     if(v.ScreenId=="menu")o=opts.FirstOrDefault(a=>a.Id=="new-game");
     if(v.ScreenId=="InitialPlan"){o=opts.First(a=>a.Id=="save-plan");var plan=PlanCatalog.CreateDefault(PlanMode.NaturalWeek);foreach(var slot in plan.Slots)if(!slot.IsRest)slot.Volume=TrainingVolume.High;o.StringValue=JsonUtility.ToJson(plan);}
     if(!enrolled&&day>=8&&inspectedDay!=day&&opts.Any(a=>a.Id=="competition-menu")){inspectedDay=day;o=opts.First(a=>a.Id=="competition-menu");}
     if(v.ScreenId=="competition"){o=opts.FirstOrDefault(a=>a.Id=="competition-enroll"&&a.IntValue==(int)targetKind);if(o==null)o=opts.FirstOrDefault(a=>a.Id=="competition-attend"&&a.IntValue==(int)targetKind)??opts.FirstOrDefault(a=>a.Id=="competition-close");}
     if(opts.Any(a=>a.Id=="begin-set")) {
      if(hadSet&&!rested){o=opts.FirstOrDefault(a=>a.Id=="rest"&&a.IntValue==180);Assert.That(o,Is.Not.Null);send(o);rested=true;continue;}
      firstSet=Regex.IsMatch(v.Body??"",@"第\s*1\s*组");
      o=targetKind==ContestKind.Bodybuilding&&firstSet?opts.FirstOrDefault(a=>a.Id=="begin-set"&&a.IntValue==1):null;
      o=o??opts.FirstOrDefault(a=>a.Id=="begin-set"&&a.StringValue=="auto")??opts.First(a=>a.Id=="begin-set"&&a.StringValue=="manual"&&a.IntValue==0);if(o.StringValue=="auto")automatic++;else manual++;if(o.IntValue==1)challenges++;firstSet=false;hadSet=true;rested=false;
     }
     if(o==null&&opts.Any(a=>a.Id=="choose-entry"))o=opts.FirstOrDefault(a=>a.Id=="choose-entry"&&a.StringValue=="training-choice")??opts.First(a=>a.Id=="choose-entry");
     if(o==null&&opts.Any(a=>a.Id=="meal")){o=opts.FirstOrDefault(a=>a.Id=="meal"&&new[]{"porridge_egg","home_rice","home_noodles"}.Contains(a.StringValue));Assert.That(o,Is.Not.Null,"CoverageMissing: normal meal unavailable");}
     if(v.ScreenId=="ending-menu"&&!opts.Any(a=>a.Id=="ending-continue")){exit="ReachedContestNonChampion";break;}
     if(opts.Any(a=>a.Id=="ending-continue")) {
      var p=JsonUtility.FromJson<ProgressionRun>(s.InspectSnapshot().FindFragment("progression","player").Json);Assert.That(p.TerminalLock,Is.False,"Natural route lost");Assert.That(p.Active.EndingId,Is.EqualTo(targetKind==ContestKind.Bodybuilding?"bodybuilding_champion":"lean_champion"));Assert.That(p.TriggeredEndingIds.Distinct().Count(),Is.EqualTo(p.TriggeredEndingIds.Count));
      AuditScores(s,targetKind);var grants=JsonUtility.FromJson<ProgressionPermanent>(s.InspectSnapshot().PermanentFragments.Single(f=>f.Owner=="progression"&&f.Scope=="permanent").Json);Assert.That(grants.TemplateGrants.Count(g=>g.AchievementId==p.Active.AchievementId&&g.TemplateId==(targetKind==ContestKind.Bodybuilding?"strength_foundation":"lean_social")),Is.EqualTo(1));var editionBefore=JsonUtility.FromJson<CompetitionState>(s.InspectSnapshot().FindFragment("competition","player").Json).Current;oldResults=JsonUtility.ToJson(editionBefore);string achievement=p.Active.AchievementId;var normal=s.InspectSnapshot().Fragments.Where(f=>f.Owner!="progression").ToDictionary(f=>FragmentKey(f),f=>JsonUtility.ToJson(f));
      AutomationEvidence.WriteAllText(Path.Combine(dir,"ending-before.json"),JsonUtility.ToJson(s.InspectSnapshot(),true));send(opts.First(a=>a.Id=="ending-continue"));foreach(var f in s.InspectSnapshot().Fragments.Where(f=>f.Owner!="progression"))Assert.That(JsonUtility.ToJson(f),Is.EqualTo(normal[FragmentKey(f)]));string state=JsonUtility.ToJson(s.InspectSnapshot());s.Dispose();using(var store=new LocalCheckpointStore(Path.Combine(dir,"save"),new UnityJsonStateCodec())){store.Load();var history=new EndingProgression(new UnityJsonStateCodec()).ReadHistory(store,0,64);Assert.That(history.Count(h=>h.Snapshot.AchievementId==achievement),Is.EqualTo(1));Assert.That(history.Single(h=>h.Snapshot.AchievementId==achievement).Snapshot.EndingId,Is.EqualTo(p.Active.EndingId));}s=AutomationEvidenceSession.OpenAutomation(Path.Combine(dir,"save"),1);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(state));send(s.ReadView().Actions.First(a=>a.Id=="continue-game"));continued=true;exit="NaturalChampionContinued";continue;
     }
     foreach(string id in new[]{"ending-segment","ending-empty","settle","continue","shop-leave","item-use","competition-next","next-day"})if(o==null)o=opts.FirstOrDefault(a=>a.Id==id);
     if(!longRun&&day>=8&&enrolled){var enrollment=JsonUtility.FromJson<CompetitionState>(s.InspectSnapshot().FindFragment("competition","player").Json).Current;Assert.That(enrollment.Contest(targetKind).Enrolled,Is.True);var occupancy=JsonUtility.FromJson<FlowState>(s.InspectSnapshot().FindFragment("flow","player").Json).Occupancies;foreach(var kind in new[]{ContestKind.Bodybuilding,ContestKind.ThinBuild})Assert.That(occupancy.Count(x=>x.ActorId=="player"&&x.Day==42&&x.AnchorId==CompetitionContent.Anchor(kind)),Is.EqualTo(kind==targetKind?1:0));Assert.That(enrollment.Contests.Where(c=>c.Kind!=targetKind).All(c=>!c.Enrolled),Is.True);Assert.That(exercises.Count,Is.GreaterThanOrEqualTo(2),"CoverageMissing: multiple real exercise kernels");Assert.That(manual,Is.GreaterThan(0));exit="D8PreflightReached";break;}
     if(o!=null){send(o);if(o.Id=="competition-enroll")enrolled=true;}else {var unknown=opts.FirstOrDefault(a=>a.Id!="save-plan"&&a.Id!="open-menu"&&a.Id!="competition-menu");Assert.That(unknown,Is.Null,"MissingHandler: "+v.ScreenId+"/"+unknown?.Id);s.Advance(0);}
    }
    Assert.That(exit,Is.EqualTo(longRun?"NaturalChampionD43":"D8PreflightReached"));

   }catch(Exception ex){exception=ex.ToString();throw;}
   finally{AutomationEvidence.WriteAllText(Path.Combine(dir,"final.json"),JsonUtility.ToJson(s.InspectSnapshot(),true));AutomationEvidence.WriteAllText(Path.Combine(dir,"report.txt"),"strategy=natural-champion-candidate-v2\ncontest="+targetKind+"\nseed=1\nmaxDay=43\nmaxSteps=500000\nmaxSeconds="+seconds+"\nelapsedSeconds="+watch.Elapsed.TotalSeconds+"\nmode="+(longRun?"long":"D8-preflight")+"\nmanualSets="+manual+"\nautoSets="+automatic+"\nchallengeSets="+challenges+"\nobservedExercises="+string.Join(",",exercises)+"\nbusinessCommands="+commands+"\nevidenceBytes="+trace.Bytes+"\nevidenceByteLimit="+AutomationEvidence.TraceLimit+"\nviewReferences="+trace.ViewReferences+"\nfreezeVerified="+freezeVerified+"\ninputCommands="+inputCommands+"\ninputClockCalls="+inputClockCalls+"\ncontinued="+continued+"\nD43="+d43+"\noutcome="+exit+"\nexception="+exception+"\ncoverage=preflight is not champion; long mode audits scores/history/cursor");s.Dispose();TestContext.WriteLine(dir);}
  }
 }
}
}















