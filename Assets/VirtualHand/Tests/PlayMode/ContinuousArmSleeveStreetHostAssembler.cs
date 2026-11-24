#if UNITY_EDITOR
using System;
using Neuralife.VirtualHand;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Small editor-only fixture assembler for testing the sleeve in an isolated host.
    /// It locates and instantiates the project's real FirstPersonWrist prefab without calling
    /// FirstPersonWristCuffBuilder or changing the imported hand assets.
    /// </summary>
    public static class ContinuousArmSleeveStreetHostAssembler
    {
        public sealed class Host
        {
            public GameObject root;
            public GameObject leftHandVisual;
            public GameObject rightHandVisual;
            public SeatedArmPreview preview;
            public ContinuousArmSleeve sleeve;
            public Transform leftShoulder;
            public Transform leftElbow;
            public Transform leftWrist;
            public Transform rightShoulder;
            public Transform rightElbow;
            public Transform rightWrist;
        }

        public static GameObject FindActualHandPrefab()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            GameObject best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!candidate || !candidate.GetComponentInChildren<FirstPersonWrist>(true))
                    continue;

                int score = 0;
                if (candidate.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                if (candidate.GetComponent<FirstPersonWrist>()) score += 4;
                if (candidate.GetComponentInChildren<SkinnedMeshRenderer>(true)) score += 2;
                if (HasWovenMaterial(candidate)) score += 6;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        public static Material FindGeneratedWeaveMaterial(GameObject handPrefab = null)
        {
            Material fromPrefab = FindWovenMaterial(handPrefab);
            if (fromPrefab) return fromPrefab;

            string[] guids = AssetDatabase.FindAssets("t:Material");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (IsWovenMaterial(material)) return material;
            }
            return null;
        }

        public static Host CreateIsolatedStreetHost(GameObject handPrefab, Material weaveMaterial,
            Transform streetHostRoot = null)
        {
            if (!handPrefab) throw new ArgumentNullException("handPrefab");
            if (!weaveMaterial) weaveMaterial = FindGeneratedWeaveMaterial(handPrefab);
            if (!weaveMaterial) throw new InvalidOperationException("The generated woven cuff material was not found.");

            Host host = new Host();
            host.root = new GameObject("Continuous Arm Sleeve Street Host");
            if (streetHostRoot) host.root.transform.SetParent(streetHostRoot, false);

            Transform body = CreateNode(host.root.transform, "Body Origin");
            Transform leftRoot = CreateNode(host.root.transform, "Left Sleeve Outputs");
            Transform rightRoot = CreateNode(host.root.transform, "Right Sleeve Outputs");
            host.leftShoulder = CreateNode(leftRoot, "Shoulder");
            host.leftElbow = CreateNode(leftRoot, "Elbow");
            host.leftWrist = CreateNode(leftRoot, "Wrist");
            host.rightShoulder = CreateNode(rightRoot, "Shoulder");
            host.rightElbow = CreateNode(rightRoot, "Elbow");
            host.rightWrist = CreateNode(rightRoot, "Wrist");

            host.leftHandVisual = InstantiateHand(handPrefab, host.root.transform, "Actual Left Hand Prefab");
            host.rightHandVisual = InstantiateHand(handPrefab, host.root.transform, "Actual Right Hand Prefab");
            host.leftHandVisual.transform.localPosition = new Vector3(-0.55f, 0f, 0f);
            host.rightHandVisual.transform.localPosition = new Vector3(0.55f, 0f, 0f);
            DisablePrefabBehaviours(host.leftHandVisual);
            DisablePrefabBehaviours(host.rightHandVisual);

            host.preview = host.root.AddComponent<SeatedArmPreview>();
            host.preview.pair = null;
            host.preview.bodyOrigin = body;
            host.preview.leftOutputRoot = leftRoot.gameObject;
            host.preview.rightOutputRoot = rightRoot.gameObject;
            host.preview.leftShoulderPoint = host.leftShoulder;
            host.preview.leftElbowPoint = host.leftElbow;
            host.preview.leftWristPoint = host.leftWrist;
            host.preview.rightShoulderPoint = host.rightShoulder;
            host.preview.rightElbowPoint = host.rightElbow;
            host.preview.rightWristPoint = host.rightWrist;
            host.preview.enabled = false;
            leftRoot.gameObject.SetActive(true);
            rightRoot.gameObject.SetActive(true);

            host.sleeve = host.root.AddComponent<ContinuousArmSleeve>();
            host.sleeve.preview = host.preview;
            host.sleeve.mountRoot = host.root.transform;
            host.sleeve.weaveMaterial = weaveMaterial;
            host.sleeve.Refresh();
            return host;
        }

        private static GameObject InstantiateHand(GameObject prefab, Transform parent, string instanceName)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (!instance) instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = instanceName;
            instance.transform.SetParent(parent, false);
            return instance;
        }

        private static void DisablePrefabBehaviours(GameObject instance)
        {
            Behaviour[] behaviours = instance.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i]) behaviours[i].enabled = false;
        }

        private static Transform CreateNode(Transform parent, string nodeName)
        {
            GameObject node = new GameObject(nodeName);
            node.transform.SetParent(parent, false);
            return node.transform;
        }

        private static bool HasWovenMaterial(GameObject root)
        {
            return FindWovenMaterial(root) != null;
        }

        private static Material FindWovenMaterial(GameObject root)
        {
            if (!root) return null;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i] ? renderers[i].sharedMaterial : null;
                if (IsWovenMaterial(material)) return material;
            }
            return null;
        }

        private static bool IsWovenMaterial(Material material)
        {
            return material && material.name.IndexOf("First Person Woven Cuff", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
