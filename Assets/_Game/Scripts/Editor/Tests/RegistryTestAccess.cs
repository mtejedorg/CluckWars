using System.Reflection;
using CluckWars.Gameplay;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// The scene-object registries (<c>ChickenController.ActiveControllers</c> and its siblings) are read-only: a live
    /// object registers itself in <c>Spawned</c>. EditMode tests have no runner, so a test that needs a never-spawned
    /// chicken in the registry reaches the private registration through reflection, here and nowhere else.
    /// </summary>
    internal static class RegistryTestAccess
    {
        public static void Register(ChickenController chicken) => Call("Register", chicken);

        public static void Unregister(ChickenController chicken) => Call("Unregister", chicken);

        private static void Call(string method, ChickenController chicken)
        {
            var m = typeof(ChickenController).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"ChickenController.{method} is gone: the registry's private registration moved");
            m.Invoke(null, new object[] { chicken });
        }
    }
}
