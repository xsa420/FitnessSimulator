using UnityEngine;

namespace FitnessSimulator.Training
{
    [CreateAssetMenu(menuName = "Fitness Simulator/Exercise Catalog")]
    public sealed class ExerciseCatalog : ScriptableObject
    {
        public ExercisePresentation[] exercises;
    }
}
