using UnityEngine;

namespace FitnessSimulator.Training
{
    [CreateAssetMenu(menuName = "Fitness Simulator/Exercise Presentation")]
    public sealed class ExercisePresentation : ScriptableObject
    {
        public string exerciseId;
        public string displayName;
        [TextArea] public string presentationNotes;
        public string[] targetMuscleTags;
        public GameObject stagePrefab;
        public AnimationClip demonstrationClip;
        public bool demonstrationOnly = true;
        public string[] referenceUrls;
    }
}
