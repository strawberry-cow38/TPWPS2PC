using System;
using System.Collections.Generic;

namespace TPW.PS2.Data
{
    /// <summary>The two decoded state0 actions integrated here. Other native arms are
    /// not silently turned into destination picks or invented movement.</summary>
    public enum GuestIdleAction { None, SelectDestination, Move }

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
