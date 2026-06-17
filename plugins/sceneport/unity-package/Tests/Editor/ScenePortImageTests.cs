using NUnit.Framework;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortImageTests
    {
        [Test]
        public void FitDimensionsLeavesSmallImageUnchanged()
        {
            ScenePortImage.FitDimensions(640, 480, 1024, out var width, out var height);
            Assert.AreEqual(640, width);
            Assert.AreEqual(480, height);
        }

        [Test]
        public void FitDimensionsDownscalesLongestEdgePreservingAspect()
        {
            ScenePortImage.FitDimensions(2048, 1024, 1024, out var width, out var height);
            Assert.AreEqual(1024, width);
            Assert.AreEqual(512, height);
        }

        [Test]
        public void FitDimensionsHandlesPortraitOrientation()
        {
            ScenePortImage.FitDimensions(1000, 2000, 1000, out var width, out var height);
            Assert.AreEqual(500, width);
            Assert.AreEqual(1000, height);
        }

        [Test]
        public void FitDimensionsWithZeroMaxEdgeKeepsSource()
        {
            ScenePortImage.FitDimensions(2048, 2048, 0, out var width, out var height);
            Assert.AreEqual(2048, width);
            Assert.AreEqual(2048, height);
        }

        [Test]
        public void EncodeBase64ReturnsNonEmptyForSmallTexture()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[8 * 8];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(10, 20, 30, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply();

                var encoded = ScenePortImage.EncodeBase64(texture, 1024);
                Assert.IsFalse(string.IsNullOrEmpty(encoded.Base64));
                Assert.AreEqual(8, encoded.Width);
                Assert.AreEqual(8, encoded.Height);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EncodeBase64DownscalesWhenLargerThanMaxEdge()
        {
            var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            try
            {
                texture.Apply();
                var encoded = ScenePortImage.EncodeBase64(texture, 16);
                Assert.IsFalse(string.IsNullOrEmpty(encoded.Base64));
                Assert.AreEqual(16, encoded.Width);
                Assert.AreEqual(8, encoded.Height);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EncodeBase64WithNullTextureReturnsEmpty()
        {
            var encoded = ScenePortImage.EncodeBase64(null, 1024);
            Assert.IsTrue(string.IsNullOrEmpty(encoded.Base64));
        }
    }
}
