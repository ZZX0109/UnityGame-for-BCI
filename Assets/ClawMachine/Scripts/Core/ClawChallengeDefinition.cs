using UnityEngine;

namespace ClawMachine
{
    [CreateAssetMenu(menuName = "Claw Machine/Challenge")]
    public sealed class ClawChallengeDefinition : ScriptableObject
    {
        public string challengeId = "C01";
        public string title = "C01 · 三轴定位教学";
        public string prizeId = "mint-parcel";
        public string targetId = "mint-parcel";
        public int seed = 20261112;
        [Min(1)] public int attemptLimit = 5;
        public Vector3 spawnPosition = new Vector3(0.08f, 0.91f, 0.08f);
        public Vector3 spawnEuler;
        public string cameraPolicy = "FrontLeftRight";
        public string successRule = "ReleasedTargetFullyInsideChuteAndStable";
        public PrizeProfile graspProfile;
        public Vector3 SpawnForSeed()
        {
            // Own PRNG: never perturb Unity's global random state or PhysX.
            var random = new System.Random(seed);
            return spawnPosition + new Vector3((float)(random.NextDouble() - 0.5) * 0.08f, 0,
                (float)(random.NextDouble() - 0.5) * 0.08f);
        }
    }
}
