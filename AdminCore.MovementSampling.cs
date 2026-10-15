using System;
using System.Globalization;
using UnityEngine;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool TryReadMovementSample(PlayerEntry entry, ref MovementSignalState state,
            out Vector3 position, out DateTime observedAt, out object playerState)
        {
            position = Vector3.zero;
            observedAt = DateTime.UtcNow;
            playerState = null;
            state.LastSampleAttemptUtc = observedAt;
#if NET11_0_OR_GREATER
            if (HasHostSession() && !IsLocalPlayerEntry(entry))
            {
                string steamId = CleanSteamId(entry.SteamId);
                if (!UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                {
                    InvalidateMovementSample(state, "Player Steam64 ID unavailable; baseline reset");
                    return false;
                }
                if (!TryResolveServerIdentity(parsed, out var identity, out string failure))
                {
                    InvalidateMovementSample(state, failure + "; baseline reset");
                    return false;
                }
                if (!ReferenceEquals(state.SampleSession, identity.Session))
                {
                    state = StartMovementLifetime(state);
                    state.SampleSession = identity.Session;
                }
                if (!OpenACAISonsServerWorld.TryReadPlayer(identity.Session, out var pose, out var readStatus))
                {
                    InvalidateMovementSample(state, "SDK position unavailable (" + readStatus + "); baseline reset");
                    return false;
                }
                if (state.SampleNetworkId != pose.NetworkId)
                {
                    state = StartMovementLifetime(state);
                    state.SampleSession = identity.Session;
                    state.SampleNetworkId = pose.NetworkId;
                }
                state.PositionSource = "SDK player pose";
                position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                if (!IsFiniteEventPosition(position))
                {
                    InvalidateMovementSample(state, "Nonfinite SDK position; baseline reset");
                    return false;
                }
                observedAt = pose.ObservedAt.UtcDateTime;
                if (!MovementContextMatchesSample(entry, state)
                    || (playerState = GetNetworkedPlayerState(entry)) == null)
                {
                    InvalidateMovementSample(state, "Matching player context unavailable; baseline reset");
                    return false;
                }
                return true;
            }
            if (state.SampleSession != null) state = StartMovementLifetime(state);
#endif
            if (!TryGetPlayerWorldPosition(entry, out position))
            {
                InvalidateMovementSample(state, "Position unavailable; baseline reset");
                return false;
            }
            state.PositionSource = "Native roster transform";
            playerState = GetNetworkedPlayerState(entry);
            observedAt = DateTime.UtcNow;
            return true;
        }

        private bool MovementContextMatchesSample(PlayerEntry entry, MovementSignalState state)
        {
#if NET11_0_OR_GREATER
            if (state.SampleSession != null)
            {
                if (state.SampleSession.ClosedAt.HasValue) return false;
                object entity = entry.BoltEntity;
                if (!(entity is UnityEngine.Object nativeEntity) || nativeEntity == null
                    || !HasLiveEntityProof(entity, state.SteamId)) return false;
                object id = ReflectionTools.GetMemberValue(entity, "networkId");
                return ReflectionTools.GetMemberValue(id, "PackedValue") is ulong packed
                    && packed == state.SampleNetworkId;
            }
#endif
            return true;
        }

        private bool TryGetSampleMovementContext(PlayerEntry entry, MovementSignalState state, object playerState,
            out MovementContext context)
        {
            context = GetMovementContext(entry, playerState);
            if (MovementContextMatchesSample(entry, state)) return true;
            InvalidateMovementSample(state, "Player context changed during sampling; baseline reset");
            return false;
        }

        private static MovementSignalState StartMovementLifetime(MovementSignalState previous)
        {
            // Grace can be granted before the first sample, including join and spawn transitions.
            return new MovementSignalState
            {
                SteamId = previous.SteamId,
                IgnoreUntilUtc = previous.IgnoreUntilUtc,
                IgnoreReason = previous.IgnoreReason,
                LastSampleAttemptUtc = previous.LastSampleAttemptUtc
            };
        }

        private static void InvalidateMovementSample(MovementSignalState state, string phase)
        {
            state.HasSample = false;
            state.ObservedMovementMode = MovementMode.Unknown;
            state.LastMovementContextUtc = DateTime.MinValue;
            state.LastSampleUtc = DateTime.MinValue;
            state.ConsecutiveViolations = 0;
            state.GliderLikeSamples = 0;
            state.GliderLikeUntilUtc = DateTime.MinValue;
            state.GliderLikeReason = "";
            ResetFootClimbEvidence(state);
            state.LastSamplePhase = phase;
        }

        private static void SetMovementBaseline(MovementSignalState state, Vector3 position, DateTime observedAt, string phase)
        {
            state.LastPosition = position;
            state.LastSampleUtc = observedAt;
            state.HasSample = true;
            state.LastSamplePhase = phase;
        }
    }
}
