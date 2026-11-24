#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace VirtualHand.Editor
{
    public sealed class AvatarMeshRegionBuildResult
    {
        internal AvatarMeshRegionBuildResult(
            SkinnedMeshRenderer sourceRenderer,
            Mesh bodyMesh,
            Mesh headMesh,
            Mesh handsMesh,
            string bodyAssetPath,
            string headAssetPath,
            string handsAssetPath)
        {
            SourceRenderer = sourceRenderer;
            BodyMesh = bodyMesh;
            HeadMesh = headMesh;
            HandsMesh = handsMesh;
            BodyAssetPath = bodyAssetPath;
            HeadAssetPath = headAssetPath;
            HandsAssetPath = handsAssetPath;
        }

        public SkinnedMeshRenderer SourceRenderer { get; }
        public Mesh BodyMesh { get; }
        public Mesh HeadMesh { get; }
        public Mesh HandsMesh { get; }
        public string BodyAssetPath { get; }
        public string HeadAssetPath { get; }
        public string HandsAssetPath { get; }
    }

    public sealed class AvatarMeshRegionRenderers
    {
        internal AvatarMeshRegionRenderers(
            SkinnedMeshRenderer body,
            SkinnedMeshRenderer head,
            SkinnedMeshRenderer importedHands)
        {
            Body = body;
            Head = head;
            ImportedHands = importedHands;
        }

        public SkinnedMeshRenderer Body { get; }
        public SkinnedMeshRenderer Head { get; }
        public SkinnedMeshRenderer ImportedHands { get; }

        /// <summary>Disables only the generated MPFB hands renderer while Elena's replacement is active.</summary>
        public void SetElenaHandsReplacementActive(bool replacementActive)
        {
            if (ImportedHands != null)
                ImportedHands.enabled = !replacementActive;
        }
    }

    /// <summary>
    /// Editor-only partitioning for an existing skinned mesh. It never changes or deletes the source.
    /// </summary>
    public static class AvatarMeshRegionBuilder
    {
        public const string GeneratedAssetRoot = "Assets/VirtualHand/Generated/AvatarRegions";

        private enum VertexRegion
        {
            Body,
            Head,
            Hands
        }

        /// <summary>
        /// Creates three mesh assets at exact, caller-specified paths beneath GeneratedAssetRoot.
        /// A vertex is classified by summed normalized skin weights. A triangle is assigned to head or
        /// hands only when all three vertices classify to that same region; every boundary triangle stays on body.
        /// </summary>
        public static AvatarMeshRegionBuildResult BuildAssets(
            SkinnedMeshRenderer sourceRenderer,
            Transform[] headBones,
            Transform[] handBones,
            string outputFolder,
            string assetBaseName,
            float regionWeightThreshold = 0.5f)
        {
            if (sourceRenderer == null)
                throw new ArgumentNullException(nameof(sourceRenderer));

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (sourceMesh == null)
                throw new ArgumentException("The source renderer must have a shared mesh.", nameof(sourceRenderer));
            if (!sourceMesh.isReadable)
                throw new InvalidOperationException("The source mesh must have Read/Write Enabled to classify its bone weights.");
            if (sourceMesh.vertexCount == 0 || sourceMesh.subMeshCount == 0)
                throw new InvalidOperationException("The source mesh must contain vertices and submeshes.");
            if (float.IsNaN(regionWeightThreshold) || float.IsInfinity(regionWeightThreshold) ||
                regionWeightThreshold < 0f || regionWeightThreshold > 1f)
                throw new ArgumentOutOfRangeException(nameof(regionWeightThreshold), "Threshold must be between 0 and 1.");

            string normalizedFolder = ValidateOutputFolder(outputFolder);
            ValidateSimpleName(assetBaseName, nameof(assetBaseName));
            EnsureOutputFolderExists(normalizedFolder);

            Transform[] sourceBones = sourceRenderer.bones;
            if (sourceBones == null || sourceBones.Length == 0)
                throw new InvalidOperationException("The source renderer must have an explicit bone array.");

            HashSet<int> headBoneIndices = ResolveBoneIndices(headBones, sourceBones, "headBones");
            HashSet<int> handBoneIndices = ResolveBoneIndices(handBones, sourceBones, "handBones");
            foreach (int boneIndex in headBoneIndices)
            {
                if (handBoneIndices.Contains(boneIndex))
                    throw new ArgumentException("headBones and handBones must not overlap.");
            }

            VertexRegion[] vertexRegions = ClassifyVertices(
                sourceMesh,
                sourceBones.Length,
                headBoneIndices,
                handBoneIndices,
                regionWeightThreshold);

            List<int>[] bodyIndices = CreateSubmeshLists(sourceMesh.subMeshCount);
            List<int>[] headIndices = CreateSubmeshLists(sourceMesh.subMeshCount);
            List<int>[] handsIndices = CreateSubmeshLists(sourceMesh.subMeshCount);
            PartitionTriangles(sourceMesh, vertexRegions, bodyIndices, headIndices, handsIndices);

            string bodyPath = normalizedFolder + "/" + assetBaseName + "_Body.asset";
            string headPath = normalizedFolder + "/" + assetBaseName + "_Head.asset";
            string handsPath = normalizedFolder + "/" + assetBaseName + "_Hands.asset";
            EnsureAssetPathIsUnused(bodyPath);
            EnsureAssetPathIsUnused(headPath);
            EnsureAssetPathIsUnused(handsPath);

            Mesh bodyMesh = UnityEngine.Object.Instantiate(sourceMesh);
            Mesh headMesh = UnityEngine.Object.Instantiate(sourceMesh);
            Mesh handsMesh = UnityEngine.Object.Instantiate(sourceMesh);
            bodyMesh.name = assetBaseName + "_Body";
            headMesh.name = assetBaseName + "_Head";
            handsMesh.name = assetBaseName + "_Hands";

            Mesh[] generatedMeshes = { bodyMesh, headMesh, handsMesh };
            List<string> createdPaths = new List<string>();
            try
            {
                ApplySubmeshIndices(bodyMesh, bodyIndices, sourceMesh);
                ApplySubmeshIndices(headMesh, headIndices, sourceMesh);
                ApplySubmeshIndices(handsMesh, handsIndices, sourceMesh);

                AssetDatabase.CreateAsset(bodyMesh, bodyPath);
                createdPaths.Add(bodyPath);
                AssetDatabase.CreateAsset(headMesh, headPath);
                createdPaths.Add(headPath);
                AssetDatabase.CreateAsset(handsMesh, handsPath);
                createdPaths.Add(handsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch
            {
                for (int i = createdPaths.Count - 1; i >= 0; i--)
                    AssetDatabase.DeleteAsset(createdPaths[i]);
                foreach (Mesh generatedMesh in generatedMeshes)
                {
                    if (generatedMesh != null && !AssetDatabase.Contains(generatedMesh))
                        UnityEngine.Object.DestroyImmediate(generatedMesh);
                }
                throw;
            }

            return new AvatarMeshRegionBuildResult(
                sourceRenderer,
                bodyMesh,
                headMesh,
                handsMesh,
                bodyPath,
                headPath,
                handsPath);
        }

        /// <summary>
        /// Creates three sibling-style renderer objects under the explicit destination parent.
        /// Source renderer settings, bones, root bone, materials, and local transform values are copied.
        /// No source object, mesh, or camera is changed.
        /// </summary>
        public static AvatarMeshRegionRenderers CreateRendererCopies(
            SkinnedMeshRenderer sourceRenderer,
            AvatarMeshRegionBuildResult buildResult,
            Transform destinationParent,
            string objectNamePrefix)
        {
            if (sourceRenderer == null)
                throw new ArgumentNullException(nameof(sourceRenderer));
            if (buildResult == null)
                throw new ArgumentNullException(nameof(buildResult));
            if (buildResult.SourceRenderer != sourceRenderer)
                throw new ArgumentException("The build result was created from a different source renderer.", nameof(buildResult));
            if (destinationParent == null)
                throw new ArgumentNullException(nameof(destinationParent));
            ValidateSimpleName(objectNamePrefix, nameof(objectNamePrefix));

            string bodyName = objectNamePrefix + "_Body";
            string headName = objectNamePrefix + "_Head";
            string handsName = objectNamePrefix + "_Hands";
            EnsureChildNameIsUnused(destinationParent, bodyName);
            EnsureChildNameIsUnused(destinationParent, headName);
            EnsureChildNameIsUnused(destinationParent, handsName);

            Vector3 localPosition = sourceRenderer.transform.localPosition;
            Quaternion localRotation = sourceRenderer.transform.localRotation;
            Vector3 localScale = sourceRenderer.transform.localScale;

            SkinnedMeshRenderer body = CreateRendererCopy(
                sourceRenderer, buildResult.BodyMesh, destinationParent, bodyName,
                localPosition, localRotation, localScale);
            try
            {
                SkinnedMeshRenderer head = CreateRendererCopy(
                    sourceRenderer, buildResult.HeadMesh, destinationParent, headName,
                    localPosition, localRotation, localScale);
                try
                {
                    SkinnedMeshRenderer hands = CreateRendererCopy(
                        sourceRenderer, buildResult.HandsMesh, destinationParent, handsName,
                        localPosition, localRotation, localScale);
                    return new AvatarMeshRegionRenderers(body, head, hands);
                }
                catch
                {
                    Undo.DestroyObjectImmediate(head.gameObject);
                    throw;
                }
            }
            catch
            {
                Undo.DestroyObjectImmediate(body.gameObject);
                throw;
            }
        }

        private static SkinnedMeshRenderer CreateRendererCopy(
            SkinnedMeshRenderer sourceRenderer,
            Mesh regionMesh,
            Transform destinationParent,
            string objectName,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            GameObject instance = new GameObject(objectName);
            try
            {
                Undo.RegisterCreatedObjectUndo(instance, "Create avatar region renderer");
                instance.layer = sourceRenderer.gameObject.layer;
                instance.transform.SetParent(destinationParent, false);
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = localScale;

                SkinnedMeshRenderer renderer = Undo.AddComponent<SkinnedMeshRenderer>(instance);
                EditorUtility.CopySerialized(sourceRenderer, renderer);
                renderer.sharedMesh = regionMesh;
                renderer.bones = (Transform[])sourceRenderer.bones.Clone();
                renderer.rootBone = sourceRenderer.rootBone;
                Material[] materials = sourceRenderer.sharedMaterials;
                renderer.sharedMaterials = materials == null
                    ? Array.Empty<Material>()
                    : (Material[])materials.Clone();
                EditorUtility.SetDirty(renderer);
                return renderer;
            }
            catch
            {
                if (instance != null)
                    Undo.DestroyObjectImmediate(instance);
                throw;
            }
        }

        private static HashSet<int> ResolveBoneIndices(
            Transform[] requestedBones,
            Transform[] sourceBones,
            string parameterName)
        {
            if (requestedBones == null || requestedBones.Length == 0)
                throw new ArgumentException("Supply at least one explicit bone transform.", parameterName);

            HashSet<int> indices = new HashSet<int>();
            for (int requestedIndex = 0; requestedIndex < requestedBones.Length; requestedIndex++)
            {
                Transform requestedBone = requestedBones[requestedIndex];
                if (requestedBone == null)
                    throw new ArgumentException("Bone arrays cannot contain null transforms.", parameterName);

                bool found = false;
                for (int sourceIndex = 0; sourceIndex < sourceBones.Length; sourceIndex++)
                {
                    if (sourceBones[sourceIndex] == requestedBone)
                    {
                        indices.Add(sourceIndex);
                        found = true;
                    }
                }
                if (!found)
                    throw new ArgumentException("Every supplied transform must be present in sourceRenderer.bones.", parameterName);
            }
            return indices;
        }

        private static VertexRegion[] ClassifyVertices(
            Mesh mesh,
            int boneCount,
            HashSet<int> headBoneIndices,
            HashSet<int> handBoneIndices,
            float threshold)
        {
            NativeArray<byte> bonesPerVertex = default(NativeArray<byte>);
            NativeArray<BoneWeight1> allWeights = default(NativeArray<BoneWeight1>);
            try
            {
                bonesPerVertex = mesh.GetBonesPerVertex();
                allWeights = mesh.GetAllBoneWeights();
                if (!bonesPerVertex.IsCreated || bonesPerVertex.Length != mesh.vertexCount)
                    throw new InvalidOperationException("The source mesh has incomplete per-vertex bone weights.");
                if (!allWeights.IsCreated)
                    throw new InvalidOperationException("The source mesh does not expose readable bone weights.");

                VertexRegion[] regions = new VertexRegion[mesh.vertexCount];
                int weightOffset = 0;
                for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
                {
                    int influenceCount = bonesPerVertex[vertex];
                    if (weightOffset + influenceCount > allWeights.Length)
                        throw new InvalidOperationException("The source mesh has an invalid bone-weight buffer.");

                    float headWeight = 0f;
                    float handsWeight = 0f;
                    float totalWeight = 0f;
                    for (int influence = 0; influence < influenceCount; influence++)
                    {
                        BoneWeight1 weight = allWeights[weightOffset++];
                        if (float.IsNaN(weight.weight) || float.IsInfinity(weight.weight) || weight.weight < 0f)
                            throw new InvalidOperationException("The source mesh contains an invalid bone weight.");
                        if (weight.weight <= 0f)
                            continue;
                        if (weight.boneIndex < 0 || weight.boneIndex >= boneCount)
                            throw new InvalidOperationException("A source bone weight refers to an invalid bone index.");

                        totalWeight += weight.weight;
                        if (headBoneIndices.Contains(weight.boneIndex))
                            headWeight += weight.weight;
                        else if (handBoneIndices.Contains(weight.boneIndex))
                            handsWeight += weight.weight;
                    }

                    if (totalWeight <= 0f)
                    {
                        regions[vertex] = VertexRegion.Body;
                        continue;
                    }

                    float headFraction = headWeight / totalWeight;
                    float handsFraction = handsWeight / totalWeight;
                    float bodyFraction = Mathf.Max(0f, (totalWeight - headWeight - handsWeight) / totalWeight);

                    if (headFraction >= threshold && headFraction > handsFraction && headFraction > bodyFraction)
                        regions[vertex] = VertexRegion.Head;
                    else if (handsFraction >= threshold && handsFraction > headFraction && handsFraction > bodyFraction)
                        regions[vertex] = VertexRegion.Hands;
                    else
                        regions[vertex] = VertexRegion.Body;
                }

                if (weightOffset != allWeights.Length)
                    throw new InvalidOperationException("The source mesh has an invalid bone-weight buffer length.");
                return regions;
            }
            finally
            {
                if (bonesPerVertex.IsCreated)
                    bonesPerVertex.Dispose();
                if (allWeights.IsCreated)
                    allWeights.Dispose();
            }
        }

        private static List<int>[] CreateSubmeshLists(int submeshCount)
        {
            List<int>[] lists = new List<int>[submeshCount];
            for (int i = 0; i < lists.Length; i++)
                lists[i] = new List<int>();
            return lists;
        }

        private static void PartitionTriangles(
            Mesh sourceMesh,
            VertexRegion[] vertexRegions,
            List<int>[] bodyIndices,
            List<int>[] headIndices,
            List<int>[] handsIndices)
        {
            for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                if (sourceMesh.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new InvalidOperationException("All source submeshes must use triangle topology.");

                int[] indices = sourceMesh.GetIndices(submesh, false);
                if (indices.Length % 3 != 0)
                    throw new InvalidOperationException("A source triangle submesh has an incomplete triangle.");
                int baseVertex = (int)sourceMesh.GetBaseVertex(submesh);

                for (int i = 0; i < indices.Length; i += 3)
                {
                    int vertex0 = indices[i] + baseVertex;
                    int vertex1 = indices[i + 1] + baseVertex;
                    int vertex2 = indices[i + 2] + baseVertex;
                    if (vertex0 < 0 || vertex0 >= vertexRegions.Length ||
                        vertex1 < 0 || vertex1 >= vertexRegions.Length ||
                        vertex2 < 0 || vertex2 >= vertexRegions.Length)
                        throw new InvalidOperationException("A source triangle refers to a vertex outside the mesh.");

                    List<int> destination;
                    VertexRegion region0 = vertexRegions[vertex0];
                    VertexRegion region1 = vertexRegions[vertex1];
                    VertexRegion region2 = vertexRegions[vertex2];
                    if (region0 == VertexRegion.Head && region1 == VertexRegion.Head && region2 == VertexRegion.Head)
                        destination = headIndices[submesh];
                    else if (region0 == VertexRegion.Hands && region1 == VertexRegion.Hands && region2 == VertexRegion.Hands)
                        destination = handsIndices[submesh];
                    else
                        destination = bodyIndices[submesh];

                    destination.Add(indices[i]);
                    destination.Add(indices[i + 1]);
                    destination.Add(indices[i + 2]);
                }
            }
        }

        private static void ApplySubmeshIndices(Mesh target, List<int>[] indices, Mesh source)
        {
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                target.SetIndices(
                    indices[submesh].ToArray(),
                    MeshTopology.Triangles,
                    submesh,
                    false,
                    (int)source.GetBaseVertex(submesh));
            }
        }

        private static string ValidateOutputFolder(string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("Specify an output folder beneath " + GeneratedAssetRoot + ".", nameof(outputFolder));

            string normalized = outputFolder.Replace((char)92, '/').TrimEnd('/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
                throw new ArgumentException("Output must be a project-relative Assets path.", nameof(outputFolder));

            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new ArgumentException("Output folder cannot contain empty or traversal path segments.", nameof(outputFolder));
            }

            if (!string.Equals(normalized, GeneratedAssetRoot, StringComparison.Ordinal) &&
                !normalized.StartsWith(GeneratedAssetRoot + "/", StringComparison.Ordinal))
                throw new ArgumentException("Output folder must be beneath " + GeneratedAssetRoot + ".", nameof(outputFolder));
            return normalized;
        }

        private static void EnsureOutputFolderExists(string folder)
        {
            string[] segments = folder.Split('/');
            string current = segments[0];
            if (!AssetDatabase.IsValidFolder(current))
                throw new InvalidOperationException("The project Assets folder is not available.");

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    throw new IOException("Could not create generated output folder: " + next);
                current = next;
            }
        }

        private static void ValidateSimpleName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                name.Trim() != name || name.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException("Specify a non-empty name without leading/trailing whitespace or a trailing dot.", parameterName);

            foreach (char character in name)
            {
                if (char.IsControl(character) || character == '/' || character == (char)92 ||
                    character == ':' || character == '*' || character == '?' || character == '"' ||
                    character == '<' || character == '>' || character == '|')
                    throw new ArgumentException("Name contains a character that is not safe for an asset or GameObject name.", parameterName);
            }
        }

        private static void EnsureAssetPathIsUnused(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(absolutePath))
                throw new IOException("Generated output already exists; choose a new exact output name: " + assetPath);
        }

        private static void EnsureChildNameIsUnused(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == childName)
                    throw new InvalidOperationException("Destination parent already has a child named " + childName + ".");
            }
        }
    }
}
#endif