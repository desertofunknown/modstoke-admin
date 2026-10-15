using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderPlayerHitsSeen;
        private int _loaderPlayerHitsRejected;
        private int _loaderPlayerHitsUnresolved;
        private DateTime _lastLoaderPlayerHitUtc = DateTime.MinValue;
        private string _lastLoaderPlayerHitDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderPlayerHitsSubscribed { get; private set; }
        internal bool LoaderPlayerHitHookInstalled => LoaderPlayerHitsSubscribed
            && OpenACAISonsServerPlayerHits.State.Status == OpenACAISonsPlayerHitHookStatus.Installed;
        internal string LoaderPlayerHitStatus => LoaderPlayerHitsSubscribed
            ? OpenACAISonsServerPlayerHits.State.Status.ToString() : "Not subscribed";

        private void InitializeLoaderPlayerHitIntegration()
        {
            if (LoaderPlayerHitsSubscribed) return;
            OpenACAISonsServerPlayerHits.Receiving += ReceiveLoaderPlayerHit;
            LoaderPlayerHitsSubscribed = true;
        }

        private void StopLoaderPlayerHitIntegration()
        {
            if (!LoaderPlayerHitsSubscribed) return;
            LoaderPlayerHitsSubscribed = false;
            OpenACAISonsServerPlayerHits.Receiving -= ReceiveLoaderPlayerHit;
        }

        private void ReceiveLoaderPlayerHit(OpenACAISonsPlayerHitRequest request)
        {
            if (!LoaderPlayerHitsSubscribed || request == null || request.IsRejected) return;
            _loaderPlayerHitsSeen = IncrementRequestCount(_loaderPlayerHitsSeen);
            _lastLoaderPlayerHitUtc = request.ReceivedAt.UtcDateTime;

            if (!_disallowCheats || !_jailedWorldEventGuard || !HasHostSession())
            {
                _lastLoaderPlayerHitDecision = "Native handling: jail lock is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderPlayerHitDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderPlayerHit("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderPlayerHit("Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderPlayerHit("Source Steam64 ID is unavailable");
                return;
            }
            if (!IsJailedSteamId(steamId))
            {
                _lastLoaderPlayerHitDecision = "Native handling: source player is not jailed";
                return;
            }

            // Routing and payload targets describe the request, never the attacking player's identity.
            string descriptor = "HitPlayer routing " + request.RoutingTargets.ToString(CultureInfo.InvariantCulture)
                + "; routed entity " + (request.RoutedEntityNetworkId?.ToString(CultureInfo.InvariantCulture) ?? "unavailable")
                + "; payload target " + request.RequestedTargetNetworkId.ToString(CultureInfo.InvariantCulture)
                + "; requested damage " + request.RequestedDamage.ToString(CultureInfo.InvariantCulture)
                + "; impact " + request.RequestedImpactType.ToString(CultureInfo.InvariantCulture);
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; HitPlayer dispatch only; requested damage is not actual health loss";

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderPlayerHitDecision(steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                DeferLoaderPlayerHit("Stored role is unresolved");
                RecordLoaderPlayerHitDecision(steamId, descriptor, policy, _lastLoaderPlayerHitDecision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderPlayerHitDecision(steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    DeferLoaderPlayerHit("Stored role value is unsupported");
                    RecordLoaderPlayerHitDecision(steamId, descriptor, policy, _lastLoaderPlayerHitDecision);
                    return;
            }

            const string reason = "Jailed player sent a blocked HitPlayer request";
            if (LearnedPolicyAllowsBlockedEvent(steamId, "HitPlayer", reason, descriptor, out var learnedNote))
            {
                RecordLoaderPlayerHitDecision(steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderPlayerHit("The rejection window has closed");
                RecordLoaderPlayerHitDecision(steamId, descriptor, policy, _lastLoaderPlayerHitDecision);
                return;
            }

            _loaderPlayerHitsRejected = IncrementRequestCount(_loaderPlayerHitsRejected);
            // This enforces an existing jail restriction without adding another jail trigger or ban strike.
            RecordLoaderPlayerHitDecision(steamId, descriptor, policy, "Rejected jailed player's HitPlayer dispatch; no additional punishment");
        }

        private void DeferLoaderPlayerHit(string detail)
        {
            _loaderPlayerHitsUnresolved = IncrementRequestCount(_loaderPlayerHitsUnresolved);
            _lastLoaderPlayerHitDecision = "Native handling: " + detail + "; no Admin punishment";
        }

        private void RecordLoaderPlayerHitDecision(string steamId, string descriptor, string policy, string decision)
        {
            _lastLoaderPlayerHitDecision = CleanConfigText(decision, 320);
            RecordGuardEvidence(steamId, "Server", "Loader HitPlayer / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderPlayerHitsSubscribed => false;
        internal bool LoaderPlayerHitHookInstalled => false;
        internal string LoaderPlayerHitStatus => "Unsupported target";
#endif

        private void ResetLoaderPlayerHitEvidence()
        {
            _loaderPlayerHitsSeen = 0;
            _loaderPlayerHitsRejected = 0;
            _loaderPlayerHitsUnresolved = 0;
            _lastLoaderPlayerHitUtc = DateTime.MinValue;
            _lastLoaderPlayerHitDecision = "No request observed";
        }
    }
}
