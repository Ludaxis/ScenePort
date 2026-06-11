using NUnit.Framework;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ComponentTypeCacheTests
    {
        [SetUp]
        public void Reset() => ComponentTypeCache.ResetForTests();

        [Test]
        public void ResolvesByShortName()
        {
            Assert.AreEqual(typeof(BoxCollider), ComponentTypeCache.Find("BoxCollider"));
        }

        [Test]
        public void ResolvesByFullName()
        {
            Assert.AreEqual(typeof(BoxCollider), ComponentTypeCache.Find("UnityEngine.BoxCollider"));
        }

        [Test]
        public void IsCaseInsensitive()
        {
            Assert.AreEqual(typeof(BoxCollider), ComponentTypeCache.Find("boxcollider"));
        }

        [Test]
        public void UnknownTypeReturnsNullAndIsCached()
        {
            Assert.IsNull(ComponentTypeCache.Find("NoSuchComponentType"));
            var missesAfterFirst = ComponentTypeCache.Misses;
            Assert.IsNull(ComponentTypeCache.Find("NoSuchComponentType"));
            Assert.AreEqual(missesAfterFirst, ComponentTypeCache.Misses, "Second lookup should hit the cache, not recompute.");
            Assert.GreaterOrEqual(ComponentTypeCache.Hits, 1);
        }

        [Test]
        public void SecondLookupServedFromMemo()
        {
            ComponentTypeCache.Find("BoxCollider");
            var hitsBefore = ComponentTypeCache.Hits;
            ComponentTypeCache.Find("BoxCollider");
            Assert.AreEqual(hitsBefore + 1, ComponentTypeCache.Hits);
        }
    }
}
