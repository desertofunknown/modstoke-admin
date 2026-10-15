using System;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private enum MovementMode
        {
            Unknown,
            Foot,
            GolfCart,
            Raft,
            Zipline,
            Glider,
            KnightV,
            Story
        }

        private void ObserveMovementTransition(MovementSignalState state, MovementContext context, DateTime observedAt, DateTime now)
        {
            MovementMode current = context.ObservedMode;
            if (current == MovementMode.Unknown)
            {
                state.ObservedMovementMode = MovementMode.Unknown;
                state.LastMovementContextUtc = DateTime.MinValue;
                return;
            }

            MovementMode previous = state.ObservedMovementMode;
            double interval = (observedAt - state.LastMovementContextUtc).TotalSeconds;
            if (previous != MovementMode.Unknown && interval >= 0.2 && interval <= 10.0 && previous != current)
            {
                string transition = "Observed movement change from " + GetMovementModeLabel(previous) + " to " + GetMovementModeLabel(current);
                float seconds = previous == MovementMode.Story || current == MovementMode.Story
                    || previous == MovementMode.Glider || current == MovementMode.Glider
                    ? Math.Max(3f, _legalTeleportGraceSeconds)
                    : Math.Max(1f, _vehicleTransitionGraceSeconds);
                DateTime deadline = GetSettingsDeadline(now, seconds);
                if (deadline > state.IgnoreUntilUtc)
                {
                    state.IgnoreUntilUtc = deadline;
                    state.IgnoreReason = transition;
                }
                InvalidateMovementSample(state, transition + "; baseline reset");
            }

            state.ObservedMovementMode = current;
            state.LastMovementContextUtc = observedAt;
        }

        private static string GetMovementModeLabel(MovementMode mode)
        {
            switch (mode)
            {
                case MovementMode.Foot: return "foot";
                case MovementMode.GolfCart: return "golf cart";
                case MovementMode.Raft: return "raft";
                case MovementMode.Zipline: return "zipline";
                case MovementMode.Glider: return "glider";
                case MovementMode.KnightV: return "KnightV";
                case MovementMode.Story: return "story/death transition";
                default: return "unknown";
            }
        }
    }
}
