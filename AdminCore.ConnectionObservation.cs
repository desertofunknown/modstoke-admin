using System;
using System.Collections.Generic;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly Dictionary<string, ServerConnectionObservation> _serverConnectionObservations
            = new Dictionary<string, ServerConnectionObservation>();

        private sealed class ServerConnectionObservation
        {
            internal DateTime LastAttemptUtc;
            internal DateTime SampleUtc;
            internal DateTime FirstObservedUtc;
            internal string Status = "Not checked";
            internal string NetworkRtt;
            internal string AliasedRtt;
            internal string IncomingBits;
            internal string OutgoingBits;
#if NET11_0_OR_GREATER
            internal OpenACAISonsServerSession Session;
#endif
        }

        private bool CanReadServerConnection(PlayerEntry entry)
        {
#if NET11_0_OR_GREATER
            return _hostActionsAvailable && LoaderSessionHooksAvailable
                && LooksLikeSteamId(CleanSteamId(entry?.SteamId)) && !IsLocalPlayerEntry(entry);
#else
            return false;
#endif
        }

        private void ReadSelectedServerConnection()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Connection check requires a selected player with a resolved Steam64 ID");
                return;
            }
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            if (!_serverConnectionObservations.TryGetValue(id, out var observation))
            {
                observation = new ServerConnectionObservation();
                _serverConnectionObservations.Add(id, observation);
            }
            observation.LastAttemptUtc = DateTime.UtcNow;
            bool read = ObserveServerConnection(entry, steamId, observation);
            string outcome = "Connection check: " + observation.Status;
            ActionResult(read, outcome, outcome);
        }

        private bool ObserveServerConnection(PlayerEntry entry, ulong steamId, ServerConnectionObservation observation)
        {
#if NET11_0_OR_GREATER
            if (!HasHostSession())
            {
                observation.Status = "Unavailable: a player-host world is required";
                return false;
            }
            if (IsLocalPlayerEntry(entry))
            {
                observation.Status = "The local host has no remote connection measurement";
                return false;
            }
            if (!TryResolveServerIdentity(steamId, out var identity, out string failure))
            {
                observation.Status = "Unavailable: " + failure;
                return false;
            }
            if (!OpenACAISonsServerSessions.TryReadConnection(identity.Session, out var snapshot, out var status))
            {
                observation.Status = "Connection read unavailable: " + status;
                return false;
            }
            if (!ReferenceEquals(snapshot.Session, identity.Session) || !HasHostSession()
                || !TryResolveServerIdentity(steamId, out var current, out _)
                || !ReferenceEquals(current.Session, identity.Session))
            {
                observation.Status = "Source identity or session changed during the read";
                return false;
            }

            observation.Session = snapshot.Session;
            observation.SampleUtc = snapshot.ObservedAt.UtcDateTime;
            observation.FirstObservedUtc = snapshot.Session.FirstObservedAt.UtcDateTime;
            observation.NetworkRtt = FormatConnectionRtt(snapshot.NetworkRoundTripSeconds);
            observation.AliasedRtt = FormatConnectionRtt(snapshot.AliasedRoundTripSeconds);
            observation.IncomingBits = FormatConnectionBits(snapshot.BitsPerSecondIn);
            observation.OutgoingBits = FormatConnectionBits(snapshot.BitsPerSecondOut);
            bool valid = IsConnectionRttValid(snapshot.NetworkRoundTripSeconds)
                && IsConnectionRttValid(snapshot.AliasedRoundTripSeconds)
                && snapshot.BitsPerSecondIn >= 0 && snapshot.BitsPerSecondOut >= 0;
            observation.Status = valid ? "Read completed; estimates may still contain initial defaults"
                : "Read completed with unavailable native metrics";
            return valid;
#else
            observation.Status = "Connection reads require the current Modstoke Loader runtime";
            return false;
#endif
        }

        private string GetServerConnectionSampleStatus(ServerConnectionObservation observation)
        {
            if (observation.SampleUtc == DateTime.MinValue) return "No connection observation retained";
#if NET11_0_OR_GREATER
            if (observation.Session == null || !observation.Session.IsCurrent || !LoaderSessionHooksAvailable)
                return "Retained observation; source session ended or tracking became unavailable";
            if (!_hostActionsAvailable) return "Retained observation; host authority is unavailable";
#endif
            return "Retained observation; check again for another read";
        }

        private static bool IsConnectionRttValid(float seconds)
        {
            return !Single.IsNaN(seconds) && !Single.IsInfinity(seconds) && seconds >= 0;
        }

        private static string FormatConnectionRtt(float seconds)
        {
            return IsConnectionRttValid(seconds)
                ? ((double)seconds * 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " ms"
                : "Unavailable (invalid native estimate)";
        }

        private static string FormatConnectionBits(int bits)
        {
            return bits >= 0 ? bits.ToString("N0", CultureInfo.InvariantCulture) + " bits"
                : "Unavailable (negative native counter)";
        }

        private void ClearServerConnectionObservations()
        {
            _serverConnectionObservations.Clear();
        }

        private void InvalidateServerConnectionObservation(string steamId)
        {
#if NET11_0_OR_GREATER
            if (_serverConnectionObservations.TryGetValue(steamId, out var observation)) observation.Session = null;
#endif
        }

#if NET11_0_OR_GREATER
        private int InvalidateEndedConnectionObservations(OpenACAISonsServerSession closedSession, bool releaseAll)
        {
            int cleared = 0;
            foreach (var observation in _serverConnectionObservations.Values)
            {
                if (!ShouldReleaseLoaderSession(observation.Session, closedSession, releaseAll)) continue;
                observation.Session = null;
                cleared++;
            }
            return cleared;
        }
#endif
    }
}
