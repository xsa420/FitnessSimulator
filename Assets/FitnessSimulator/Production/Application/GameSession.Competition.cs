using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private const string CompetitionOwner="competition";
        private const string CompetitionRival="rival";
        [Serializable] private sealed class CompetitionWorldStamp { public int Day=0; public string Phase="",BarrierId=""; }
        [Serializable] private sealed class CompetitionMenuState { public bool Open; }
        [Serializable] private sealed class CompetitionDrawReceipt { public string EditionId; public DomainRandomState Before,After; public int[] BodyDraws,ThinDraws; }

        private CompetitionState ReadCompetition(RootEnvelope root) => CompetitionRules.Restore(Read<CompetitionState>(root,CompetitionOwner),root.RunId,Actor,CompetitionRival);
        private void EnsureCompetition(RootEnvelope root,string rivalId=CompetitionRival)
        {
            if(rivalId!=CompetitionRival)throw new InvalidOperationException("Competition requires the existing rival identity.");
            if(root.FindFragment(CompetitionOwner,Actor)==null)Write(root,CompetitionOwner,CompetitionRules.Create(root.RunId,Actor,rivalId));
            else ReadCompetition(root);
        }
        private void PrepareCompetitionDayOpen(RootEnvelope root,SessionState state,PreparedOutcome outcome)
        {
            EnsureCompetition(root); var competition=ReadCompetition(root); var edition=competition.Current;
            if(state.Day==edition.ContestDay+1) { competition=CompetitionRules.NextEdition(competition).State; edition=competition.Current; }
            int relative=state.Day-CompetitionContent.Offset(edition.Ordinal);
            if(relative<1||relative>CompetitionContent.CycleDays)throw new InvalidOperationException("Competition edition has not advanced with the calendar.");
            if(relative==CompetitionContent.OpenDay&&!edition.Opened) {
                string owner="competition-random";
                var random=root.FindFragment(owner,Actor)==null?
                    DomainRandom.Create(root.RunId,Actor,"competition-judges",state.InitialRandomSeed.ToString(CultureInfo.InvariantCulture)):
                    Read<DomainRandomState>(root,owner);
                DomainRandom.Validate(random);
                if(random.RunId!=root.RunId||random.ActorId!=Actor||random.Purpose!="competition-judges")throw new InvalidOperationException("Wrong competition random stream.");
                var before=random; var bodyDraws=new int[3]; var thinDraws=new int[3];
                foreach(var draws in new[]{bodyDraws,thinDraws})for(int i=0;i<3;i++) { var draw=DomainRandom.NextInt(random,4-i); draws[i]=(int)draw.Value; random=draw.State; }
                competition=CompetitionRules.Open(competition,state.Day,bodyDraws,thinDraws).State;
                Write(root,owner,random);
                outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="CompetitionJudgeDraw",
                    Json=codec.Encode(new CompetitionDrawReceipt { EditionId=edition.EditionId,Before=before,After=random,BodyDraws=bodyDraws,ThinDraws=thinDraws }) });
                outcome.Result="健美与薄肌赛事报名开放：第 "+edition.ContestDay+" 日比赛，至少实际参加一项；参赛非冠军将结束本局。";
            }
            if(relative>=CompetitionContent.OpenDay) {
                if(!competition.Current.Opened)throw new InvalidOperationException("Required competition opening was missed.");
                competition=CompetitionRules.AdvanceDisclosure(competition,state.Day).State;
            }
            if(relative==CompetitionContent.ContestDay&&competition.Current.FrozenActors.Length==0) {
                competition=CompetitionRules.Freeze(competition,ReadCompetitionFreezeInput(root,state)).State;
                outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="CompetitionFrozen",Json=codec.Encode(competition.Current) });
            }
            Write(root,CompetitionOwner,competition);
        }
        private CompetitionFreezeInput ReadCompetitionFreezeInput(RootEnvelope root,SessionState state)
        {
            var worldFragment=root.FindFragment("world","shared");
            if(worldFragment==null||worldFragment.SchemaVersion!=1||worldFragment.Revision>root.Revision)throw new InvalidOperationException("赛事等待已提交的真实世界共同入日屏障，缺少world/shared。");
            var world=codec.Decode<CompetitionWorldStamp>(worldFragment.Json);
            if(world==null||world.Day!=state.Day||world.Phase!=CompetitionContent.FreezePhase||string.IsNullOrWhiteSpace(world.BarrierId)||string.IsNullOrWhiteSpace(root.LastCommitId))
                throw new InvalidOperationException("赛事双方尚未到达同日DayOpenReady屏障。");
            var rivalSessionFragment=root.FindFragment("session",CompetitionRival);
            if(rivalSessionFragment==null||rivalSessionFragment.SchemaVersion!=1||rivalSessionFragment.Revision>root.Revision)throw new InvalidOperationException("赛事缺少已提交的真实竞争者日程。");
            var rivalSession=codec.Decode<SessionState>(rivalSessionFragment.Json);
            if(rivalSession==null||rivalSession.Day!=state.Day||rivalSession.Phase!=CompetitionContent.FreezePhase)
                throw new InvalidOperationException("真实竞争者尚未到达赛日共同入日阶段。");
            return new CompetitionFreezeInput { BarrierId=world.BarrierId,Day=state.Day,Phase=CompetitionContent.FreezePhase,
                Player=ReadCompetitionActor(root,Actor,state.Day,CompetitionSourceKind.PlayerProduction),
                Rival=ReadCompetitionActor(root,CompetitionRival,state.Day,CompetitionSourceKind.NpcProduction) };
        }
        private CompetitionActorSnapshot ReadCompetitionActor(RootEnvelope root,string actorId,int day,CompetitionSourceKind kind)
        {
            var fragment=root.FindFragment("body",actorId);
            if(fragment==null||fragment.SchemaVersion!=1||fragment.Revision>root.Revision)throw new InvalidOperationException("赛事缺少已提交的真实身体："+actorId);
            var body=codec.Decode<BodyState>(fragment.Json); if(body==null||body.ActorId!=actorId)throw new InvalidOperationException("赛事身体身份不匹配。");
            body.Validate();
            return new CompetitionActorSnapshot { ActorId=actorId,Body=body.Clone(),ActorRevision=body.Revision,SourceCommitId=root.LastCommitId,
                Day=day,Phase=CompetitionContent.FreezePhase,SourceKind=kind };
        }
        private void PrepareCompetitionDeadline(RootEnvelope root,SessionState state,PreparedOutcome outcome)
        {
            if(root.FindFragment(CompetitionOwner,Actor)==null)return;
            var competition=ReadCompetition(root);
            if(state.Day!=CompetitionContent.Offset(competition.Current.Ordinal)+CompetitionContent.RegistrationCloseDay||competition.Current.RegistrationClosed)return;
            if(state.AnchorIndex<SessionContent.Anchors(state).Length)throw new InvalidOperationException("赛事报名截止前仍有未完成锚点。");
            Write(root,CompetitionOwner,CompetitionRules.CloseRegistration(competition,state.Day,CompetitionContent.ClosePhase).State);
            outcome.Result="本届报名及退报已截止；明日入日完成后冻结双方真实参赛数据。";
        }
        private bool PrepareCompetitionPassedWindows(RootEnvelope root,SessionState state,FlowState flow,PreparedOutcome outcome)
        {
            if(root.FindFragment(CompetitionOwner,Actor)==null)return false;
            var competition=ReadCompetition(root); var edition=competition.Current;
            if(state.Day!=edition.ContestDay||edition.Failed)return false;
            var anchors=SessionContent.Anchors(state); bool failed=false;
            foreach(var kind in new[]{ContestKind.Bodybuilding,ContestKind.ThinBuild}) {
                int index=Array.IndexOf(anchors,CompetitionContent.Anchor(kind));
                if(index<0)throw new InvalidOperationException("Missing fixed competition anchor.");
                if(state.AnchorIndex<=index||competition.Current.Contest(kind).WindowClosed)continue;
                var change=CompetitionRules.CloseWindow(competition,kind,state.Day,CompetitionContent.Anchor(kind));
                ApplyCompetitionChange(root,flow,change,outcome); competition=change.State;
                if(change.Facts.Length>0) { CompleteDayBusiness(outcome); failed=competition.Current.Failed; }
            }
            return failed;
        }
        private bool TryResolveCompetitionAnchor(RootEnvelope root,SessionState state,FlowState flow,PreparedOutcome outcome)
        {
            if(PrepareCompetitionPassedWindows(root,state,flow,outcome))return true;
            if(root.FindFragment(CompetitionOwner,Actor)==null)return false;
            var competition=ReadCompetition(root); var e=competition.Current; var anchors=SessionContent.Anchors(state);
            if(state.Phase!="Anchors"||state.Day!=e.ContestDay||state.AnchorIndex>=anchors.Length)return false;
            string anchor=anchors[state.AnchorIndex]; var contest=e.Contests.SingleOrDefault(c=>CompetitionContent.Anchor(c.Kind)==anchor);
            if(contest==null||!contest.Enrolled||contest.WindowClosed)return false;
            if(e.Failed||e.FrozenActors.Length!=2)throw new InvalidOperationException("赛事尚未完成真实冻结或已失败。");
            string reservation=contest.ContestId+"/reservation";
            var occupied=flow.Occupancies.Where(o=>o.ActorId==Actor&&o.Day==state.Day&&o.AnchorId==anchor).ToArray();
            if(occupied.Any(o=>o.OwnerInstanceId!=reservation))throw new InvalidOperationException("赛事保留时段被其他事件占用。");
            flow.Occupancies.RemoveAll(o=>o.ActorId==Actor&&o.OwnerInstanceId==reservation);
            var entry=new EntryDefinition { DefinitionId=contest.ContestId+"/entry",RootNodeId="competition" };
            var definition=new FlowDefinition { Id=contest.ContestId,Version=CompetitionContent.Version,
                Nodes=new List<FlowNode>{new FlowNode { Id="competition",DefinitionId=entry.DefinitionId,Handler="competition" }},DefaultEntry=entry };
            FlowEngine.ResolveAnchor(flow,definition,Actor,state.Day,anchor,state.AnchorIndex,contest.ContestId+"/event",_=>true,_=>true);
            state.AnchorGameSeconds=0; state.AnchorStandardSeconds=1800; state.IncomingOvertime=state.LastOvertime;
            return true;
        }
        private bool TryPrepareCompetitionAction(RootEnvelope root,FrontendAction action,out PreparedOutcome outcome)
        {
            outcome=null;
            if(action.ActionId==null||!action.ActionId.StartsWith("competition-",StringComparison.Ordinal))return false;
            var state=Read<SessionState>(root,"session"); var training=Read<TrainingDayState>(root,"training"); var flow=Read<FlowState>(root,"flow");
            if(training.Workout?.Phase==WorkoutPhase.Playing||state.Phase!="Anchors"&&state.Phase!="AwaitNextDay")throw new InvalidOperationException("当前阶段不能操作赛事。");
            var competition=ReadCompetition(root); var edition=competition.Current;
            outcome=Outcome(root.RunId+"/"+action.ActionId+"/"+root.Revision,"赛事操作已保存。");
            if(action.ActionId=="competition-menu"||action.ActionId=="competition-close") {
                if(!edition.Opened&&edition.Ordinal==1)throw new InvalidOperationException("赛事入口尚未开放。");
                Write(root,"competition-ui",new CompetitionMenuState { Open=action.ActionId=="competition-menu" }); return true;
            }
            if(action.ActionId=="competition-next") {
                ApplyCompetitionChange(root,flow,CompetitionRules.NextEdition(competition),outcome);
                outcome.Result="下一届赛事已公布，新的参赛义务从本届重新计算。";
            } else {
                if(action.IntValue<0||action.IntValue>1)throw new ArgumentException("Unknown competition kind.");
                var kind=(ContestKind)action.IntValue; var contest=edition.Contest(kind); CompetitionChange change;
                switch(action.ActionId) {
                    case "competition-enroll": case "competition-withdraw":
                        bool available=!flow.Occupancies.Any(o=>o.ActorId==Actor&&o.Day==edition.ContestDay&&o.AnchorId==CompetitionContent.Anchor(kind)&&o.OwnerInstanceId!=contest.ContestId+"/reservation");
                        change=CompetitionRules.SetEnrollment(competition,kind,state.Day,action.ActionId=="competition-enroll",available);
                        break;
                    case "competition-attend": case "competition-decline":
                        var cursor=FlowEngine.Current(flow); var node=cursor==null?null:flow.Tree.Nodes.Single(n=>n.Id==cursor.NodeId);
                        RequireHandler(state,cursor,node,"competition");
                        var anchors=SessionContent.Anchors(state);
                        if(state.AnchorIndex>=anchors.Length||anchors[state.AnchorIndex]!=CompetitionContent.Anchor(kind)||flow.AnchorId!=CompetitionContent.Anchor(kind)||flow.Day!=state.Day||flow.ActorId!=Actor)
                            throw new InvalidOperationException("当前实际锚点与所选赛事不一致。");
                        change=action.ActionId=="competition-attend"?CompetitionRules.Attend(competition,kind,state.Day,flow.AnchorId):CompetitionRules.Decline(competition,kind,state.Day,flow.AnchorId);
                        FlowEngine.CompleteCurrent(flow,_=>false); CompleteDayBusiness(outcome); outcome.SemanticKey=cursor.InstanceId+"/complete";
                        Write(root,"competition-ui",new CompetitionMenuState()); break;
                    default: throw new InvalidOperationException("Unknown competition action.");
                }
                ApplyCompetitionChange(root,flow,change,outcome);
                outcome.Result=action.ActionId=="competition-attend"?DescribeCompetitionResult(change.State.Current.Contest(kind)):
                    action.ActionId=="competition-decline"?"已放弃本场；没有生成比赛分数。":action.ActionId=="competition-enroll"?"已报名并保留比赛时段。":"已退报并释放比赛时段。";
            }
            state.LastMessage=outcome.Result; Write(root,"session",state); Write(root,"flow",flow); return true;
        }
        private void ApplyCompetitionChange(RootEnvelope root,FlowState flow,CompetitionChange change,PreparedOutcome outcome)
        {
            foreach(var intent in change.ReservationIntents) {
                var collisions=flow.Occupancies.Where(o=>o.ActorId==Actor&&o.Day==intent.Day&&o.AnchorId==intent.AnchorId).ToArray();
                if(intent.Reserve) {
                    if(collisions.Any(o=>o.OwnerInstanceId!=intent.ReservationId))throw new InvalidOperationException("赛事保留时段冲突。");
                    if(collisions.Length==0)flow.Occupancies.Add(new AnchorOccupancy { ActorId=Actor,Day=intent.Day,AnchorId=intent.AnchorId,OwnerInstanceId=intent.ReservationId });
                } else flow.Occupancies.RemoveAll(o=>o.ActorId==Actor&&o.Day==intent.Day&&o.AnchorId==intent.AnchorId&&o.OwnerInstanceId==intent.ReservationId);
            }
            Write(root,CompetitionOwner,change.State);
            foreach(var fact in change.Facts)outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="CompetitionFact",Json=codec.Encode(fact) });
            foreach(var fact in change.Facts.Where(f=>!string.IsNullOrEmpty(f.ResultId))) {
                var result=change.State.Current.Contests.SelectMany(c=>c.Results).Single(r=>r.ResultId==fact.ResultId);
                outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="CompetitionResult",Json=codec.Encode(result) });
            }
        }
        private bool IsCompetitionMenuOpen(RootEnvelope root) => root.FindFragment("competition-ui",Actor)!=null&&Read<CompetitionMenuState>(root,"competition-ui").Open;
        private bool TryProjectCompetition(RootEnvelope root,SessionState state,FrontendView view,List<FrontendOption> actions)
        {
            if(root.FindFragment(CompetitionOwner,Actor)==null)return false;
            var competition=ReadCompetition(root); var edition=competition.Current;
            if(!edition.Opened&&edition.Ordinal==1)return false;
            var flow=Read<FlowState>(root,"flow"); var cursor=FlowEngine.Current(flow);
            bool atContest=state.Phase=="Anchors"&&cursor?.Phase==NodePhase.Started&&flow.Day==state.Day&&flow.Tree.Nodes.Single(n=>n.Id==cursor.NodeId).Handler=="competition";
            bool menu=IsCompetitionMenuOpen(root);
            if(!menu&&!atContest) {
                if(view.CanOpenAuxiliary&&(state.Phase=="Anchors"||state.Phase=="AwaitNextDay"))actions.Add(Option("competition-menu","查看两项赛事"));
                return false;
            }
            actions.Clear(); view.ScreenId="competition"; view.Title=atContest?"比赛出场":"赛事日程与报名";
            var text=new List<string>{"本届至少实际参加一项；每次出场非冠军均结束本局。双报须承担两场结果，之前夺冠不豁免后续失败。", "本届实际出场："+edition.ActualAttendanceCount};
            var anchors=SessionContent.Anchors(state); string actual=state.AnchorIndex<anchors.Length?anchors[state.AnchorIndex]:"";
            foreach(var entry in CompetitionQueries.Entries(competition,state.Day)) {
                text.Add(CompetitionConfiguration.Read().Contests.Single(c=>c.Kind==entry.Kind).Name+" · 第 "+entry.ContestDay+" 日 "+SessionContent.Name(entry.AnchorId)+" · "+(entry.DaysRemaining==0?"今日":entry.DaysRemaining+" 日后")+" · "+CompetitionStatusName(entry.Status));
                text.Add(string.Join("；",entry.Judges.Select(j=>j.Revealed?j.Name+"："+j.Description:"第"+j.Seat+"席：尚未公布")));
                if(entry.Results.Length>0)text.Add(DescribeCompetitionResult(edition.Contest(entry.Kind)));
                if(menu&&entry.CanEnroll)actions.Add(Option("competition-enroll","报名"+(entry.Kind==ContestKind.Bodybuilding?"健美":"薄肌"),null,(int)entry.Kind));
                if(menu&&entry.CanWithdraw)actions.Add(CompetitionConfirmed("competition-withdraw","退报"+(entry.Kind==ContestKind.Bodybuilding?"健美":"薄肌"),(int)entry.Kind,"退报将释放该时段；请确保仍有参赛安排。"));
                if(atContest&&entry.CanAttend&&actual==entry.AnchorId&&flow.AnchorId==entry.AnchorId) {
                    actions.Add(CompetitionConfirmed("competition-attend","出场并提交比赛结果",(int)entry.Kind,"确认出场？结果提交后成立；非冠军会结束本局。"));
                    actions.Add(CompetitionConfirmed("competition-decline","放弃本场",(int)entry.Kind,"确认放弃？本届已出场"+edition.ActualAttendanceCount+"次；剩余已报名未关闭赛事"+edition.Contests.Count(c=>c.Kind!=entry.Kind&&c.Enrolled&&!c.WindowClosed)+"项。所有窗口关闭时0出场会结束本局。"));
                }
            }
            if(menu)text.Add(DescribeCompetitionRival(root,state,competition));
            if(edition.Contests.All(c=>c.WindowClosed)&&!edition.Failed&&edition.ActualAttendanceCount>0)actions.Add(Option("competition-next","查看下一届赛程"));
            if(menu)actions.Add(Option("competition-close","返回当前日程"));
            actions.Add(Option("open-menu","返回主菜单（保留待出场状态）"));
            view.Body=string.Join("\n\n",text); return true;
        }
        private string DescribeCompetitionRival(RootEnvelope root,SessionState state,CompetitionState competition)
        {
            if(competition.Current.RevealedJudges==0)return "竞争者属性尚未披露。";
            var bodyFragment=root.FindFragment("body",CompetitionRival); var sessionFragment=root.FindFragment("session",CompetitionRival);
            if(bodyFragment==null||sessionFragment==null)return "竞争者实时数据尚未就绪。";
            if(bodyFragment.SchemaVersion!=1||sessionFragment.SchemaVersion!=1)throw new InvalidOperationException("Unsupported rival disclosure schema.");
            var rivalSession=codec.Decode<SessionState>(sessionFragment.Json);
            if(rivalSession==null||rivalSession.Day!=state.Day||string.IsNullOrWhiteSpace(rivalSession.Phase))return "竞争者尚未到达当前日，暂不展示旧数据。";
            var disclosure=CompetitionQueries.Rival(competition,codec.Decode<BodyState>(bodyFragment.Json),state.Day,rivalSession.Phase,root.LastCommitId);
            var text=new List<string>{"竞争者 · 第 "+disclosure.Day+" 日 · "+CompetitionPhaseName(rivalSession)+"（当前值，赛日另行冻结）"};
            if(disclosure.Level>=1)text.Add("力量均值区间："+disclosure.StrengthBucket);
            if(disclosure.Level>=2)text.Add("体脂区间："+disclosure.BodyFatBucket+"；最高肌群："+string.Join("、",disclosure.HighestGroups.Select(CompetitionGroupName))+"；最低肌群："+string.Join("、",disclosure.LowestGroups.Select(CompetitionGroupName))+"；"+disclosure.BalanceDescription);
            if(disclosure.Level>=3) {
                text.Add("体脂 "+Format(disclosure.BodyFatMicro)+"；基础魅力 "+Format(disclosure.BaseCharmMicro));
                for(int i=0;i<disclosure.RegionIds.Length;i++)text.Add(RegionNames[Array.IndexOf(RegionIds.All.ToArray(),disclosure.RegionIds[i])]+" "+Format(disclosure.StrengthMicro[i]));
            }
            return string.Join("\n",text);
        }
        private static string CompetitionPhaseName(SessionState state)
        {
            switch(state.Phase) {
                case "DayOpenReady":return "入日初始化完成，当日事件前";
                case "EnterDay":return "入日初始化中";
                case "DayEnd":return "日结中";
                case "AwaitNextDay":return "日结已完成";
                case "Anchors":
                    var anchors=SessionContent.Anchors(state);
                    return state.AnchorIndex>=0&&state.AnchorIndex<anchors.Length?SessionContent.Name(anchors[state.AnchorIndex]):"当日时段已结束";
                default:return "当前已提交阶段";
            }
        }
        private static string CompetitionGroupName(string id)
        {
            switch(id) { case "chest":return "胸";case "back":return "背";case "leg":return "腿";case "shoulder":return "肩";
                case "biceps":return "二头";case "triceps":return "三头";case "abs":return "腹";case "glutes":return "臀";default:throw new ArgumentException("Unknown muscle group."); }
        }
        private FrontendOption CompetitionConfirmed(string id,string label,int kind,string confirmation)
        { var option=Option(id,label,null,kind); option.RequiresConfirmation=true; option.ConfirmationText=confirmation; return option; }
        private static string CompetitionStatusName(string status)
        {
            switch(status) { case "Settled":return "已结算";case "Declined":return "已放弃";case "Closed":return "出场窗口已关闭";case "Stopped":return "本局已结束";
                case "AwaitingAttendance":return "待出场";case "RegistrationClosed":return "报名已截止";case "Enrolled":return "已报名";case "RegistrationOpen":return "可报名";case "Upcoming":return "尚未开放报名";default:throw new ArgumentException("Unknown competition status."); }
        }
        private static string DescribeCompetitionResult(ContestState contest)
        {
            if(contest.Results.Length==0)return "尚未结算";
            var result=contest.Results[0].Score;
            return string.Join("\n",result.Participants.Select(p=>(p.ActorId==Actor?"玩家":"竞争者")+"："+string.Join(" / ",p.Judges.Select(j=>(j.ScoreUnits4/10000m).ToString("0.0000",CultureInfo.InvariantCulture)+" · "+(string.IsNullOrEmpty(j.CommentaryTextRef)?"此旧结果未保存点评":FitnessSimulator.Game.Presentation.PresentationContent.Text(j.CommentaryTextRef))))+
                "；均分 "+(p.DisplayMeanCents/100m).ToString("0.00",CultureInfo.InvariantCulture)+"；第 "+p.Rank+" 名"))+(result.SharedChampionship?"\n并列冠军":"");
        }
    }
}
