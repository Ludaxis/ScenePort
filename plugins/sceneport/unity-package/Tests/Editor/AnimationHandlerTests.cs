using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class AnimationHandlerTests
    {
        private const string TempFolder = "Assets/ScenePortAnimationTests";

        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 0 };
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        private static ScenePortRequest Body(string json) => new ScenePortRequest("", json);

        [Test]
        public void CreateAnimationClipBakesCurve()
        {
            var path = TempFolder + "/Bob.anim";
            var result = AnimationHandlers.CreateAnimationClip(
                Body("{\"path\":\"" + path + "\",\"curves\":[{\"path\":\"\",\"type\":\"UnityEngine.Transform\",\"property\":\"m_LocalPosition.y\",\"keys\":[{\"time\":0,\"value\":0},{\"time\":1,\"value\":2}]}]}"),
                ctx);

            Assert.IsInstanceOf<AuthoringResponse>(result);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.IsNotNull(clip, "Clip asset should exist after creation.");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            Assert.AreEqual(2, curve.keys.Length);
        }

        [Test]
        public void CreateClipDryRunDoesNotWriteAsset()
        {
            var path = TempFolder + "/DryRun.anim";
            var result = AnimationHandlers.CreateAnimationClip(Body("{\"path\":\"" + path + "\",\"dryRun\":true}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsTrue(((AuthoringResponse)result).DryRun);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
        }

        [Test]
        public void CreateControllerWithTwoStatesAndConditionedTransition()
        {
            var controllerPath = TempFolder + "/Hero.controller";
            var clipPath = TempFolder + "/Run.anim";

            AnimationHandlers.CreateAnimatorController(
                Body("{\"path\":\"" + controllerPath + "\",\"parameters\":[{\"name\":\"Speed\",\"type\":\"float\"}]}"),
                ctx);
            AnimationHandlers.CreateAnimationClip(Body("{\"path\":\"" + clipPath + "\"}"), ctx);

            AnimationHandlers.AddAnimatorState(
                Body("{\"controllerPath\":\"" + controllerPath + "\",\"stateName\":\"Idle\",\"isDefault\":true}"),
                ctx);
            AnimationHandlers.AddAnimatorState(
                Body("{\"controllerPath\":\"" + controllerPath + "\",\"stateName\":\"Run\",\"motionPath\":\"" + clipPath + "\"}"),
                ctx);

            var transitionResult = AnimationHandlers.AddAnimatorTransition(
                Body("{\"controllerPath\":\"" + controllerPath + "\",\"fromState\":\"Idle\",\"toState\":\"Run\",\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"greater\",\"threshold\":0.1}]}"),
                ctx);
            Assert.IsInstanceOf<AuthoringResponse>(transitionResult);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsNotNull(controller);
            Assert.AreEqual(1, controller.parameters.Length);
            Assert.AreEqual("Speed", controller.parameters[0].name);

            var sm = controller.layers[0].stateMachine;
            Assert.AreEqual(2, sm.states.Length);
            Assert.IsNotNull(sm.defaultState);
            Assert.AreEqual("Idle", sm.defaultState.name);

            AnimatorState idle = null;
            for (var i = 0; i < sm.states.Length; i++)
            {
                if (sm.states[i].state.name == "Idle")
                {
                    idle = sm.states[i].state;
                }
            }
            Assert.IsNotNull(idle);
            Assert.AreEqual(1, idle.transitions.Length);
            Assert.AreEqual(1, idle.transitions[0].conditions.Length);
            Assert.AreEqual("Speed", idle.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void AddTransitionUnknownStateReturnsError()
        {
            var controllerPath = TempFolder + "/Bad.controller";
            AnimationHandlers.CreateAnimatorController(Body("{\"path\":\"" + controllerPath + "\"}"), ctx);
            AnimationHandlers.AddAnimatorState(Body("{\"controllerPath\":\"" + controllerPath + "\",\"stateName\":\"Idle\"}"), ctx);

            var result = AnimationHandlers.AddAnimatorTransition(
                Body("{\"controllerPath\":\"" + controllerPath + "\",\"fromState\":\"Idle\",\"toState\":\"Missing\"}"),
                ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void AssignAnimatorAddsComponentAndUndoRestores()
        {
            var controllerPath = TempFolder + "/Assign.controller";
            AnimationHandlers.CreateAnimatorController(Body("{\"path\":\"" + controllerPath + "\"}"), ctx);

            var go = new GameObject("Animated");
            Assert.IsNull(go.GetComponent<Animator>());

            var result = AnimationHandlers.AssignAnimator(
                Body("{\"instanceId\":" + go.GetInstanceID() + ",\"controllerPath\":\"" + controllerPath + "\"}"),
                ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);

            var animator = go.GetComponent<Animator>();
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.runtimeAnimatorController);

            Undo.PerformUndo();
            Assert.IsNull(go.GetComponent<Animator>());
        }

        [Test]
        public void CreateControllerRejectsBadExtension()
        {
            var result = AnimationHandlers.CreateAnimatorController(Body("{\"path\":\"" + TempFolder + "/Wrong.asset\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }
    }
}
