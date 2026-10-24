using System;

namespace FitnessSimulator.Game.Training
{
    public enum TrainingDayClaimStatus { Available,Claimed,Closed }
    public enum TrainingClaimDisposition { Created,AlreadyClaimed,Conflict,Closed }
    public enum TrainingDayCloseReason { None,Missed,Completed,EndedEarly,Terminated }
    [Serializable] public sealed class TrainingDayClaim
    {
        public int SchemaVersion=1;
        public string RunId,ActorId,ScheduleRevision,ArrivalAnchor,WorkoutId;
        public int Day;
        public bool HasWorkoutId;
        public TrainingDayClaimStatus Status;
        public TrainingDayCloseReason CloseReason;
        public TrainingDayClaim Clone()=>(TrainingDayClaim)MemberwiseClone();
        public void Validate()
        {
            if(SchemaVersion!=1||string.IsNullOrWhiteSpace(RunId)||string.IsNullOrWhiteSpace(ActorId)||string.IsNullOrWhiteSpace(ScheduleRevision)||Day<1||!Enum.IsDefined(typeof(TrainingDayClaimStatus),Status)||!Enum.IsDefined(typeof(TrainingDayCloseReason),CloseReason))throw new ArgumentException("Invalid day claim");
            if(HasWorkoutId&&(string.IsNullOrWhiteSpace(WorkoutId)||string.IsNullOrWhiteSpace(ArrivalAnchor)))throw new ArgumentException("Missing claimed workout/anchor");
            if(!HasWorkoutId&&(!string.IsNullOrEmpty(WorkoutId)||!string.IsNullOrEmpty(ArrivalAnchor)))throw new ArgumentException("Unexpected workout identity");
            if(Status==TrainingDayClaimStatus.Available&&(HasWorkoutId||CloseReason!=TrainingDayCloseReason.None))throw new ArgumentException("Invalid available claim");
            if(Status==TrainingDayClaimStatus.Claimed&&(!HasWorkoutId||CloseReason!=TrainingDayCloseReason.None))throw new ArgumentException("Invalid active claim");
            if(Status==TrainingDayClaimStatus.Closed&&(CloseReason==TrainingDayCloseReason.None||(!HasWorkoutId&&CloseReason!=TrainingDayCloseReason.Missed&&CloseReason!=TrainingDayCloseReason.Terminated)||(HasWorkoutId&&CloseReason==TrainingDayCloseReason.Missed)))throw new ArgumentException("Invalid closed claim");
        }
    }
    public sealed class TrainingClaimPreparation
    {
        public TrainingDayClaim NextClaim { get; internal set; }
        public TrainingClaimDisposition Disposition { get; internal set; }
    }
    public static class TrainingClaims
    {
        public static TrainingDayClaim CreateDay(string runId,string actorId,int day,string scheduleRevision)
        {
            var c=new TrainingDayClaim { RunId=runId,ActorId=actorId,Day=day,ScheduleRevision=scheduleRevision,WorkoutId="",ArrivalAnchor="" }; c.Validate(); return c;
        }
        public static TrainingClaimPreparation TryClaim(TrainingDayClaim current,string runId,string actorId,int day,string scheduleRevision,string workoutId,string arrivalAnchor)
        {
            current.Validate();
            if(string.IsNullOrWhiteSpace(workoutId)||string.IsNullOrWhiteSpace(arrivalAnchor))throw new ArgumentException("Missing workout/anchor");
            var next=current.Clone(); TrainingClaimDisposition disposition;
            if(current.RunId!=runId||current.ActorId!=actorId||current.Day!=day||current.ScheduleRevision!=scheduleRevision)disposition=TrainingClaimDisposition.Conflict;
            else if(current.HasWorkoutId&&(current.WorkoutId!=workoutId||current.ArrivalAnchor!=arrivalAnchor))disposition=TrainingClaimDisposition.Conflict;
            else if(current.Status==TrainingDayClaimStatus.Closed)disposition=TrainingClaimDisposition.Closed;
            else if(current.Status==TrainingDayClaimStatus.Claimed)disposition=TrainingClaimDisposition.AlreadyClaimed;
            else { next.Status=TrainingDayClaimStatus.Claimed; next.HasWorkoutId=true; next.WorkoutId=workoutId; next.ArrivalAnchor=arrivalAnchor; disposition=TrainingClaimDisposition.Created; }
            next.Validate(); return new TrainingClaimPreparation { NextClaim=next,Disposition=disposition };
        }
        public static TrainingDayClaim Close(TrainingDayClaim current,TrainingDayCloseReason reason)
        {
            current.Validate();
            if(reason==TrainingDayCloseReason.None||!Enum.IsDefined(typeof(TrainingDayCloseReason),reason))throw new ArgumentException("Invalid close reason");
            if(current.Status==TrainingDayClaimStatus.Closed)
            {
                if(current.CloseReason!=reason)throw new InvalidOperationException("Conflicting close reason");
                return current.Clone();
            }
            var next=current.Clone(); next.Status=TrainingDayClaimStatus.Closed; next.CloseReason=reason; next.Validate(); return next;
        }
    }
}
