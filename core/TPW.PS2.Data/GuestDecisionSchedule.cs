using System;
using System.Collections.Generic;

namespace TPW.PS2.Data
{
    /// <summary>The decoded state0 actions integrated here. Other native arms are
    /// not silently turned into destination picks or invented movement.
    /// Litter and Vomit are 20C930's arms 3 and 4 (findings/staff-handymen-entertainers.md
    /// §5.5): the arm is only DRAWN here; its own test (+0x74 &gt; 89, +0x76 &gt; 92 and rand(4))
    /// and effect belong to the caller, which acts on them only with a staff system attached.
    /// Heckle and Prank are arms 2 and 5 (§5.5, READ in the corpus FUN_0020c930 and MIPS
    /// 0x20CDE4): again only the arm is drawn here; the rand(1000) &lt; 10 roll, the entertainer
    /// search and the happiness &lt; 25 test are the caller's, with staff attached.</summary>
    public enum GuestIdleAction { None, SelectDestination, Move, Litter, Vomit, Heckle, Prank }

    /// <summary>
    /// Post-completion destination-selection gate, in caller-supplied counter units.
    /// This is not the full native AI; cadence is the caller's responsibility and
    /// the original wall-clock rate is not guaranteed to be 25 Hz.
    /// </summary>
    public sealed class GuestDecisionSchedule
    {
        private readonly Func<int> random;
        private readonly Dictionary<int, Gate> gates = new Dictionary<int, Gate>();

        private struct Gate
        {
            public uint Deadline;
            public uint LastTick;
        }

        public GuestDecisionSchedule(Func<int> random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int Count => gates.Count;

        /// <summary>Replace this guest's gate with now + 300 + rand300.</summary>
        public void Completed(int guest, uint now)
        {
            gates[guest] = new Gate
            {
                Deadline = unchecked(now + 300u + Draw(300u)),
                LastTick = now
            };
        }

        /// <summary>
        /// Attempt once per observed counter value. Missing gates permit selection;
        /// repeated checks at the same tick return false without consuming RNG.
        /// A true result does not clear the gate: call Forget only after routing
        /// succeeds, so failed routing retains the original completion deadline.
        /// </summary>
        public bool CanSelect(int guest, uint now) => NextAction(guest, now) == GuestIdleAction.SelectDestination;

        /// <summary>20C930 gates only arm0. Arm1 starts ordinary movement independently
        /// of that deadline; this remains a bounded adapter, not all six native actions.
        /// Missing gates preserve the port's pre-completion selection policy.</summary>
        public GuestIdleAction NextAction(int guest, uint now)
        {
            Gate gate;
            if (!gates.TryGetValue(guest, out gate))
                return GuestIdleAction.SelectDestination;
            if (gate.LastTick == now)
                return GuestIdleAction.None;

            gate.LastTick = now;
            gates[guest] = gate;

            // Both draws occur on every decision attempt, including nonzero arms.
            uint arm = Draw(6u);
            uint extra = Draw(300u);
            uint threshold = unchecked(gate.Deadline + 60u + extra);
            // Deliberately plain unsigned comparison, NOT signed elapsed-time math.
            if (arm == 1u) return GuestIdleAction.Move;
            if (arm == 2u) return GuestIdleAction.Heckle;   // 20C930 arm 2 -> 12E278 when rand(1000) < 10, ent < 5
            if (arm == 3u) return GuestIdleAction.Litter;   // 20C930 arm 3 -> 20D010 when +0x74 > 89
            if (arm == 4u) return GuestIdleAction.Vomit;    // arm 4 -> state 0x1D when +0x76 > 92, rand(4) == 0
            if (arm == 5u) return GuestIdleAction.Prank;    // arm 5 -> 20D010(g, 1) when +0x75 < 25, rand(1000) < 10
            return arm == 0u && now > threshold ? GuestIdleAction.SelectDestination : GuestIdleAction.None;
        }

        public void Forget(int guest)
        {
            gates.Remove(guest);
        }

        public void Reconcile(IEnumerable<int> live)
        {
            if (live == null)
                throw new ArgumentNullException(nameof(live));

            var keep = new HashSet<int>(live);
            var stale = new List<int>();
            foreach (int guest in gates.Keys)
            {
                if (!keep.Contains(guest))
                    stale.Add(guest);
            }
            foreach (int guest in stale)
                gates.Remove(guest);
        }

        private uint Draw(uint modulus)
        {
            return unchecked((uint)random()) % modulus;
        }
    }
}
