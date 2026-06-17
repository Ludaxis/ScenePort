using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class GoldenCompareTests
    {
        private ScenePortContext ctx;
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Version = "test", BoundPort = 0 };
            tempDir = Path.Combine(Path.GetTempPath(), "sceneport-golden-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        private string WritePng(string name, Color32 fill, Color32? singlePixel = null)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[8 * 8];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = fill;
                }
                if (singlePixel.HasValue)
                {
                    pixels[0] = singlePixel.Value;
                }
                texture.SetPixels32(pixels);
                texture.Apply();

                var path = Path.Combine(tempDir, name);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static T Read<T>(object response, string property)
        {
            var prop = response.GetType().GetProperty(property);
            Assert.IsNotNull(prop, "Missing property: " + property);
            return (T)prop.GetValue(response);
        }

        [Test]
        public void IdenticalImagesPassWithZeroPercent()
        {
            var baseline = WritePng("baseline.png", new Color32(20, 40, 60, 255));
            var actual = WritePng("actual.png", new Color32(20, 40, 60, 255));

            var response = ProofHandlers.GoldenCompare(
                new ScenePortRequest("", "{\"baselinePath\":\"" + Escape(baseline) + "\",\"actualPath\":\"" + Escape(actual) + "\"}"),
                ctx);

            Assert.AreEqual("ok", Read<string>(response, "status"));
            Assert.IsTrue(Read<bool>(response, "passed"));
            Assert.AreEqual(0, Read<int>(response, "changedPixels"));
            Assert.AreEqual(0.0, Read<double>(response, "pixelDiffPercent"));
            Assert.IsFalse(string.IsNullOrEmpty(Read<string>(response, "imageBase64")));
        }

        [Test]
        public void SinglePixelDifferenceFailsWithNonEmptyDiff()
        {
            var baseline = WritePng("baseline.png", new Color32(20, 40, 60, 255));
            var actual = WritePng("actual.png", new Color32(20, 40, 60, 255), new Color32(255, 255, 255, 255));

            var response = ProofHandlers.GoldenCompare(
                new ScenePortRequest("", "{\"baselinePath\":\"" + Escape(baseline) + "\",\"actualPath\":\"" + Escape(actual) + "\"}"),
                ctx);

            Assert.IsFalse(Read<bool>(response, "passed"));
            Assert.AreEqual(1, Read<int>(response, "changedPixels"));
            Assert.Greater(Read<double>(response, "pixelDiffPercent"), 0.0);
            Assert.IsFalse(string.IsNullOrEmpty(Read<string>(response, "imageBase64")));
        }

        [Test]
        public void MissingPathReturnsErrorResponse()
        {
            var response = ProofHandlers.GoldenCompare(new ScenePortRequest("", "{}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(response);
        }

        private static string Escape(string path)
        {
            return path.Replace("\\", "\\\\");
        }
    }
}
