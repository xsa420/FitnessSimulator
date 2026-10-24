using System;
using System.Linq;
using FitnessSimulator.Game.Body;

namespace FitnessSimulator.Game.Training
{
    public sealed class StudyCalculation
    {
        public BodyState NextBody { get; internal set; }
        public int GameSeconds { get; internal set; }
        public string[] ExerciseIds { get; internal set; }
        public long KnowledgePerExerciseMicro { get; internal set; }
    }
    public static class TrainingKnowledgeRules
    {
        public static StudyCalculation Evening(BodyState body,string[] practicedExercises,bool fullDayStudyAlreadyChosen)
        {
            if(fullDayStudyAlreadyChosen||practicedExercises==null||practicedExercises.Length==0||practicedExercises.Distinct().Count()!=practicedExercises.Length)throw new InvalidOperationException("Evening study requires distinct actually practiced exercises and no all-day study");
            foreach(string id in practicedExercises)ExerciseCatalog.Get(id);
            var d=TrainingContent.Current.Knowledge;return Apply(body,practicedExercises,d.EveningStudyMicro,d.EveningStudySeconds);
        }
        public static StudyCalculation FullDay(BodyState body,bool isRestDay,int consumedOrdinaryAnchorCount)
        {
            if(!isRestDay||consumedOrdinaryAnchorCount<=0)throw new InvalidOperationException("All-day study requires rest day and explicit ordinary anchors");
            var d=TrainingContent.Current.Knowledge;return Apply(body,ExerciseCatalog.All.Select(e=>e.Id).ToArray(),d.FullDayStudyMicro,checked(d.FullDayStudySecondsPerAnchor*consumedOrdinaryAnchorCount));
        }
        public static long NotebookKnowledgeMicro=>TrainingContent.Current.Knowledge.NotebookMicro;
        private static StudyCalculation Apply(BodyState body,string[] ids,long amount,int seconds)=>new StudyCalculation { NextBody=BodyCalculator.AddKnowledge(body,ids,amount),ExerciseIds=(string[])ids.Clone(),KnowledgePerExerciseMicro=amount,GameSeconds=seconds };
    }
}
