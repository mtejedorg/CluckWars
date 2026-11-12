using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Deposit diagnostics (Source <c>Base</c>): the exact line text for each peer's view of the bases, each client-side
    /// deposit flush, and each host-side rejection, so one line tells which of the three host checks (allegiance,
    /// attribution, range) failed and where each side thinks the base and the chicken are. Invariant culture; pure, tested.
    /// </summary>
    public static class DepositDiagnostics
    {
        private static string Xz(Vector3 p) => FormattableString.Invariant($"{p.x:0.0},{p.z:0.0}");

        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary><c>Base corner=3 owner=[Player:2] pos=21.8,-21.8 radius=2.5 stamped=1</c></summary>
        public static string BaseLine(int corner, string owner, Vector3 pos, float radius, bool stamped) =>
            FormattableString.Invariant($"Base corner={corner} owner={owner} pos={Xz(pos)} radius={radius:0.00} stamped={(stamped ? 1 : 0)}");

        /// <summary><c>Base flush chicken=P2 home=3 pos=.. base=3 basePos=.. dist=.. radius=.. in=1 amount=..</c></summary>
        public static string FlushLine(string chicken, int home, Vector3 chickenPos, int baseCorner, Vector3 basePos, float radius, float amount)
        {
            float d = PlanarDistance(chickenPos, basePos);
            return FormattableString.Invariant(
                $"Base flush chicken={chicken} home={home} pos={Xz(chickenPos)} base={baseCorner} basePos={Xz(basePos)} dist={d:0.00} radius={radius:0.00} in={(d <= radius ? 1 : 0)} amount={amount:0.00}");
        }

        /// <summary>
        /// One rejection candidate as the HOST sees it: <c>{name pos=.. home=.. allegiance=0|1 attributable=0|1
        /// dist=.. limit=..}</c> (limit = radius + latency margin).
        /// </summary>
        public static string Candidate(string name, Vector3 chickenPos, int home, bool allegiance, bool attributable,
            Vector3 basePos, float radius, float margin)
        {
            float d = PlanarDistance(chickenPos, basePos);
            return FormattableString.Invariant(
                $"{{{name} pos={Xz(chickenPos)} home={home} allegiance={(allegiance ? 1 : 0)} attributable={(attributable ? 1 : 0)} dist={d:0.00} limit={radius + margin:0.00} inRange={(d <= radius + margin ? 1 : 0)}}}");
        }

        /// <summary><c>basePos=.. radius=..</c> prefix of a rejection line.</summary>
        public static string RejectionBase(int corner, Vector3 basePos, float radius) =>
            FormattableString.Invariant($"base={corner} basePos={Xz(basePos)} radius={radius:0.00}");
    }
}
