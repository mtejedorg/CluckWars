using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-207: sampling a non-legacy clip outside the Editor needs an Animator on the target, else the
    /// engine warns on every call and poses nothing. The stage used to destroy the model's Animator.
    /// </summary>
    public sealed class MenuStageAnimationRulesTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        [Test]
        public void PrepareForSampling_AddsAnimatorToAModelWithout_ControllerLess()
        {
            _go = new GameObject("model");
            var a = MenuStageAnimationRules.PrepareForSampling(_go);
            Assert.IsNotNull(a);
            Assert.AreSame(a, _go.GetComponent<Animator>());
            Assert.IsTrue(a.enabled, "a disabled Animator does not make sampling legal");
            Assert.IsNull(a.runtimeAnimatorController, "the Animator must not drive anything");
            Assert.IsFalse(a.applyRootMotion);
        }

        [Test]
        public void PrepareForSampling_KeepsAnImportedAnimator_AndReEnablesIt()
        {
            _go = new GameObject("model");
            var imported = _go.AddComponent<Animator>();
            imported.enabled = false;   // what the old SwapModel did before destroying it
            var a = MenuStageAnimationRules.PrepareForSampling(_go);
            Assert.AreSame(imported, a, "the imported Animator (and its Avatar) is reused");
            Assert.IsTrue(a.enabled);
            Assert.AreEqual(1, _go.GetComponents<Animator>().Length);
        }

        [Test]
        public void PrepareForSampling_Twice_StaysASingleAnimator()
        {
            _go = new GameObject("model");
            MenuStageAnimationRules.PrepareForSampling(_go);
            MenuStageAnimationRules.PrepareForSampling(_go);
            Assert.AreEqual(1, _go.GetComponents<Animator>().Length);
        }

        [Test]
        public void MenuChickenStage_NeverStripsTheAnimator()
        {
            // Source guard, in addition to the behavioural tests above: the stage must go through the rule.
            var path = System.IO.Path.Combine(Application.dataPath, "_Game/Scripts/UI/MenuChickenStage.cs");
            var src = System.IO.File.ReadAllText(path);
            StringAssert.Contains("MenuStageAnimationRules.PrepareForSampling(", src);
            StringAssert.DoesNotContain("Object.Destroy(nested)", src);
        }
    }
}
