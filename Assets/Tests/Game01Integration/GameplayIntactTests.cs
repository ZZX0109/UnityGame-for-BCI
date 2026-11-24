#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
// Regression sentinel for Game01 key gameplay scripts. Pure file-existence
// assertions: if any of these files (or their .meta) is accidentally deleted,
// renamed or moved, these tests fail fast and point at the missing file.
using NUnit.Framework;
using UnityEngine;

namespace Game01Integration.Tests
{
    public sealed class GameplayIntactTests
    {
        private const string SokobanRoot = "Assets/FirstPersonSokoban";

        /// <summary>Key gameplay script files that must stay in place (relative to Assets/FirstPersonSokoban).</summary>
        private static readonly string[] GameplayScripts =
        {
            "Scripts/Interaction/BoxInteractor.cs",
            "Scripts/Player/FirstPersonController.cs",
            "Scripts/Core/PuzzleGameManager.cs",
            "Scripts/Core/GridManager.cs",
            "Scripts/Core/LevelLoader.cs",
            "Scripts/UI/GameUIController.cs",
            "Scripts/Save/SaveManager.cs"
        };

        [Test]
        public void KeyGameplayScriptsExist()
        {
            foreach (string relativePath in GameplayScripts)
            {
                string fullPath = $"{SokobanRoot}/{relativePath}";
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", fullPath)),
                    $"[file in place sentinel] {fullPath} is missing. Gameplay regression suspected; restore the file.");
            }
        }

        [Test]
        public void KeyGameplayScriptMetaFilesExist()
        {
            foreach (string relativePath in GameplayScripts)
            {
                string metaPath = $"{SokobanRoot}/{relativePath}.meta";
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", metaPath)),
                    $"[file in place sentinel] {metaPath} is missing. Without its .meta the script loses its guid and all scene references break.");
            }
        }

        [Test]
        public void VirtualHandRuntimeAssemblyDefinitionExists()
        {
            string asmdefPath = "Assets/VirtualHand/Scripts/VirtualHand.Runtime.asmdef";
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", asmdefPath)),
                $"[file in place sentinel] {asmdefPath} is missing; test and prefab script references depend on assembly 'Neuralife.VirtualHand.Runtime'.");
        }

        [Test]
        public void FirstPersonFullBodyPrefabExists()
        {
            string prefabPath = "Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab";
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", prefabPath)),
                $"[file in place sentinel] {prefabPath} is missing; full-body integration has no source asset.");
        }
    }
}
