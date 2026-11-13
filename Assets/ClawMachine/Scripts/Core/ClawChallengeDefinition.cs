using System;
using UnityEngine;

namespace ClawMachine
{
    [Serializable]
    public sealed class PrizeSpawnDefinition
    {
        public string prizeId;
        public string displayName;
        public Vector3 spawnPosition;
        public Vector3 spawnEuler;
        public PrizeProfile graspProfile;
    }
    public enum FailedAttemptResetPolicy { RestoreSameLayout }
    [CreateAssetMenu(menuName = "Claw Machine/Challenge")]
    public sealed class ClawChallengeDefinition : ScriptableObject
    {
        public string challengeId = "C01";
        public string title = "C01 · 三轴定位教学";
        [TextArea] public string instructions = "观察正面和侧面，对准目标顶部下方的抓持带。";
        [TextArea] public string gripHint = "先安全接近，再慢速下降。";
        public string prizeId = "mint-parcel";
        public string targetId = "mint-parcel";
        public int seed = 20261112;
        [Min(1)] public int attemptLimit = 5;
        public PrizeSpawnDefinition[] spawns = new PrizeSpawnDefinition[0];
        public FailedAttemptResetPolicy failedAttemptResetPolicy;
        public Vector3 spawnPosition = new Vector3(0.08f, 0.92f, 0.08f);
        public Vector3 spawnEuler;
        public string cameraPolicy = "FrontSidePipAndBoundedOrbit";
        public string successRule = "ReleasedTargetFullyInsideChuteAndStable";
        public PrizeProfile graspProfile;
        public Vector3 SpawnForSeed() => PositionForSeed(spawnPosition, seed, prizeId);
        public static Vector3 PositionForSeed(Vector3 position, int seed, string id)
        {
            int stableHash = 17;
            if (id != null) foreach (char c in id) stableHash = unchecked(stableHash * 31 + c);
            var random = new System.Random(unchecked(seed + stableHash));
            return position + new Vector3((float)(random.NextDouble() - 0.5) * 0.02f, 0, (float)(random.NextDouble() - 0.5) * 0.02f);
        }
    }
}
