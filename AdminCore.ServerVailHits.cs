using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderVailHitsSeen;
        private int _loaderVailHitsRejected;
        private int _loaderVailHitsUnresolved;
        private DateTime _lastLoaderVailHitUtc = DateTime.MinValue;
        private string _lastLoaderVailHitDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderVailHitsSubscribed { get; private set; }
        internal bool LoaderVailHitHookInstalled => LoaderVailHitsSubscribed
            && OpenACAISonsServerVailHits.State.Status == OpenACAISonsVailHitHookStatus.Installed;
        internal string LoaderVailHitStatus => LoaderVailHitsSubscribed
            ? OpenACAISonsServerVailHits.State.Status.ToString() : "Not subscribed";

        private void InitializeLoaderVailHitIntegration()
        {
            if (LoaderVailHitsSubscribed) return;
            OpenACAISonsServerVailHits.Receiving += ReceiveLoaderVailHit;
            LoaderVailHitsSubscribed = true;
        }

        private void StopLoaderVailHitIntegration()
        {
            if (!LoaderVailHitsSubscribed) return;
            LoaderVailHitsSubscribed = false;
            OpenACAISonsServerVailHits.Receiving -= ReceiveLoaderVailHit;
        }

        private void ReceiveLoaderVailHit(OpenACAISonsVailHitRequest request)
        {
            if (!LoaderVailHitsSubscribed || request == null || request.IsRejected) return;
            _loaderVailHitsSeen = IncrementRequestCount(_loaderVailHitsSeen);
            _lastLoaderVailHitUtc = request.ReceivedAt.UtcDateTime;

            if (!_disallowCheats || !_jailedWorldEventGuard || !HasHostSession())
            {
                _lastLoaderVailHitDecision = "Native handling: jail lock is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderVailHitDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderVailHit("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderVailHit("Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderVailHit("Source Steam64 ID is unavailable");
                return;
            }
            if (!IsJailedSteamId(steamId))
            {
                _lastLoaderVailHitDecision = "Native handling: source player is not jailed";
                return;
            }

            // DamageNodeId belongs to the requested target; it is not an item or sender ID.
            string descriptor = "VailHitEvent TargetNetworkId=" + request.RequestedTargetNetworkId.ToString(CultureInfo.InvariantCulture)
                + ", Damage=" + request.RequestedDamage.ToString(CultureInfo.InvariantCulture)
                + ", DamageNodeId=" + request.RequestedDamageNodeId.ToString(CultureInfo.InvariantCulture)
                + ", ImpactType=" + request.RequestedImpactType.ToString(CultureInfo.InvariantCulture)
                + ", StatusType=" + request.RequestedStatusType.ToString(CultureInfo.InvariantCulture)
                + ", StatusDuration=" + request.RequestedStatusDuration.ToString(CultureInfo.InvariantCulture);
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; VailHitEvent dispatch only; requested damage is not actual health loss";

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderVailHitDecision(steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                DeferLoaderVailHit("Stored role is unresolved");
                RecordLoaderVailHitDecision(steamId, descriptor, policy, _lastLoaderVailHitDecision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderVailHitDecision(steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    DeferLoaderVailHit("Stored role value is unsupported");
                    RecordLoaderVailHitDecision(steamId, descriptor, policy, _lastLoaderVailHitDecision);
                    return;
            }

            string reason = "Jailed player attempted blocked server event: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, "VailHitEvent", reason, descriptor, out var learnedNote))
            {
                RecordLoaderVailHitDecision(steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderVailHit("The rejection window has closed");
                RecordLoaderVailHitDecision(steamId, descriptor, policy, _lastLoaderVailHitDecision);
                return;
            }

            _loaderVailHitsRejected = IncrementRequestCount(_loaderVailHitsRejected);
            RecordLoaderVailHitDecision(steamId, descriptor, policy,
                "Rejected jailed player's VailHitEvent dispatch; no additional punishment");
        }

        private void DeferLoaderVailHit(string detail)
        {
            _loaderVailHitsUnresolved = IncrementRequestCount(_loaderVailHitsUnresolved);
            _lastLoaderVailHitDecision = "Native handling: " + detail + "; no Admin jail punishment";
        }

        private void RecordLoaderVailHitDecision(string steamId, string descriptor, string policy, string decision)
        {
            _lastLoaderVailHitDecision = CleanConfigText(decision, 320);
            RecordGuardEvidence(steamId, "Server", "Loader VailHitEvent / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderVailHitsSubscribed => false;
        internal bool LoaderVailHitHookInstalled => false;
        internal string LoaderVailHitStatus => "Unsupported target";
#endif

        private void ResetLoaderVailHitEvidence()
        {
            _loaderVailHitsSeen = 0;
            _loaderVailHitsRejected = 0;
            _loaderVailHitsUnresolved = 0;
            _lastLoaderVailHitUtc = DateTime.MinValue;
            _lastLoaderVailHitDecision = "No request observed";
        }
    }
}
