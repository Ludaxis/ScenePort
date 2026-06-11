using NUnit.Framework;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class SecurityTests
    {
        private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static ScenePortGateResult Eval(
            string method = "POST",
            string host = "127.0.0.1:38987",
            string origin = null,
            string contentType = "application/json",
            bool hasBody = true,
            string path = "/create-game-object",
            string tokenHeader = Token,
            bool tokenRequired = true,
            string expectedToken = Token)
        {
            return ScenePortRequestGate.Evaluate(method, host, origin, contentType, hasBody, path, tokenHeader, tokenRequired, expectedToken);
        }

        [Test]
        public void ValidRequestPasses()
        {
            Assert.IsNull(Eval());
        }

        [Test]
        public void OriginHeaderIsRejected()
        {
            Assert.AreEqual(403, Eval(origin: "http://evil.example").StatusCode);
        }

        [Test]
        public void NonLoopbackHostIsRejected()
        {
            Assert.AreEqual(403, Eval(host: "evil.example:38987").StatusCode);
            Assert.IsNull(Eval(host: "localhost:38987"));
            Assert.IsNull(Eval(host: "127.0.0.1"));
        }

        [Test]
        public void DisallowedMethodsRejected()
        {
            Assert.AreEqual(403, Eval(method: "OPTIONS").StatusCode);
            Assert.AreEqual(405, Eval(method: "DELETE").StatusCode);
            Assert.AreEqual(405, Eval(method: "PUT").StatusCode);
        }

        [Test]
        public void NonJsonPostBodyRejected()
        {
            Assert.AreEqual(415, Eval(contentType: "text/plain").StatusCode);
            Assert.IsNull(Eval(contentType: "application/json; charset=utf-8"));
        }

        [Test]
        public void TokenMatrix()
        {
            var missing = Eval(tokenHeader: null);
            Assert.AreEqual(401, missing.StatusCode);
            StringAssert.Contains("bridge.unauthorized", missing.Body);
            Assert.AreEqual(401, Eval(tokenHeader: "wrong").StatusCode);
            Assert.IsNull(Eval(tokenHeader: Token));
        }

        [Test]
        public void HealthIsExemptFromToken()
        {
            Assert.IsNull(Eval(method: "GET", hasBody: false, path: "/health", tokenHeader: null));
        }

        [Test]
        public void TokenNotRequiredWhenDisabled()
        {
            Assert.IsNull(Eval(tokenHeader: null, tokenRequired: false));
        }

        [Test]
        public void FixedTimeEqualsCorrectness()
        {
            Assert.IsTrue(ScenePortAuth.FixedTimeEquals("abc", "abc"));
            Assert.IsFalse(ScenePortAuth.FixedTimeEquals("abc", "abd"));
            Assert.IsFalse(ScenePortAuth.FixedTimeEquals("abc", "abcd"));
            Assert.IsFalse(ScenePortAuth.FixedTimeEquals(null, "abc"));
        }

        [Test]
        public void GeneratedTokenIsValidHex()
        {
            var token = ScenePortAuth.GenerateToken();
            Assert.AreEqual(64, token.Length);
            Assert.IsTrue(ScenePortAuth.IsValidTokenFormat(token));
            Assert.AreNotEqual(token, ScenePortAuth.GenerateToken());
        }

        [Test]
        public void DiscoveryFileRoundTrips()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScenePortDiscoveryTest_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var token = ScenePortAuth.GenerateToken();
                ScenePortDiscoveryFile.Write(dir, new ScenePortDiscoveryFile.BridgeInfo
                {
                    bridgeVersion = "0.3.0",
                    protocolVersion = ScenePortProtocol.Version,
                    capabilitiesHash = ScenePortProtocol.CapabilitiesHash,
                    url = "http://127.0.0.1:38990",
                    port = 38990,
                    token = token,
                    projectPath = dir,
                    projectId = "abc",
                    projectName = "Test",
                    unityVersion = "2022.3",
                    processId = 1234,
                    processName = "Unity",
                    startedUtc = "now",
                    heartbeatUtc = "now",
                    expiresUtc = "later",
                    ownerLeaseId = "owner",
                    editorRole = "editor",
                });

                Assert.AreEqual(token, ScenePortDiscoveryFile.TryReadToken(dir));
                ScenePortDiscoveryFile.BridgeInfo info;
                Assert.IsTrue(ScenePortDiscoveryFile.TryRead(dir, out info));
                Assert.AreEqual(2, info.schemaVersion);
                Assert.AreEqual("owner", info.ownerLeaseId);

                ScenePortDiscoveryFile.DeleteIfOwner(dir, "other-owner");
                Assert.IsTrue(System.IO.File.Exists(ScenePortDiscoveryFile.PathFor(dir)));
                ScenePortDiscoveryFile.DeleteIfOwner(dir, "owner");
                Assert.IsFalse(System.IO.File.Exists(ScenePortDiscoveryFile.PathFor(dir)));

                Assert.IsNull(ScenePortDiscoveryFile.TryReadToken(dir));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
