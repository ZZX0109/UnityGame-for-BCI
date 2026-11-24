using System.Collections.Generic;
using FirstPersonSokoban.Core;
using NUnit.Framework;

namespace FirstPersonSokoban.Tests.EditMode
{
    /// <summary>Game01 关卡目录的完整性回归：条目数量、唯一标识与元数据有效性。</summary>
    public sealed class LevelCatalogIntegrityTests
    {
        [Test]
        public void LevelCatalog_HasExactlyEightEntries()
        {
            Assert.AreEqual(8, LevelCatalog.Count, "LevelCatalog.json 应包含恰好 8 个关卡。");
        }

        [Test]
        public void LevelCatalog_EntryIdsAreUnique()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                string id = LevelCatalog.GetId(i);
                Assert.IsFalse(string.IsNullOrWhiteSpace(id), $"第 {i} 个关卡的 id 不能为空。");
                Assert.IsTrue(ids.Add(id), $"关卡 id 重复：{id}。");
            }
        }

        [Test]
        public void LevelCatalog_ResourcesAreUniqueAndNamed()
        {
            var resources = new HashSet<string>();
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                string resource = LevelCatalog.Get(i).Resource;
                Assert.IsFalse(string.IsNullOrWhiteSpace(resource), $"第 {i} 个关卡的 resource 不能为空。");
                Assert.IsTrue(resources.Add(resource), $"关卡 resource 重复：{resource}。");
            }
        }

        [Test]
        public void LevelCatalog_RevisionsArePositive()
        {
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                int revision = LevelCatalog.GetRevision(i);
                Assert.GreaterOrEqual(revision, 1, $"关卡 {LevelCatalog.GetId(i)} 的 revision 必须为正数。");
            }
        }

        [Test]
        public void LevelCatalog_TitlesAndSubtitlesArePresent()
        {
            for (int i = 0; i < LevelCatalog.Count; i++)
            {
                LevelEntry entry = LevelCatalog.Get(i);
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Title), $"关卡 {entry.Id} 缺少 title。");
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Subtitle), $"关卡 {entry.Id} 缺少 subtitle。");
            }
        }
    }
}
