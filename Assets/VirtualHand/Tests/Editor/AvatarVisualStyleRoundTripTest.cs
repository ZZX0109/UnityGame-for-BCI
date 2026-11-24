using System;
using UnityEditor;
using UnityEngine;
using Neuralife.VirtualHand;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Focused, manually runnable editor test for material-slot preservation and restoration.
    /// It does not require an avatar prefab or change project assets.
    /// </summary>
    public static class AvatarVisualStyleRoundTripTest
    {
        public static Material Run()
        {
            GameObject root = null;
            Material firstOriginalA = null;
            Material firstOriginalB = null;
            Material secondOriginalA = null;
            Material secondOriginalB = null;
            Material secondOriginalC = null;

            try
            {
                Shader standardShader = Shader.Find("Standard");
                if (!standardShader)
                    throw new InvalidOperationException("The Built-in Standard shader was not found.");

                firstOriginalA = new Material(standardShader);
                firstOriginalB = new Material(standardShader);
                secondOriginalA = new Material(standardShader);
                secondOriginalB = new Material(standardShader);
                secondOriginalC = new Material(standardShader);

                root = new GameObject("AvatarVisualStyle Round Trip Test");
                GameObject firstObject = new GameObject("Renderer With Two Slots");
                firstObject.transform.SetParent(root.transform, false);
                MeshRenderer firstRenderer = firstObject.AddComponent<MeshRenderer>();
                Material[] firstOriginals = { firstOriginalA, firstOriginalB };
                firstRenderer.sharedMaterials = firstOriginals;

                GameObject secondObject = new GameObject("Renderer With Three Slots");
                secondObject.transform.SetParent(root.transform, false);
                MeshRenderer secondRenderer = secondObject.AddComponent<MeshRenderer>();
                Material[] secondOriginals = { secondOriginalA, secondOriginalB, secondOriginalC };
                secondRenderer.sharedMaterials = secondOriginals;

                AvatarVisualStyle style = root.AddComponent<AvatarVisualStyle>();
                style.targets = new Renderer[] { firstRenderer, secondRenderer };

                style.SetBlueMode(true);
                Material[] firstBlue = firstRenderer.sharedMaterials;
                Material[] secondBlue = secondRenderer.sharedMaterials;
                Require(firstBlue.Length == firstOriginals.Length, "Blue mode changed the first renderer's slot count.");
                Require(secondBlue.Length == secondOriginals.Length, "Blue mode changed the second renderer's slot count.");
                Require(firstBlue.Length == 2 && secondBlue.Length == 3, "The test renderers did not retain their distinct slot counts.");
                Require(firstBlue[0] && firstBlue[0].shader == Shader.Find(AvatarVisualStyle.ShaderName), "Blue mode did not assign the requested shader.");
                Require(object.ReferenceEquals(firstBlue[0], firstBlue[1]), "Slots on one renderer do not share the generated material.");
                Require(object.ReferenceEquals(firstBlue[0], secondBlue[0]), "Target renderers do not share the generated material.");

                style.SetBlueMode(false);
                RequireSameMaterials(firstRenderer.sharedMaterials, firstOriginals, "First renderer did not restore its original materials.");
                RequireSameMaterials(secondRenderer.sharedMaterials, secondOriginals, "Second renderer did not restore its original materials.");

                style.SetBlueMode(true);
                Material generatedMaterial = firstRenderer.sharedMaterials[0];
                style.SetBlueMode(true);
                Require(object.ReferenceEquals(generatedMaterial, firstRenderer.sharedMaterials[0]), "Repeated blue switching generated a different material.");

                style.enabled = false;
                RequireSameMaterials(firstRenderer.sharedMaterials, firstOriginals, "OnDisable did not restore the first renderer.");
                RequireSameMaterials(secondRenderer.sharedMaterials, secondOriginals, "OnDisable did not restore the second renderer.");

                style.enabled = true;
                Require(object.ReferenceEquals(generatedMaterial, firstRenderer.sharedMaterials[0]), "Re-enabling did not restore the selected blue mode.");
                UnityEngine.Object.DestroyImmediate(style);
                RequireSameMaterials(firstRenderer.sharedMaterials, firstOriginals, "OnDestroy did not restore the first renderer.");
                RequireSameMaterials(secondRenderer.sharedMaterials, secondOriginals, "OnDestroy did not restore the second renderer.");
                // Runtime Destroy is deferred until the end of the frame; the caller checks after yielding.
                return generatedMaterial;
            }
            finally
            {
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                DestroyMaterial(firstOriginalA);
                DestroyMaterial(firstOriginalB);
                DestroyMaterial(secondOriginalA);
                DestroyMaterial(secondOriginalB);
                DestroyMaterial(secondOriginalC);
            }
        }

        private static void RequireSameMaterials(Material[] actual, Material[] expected, string message)
        {
            if (actual == null || actual.Length != expected.Length)
                throw new InvalidOperationException(message);

            for (int i = 0; i < expected.Length; i++)
            {
                if (!object.ReferenceEquals(actual[i], expected[i]))
                    throw new InvalidOperationException(message);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material)
                UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
