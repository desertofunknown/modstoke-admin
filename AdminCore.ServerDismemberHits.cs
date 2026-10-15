using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderDismemberHitsSeen;
        private int _loaderDismemberHitsRejected;
        private int _loaderDismemberHitsUnresolved;
        private DateTime _lastLoaderDismemberHitUtc = DateTime.MinValue;
        private string _lastLoaderDismemberHitDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderDismemberHitsSubscribed { get; private set; }
        internal bool LoaderDismemberHitHookInstalled => LoaderDismemberHitsSubscribed
            && OpenACAISonsServerDismemberHits.State.Status == OpenACAISonsDismemberHitHookStatus.Installed;
        internal string LoaderDismemberHitStatus => LoaderDismemberHitsSubscribed
            ? OpenACAISonsServerDismemberHits.State.Status.ToString() : "Not subscribed";

        private void InitializeLoaderDismemberHitIntegration()
        {
            if (LoaderDismemberHitsSubscribed) return;
            OpenACAISonsServerDismemberHits.Receiving += ReceiveLoaderDismemberHit;
            LoaderDismemberHitsSubscribed = true;
        }

        private void StopLoaderDismemberHitIntegration()
        {
            if (!LoaderDismemberHitsSubscribed) return;
            LoaderDismemberHitsSubscribed = false;
            OpenACAISonsServerDismemberHits.Receiving -= ReceiveLoaderDismemberHit;
        }

        private void ReceiveLoaderDismemberHit(OpenACAISonsDismemberHitRequest request)
        {
            if (!LoaderDismemberHitsSubscribed || request == null || request.IsRejected) return;
            _loaderDismemberHitsSeen = IncrementRequestCount(_loaderDismemberHitsSeen);
            _lastLoaderDismemberHitUtc = request.ReceivedAt.UtcDateTime;

            if (!_disallowCheats || !_jailedWorldEventGuard || !HasHostSession())
            {
                _lastLoaderDismemberHitDecision = "Native handling: jail lock is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderDismemberHitDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderDismemberHit("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderDismemberHit("Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderDismemberHit("Source Steam64 ID is unavailable");
                return;
            }
            if (!IsJailedSteamId(steamId))
            {
                _lastLoaderDismemberHitDecision = "Native handling: source player is not jailed";
                return;
            }

            // Payload fields do not establish source identity, actor validity, or actual damage.
            // PartId is a raw part selection, not an item ID; the part position is sender-supplied.
            string descriptor = "DismemberHitEvent TargetNetworkId=" + request.RequestedTargetNetworkId.ToString(CultureInfo.InvariantCulture)
                + ", Damage=" + request.RequestedDamage.ToString(CultureInfo.InvariantCulture)
                + ", PartId=" + request.RequestedPartId.ToString(CultureInfo.InvariantCulture)
                + ", PartPosition=(" + request.RequestedPartPosition.X.ToString(CultureInfo.InvariantCulture)
                + ", " + request.RequestedPartPosition.Y.ToString(CultureInfo.InvariantCulture)
                + ", " + request.RequestedPartPosition.Z.ToString(CultureInfo.InvariantCulture) + ")";
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; DismemberHitEvent dispatch only; requested damage is not actual health loss";

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderDismemberHitDecision(steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                DeferLoaderDismemberHit("Stored role is unresolved");
                RecordLoaderDismemberHitDecision(steamId, descriptor, policy, _lastLoaderDismemberHitDecision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderDismemberHitDecision(steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    DeferLoaderDismemberHit("Stored role value is unsupported");
                    RecordLoaderDismemberHitDecision(steamId, descriptor, policy, _lastLoaderDismemberHitDecision);
                    return;
            }

            string reason = "Jailed player attempted blocked server event: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, "DismemberHitEvent", reason, descriptor, out var learnedNote))
            {
                RecordLoaderDismemberHitDecision(steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderDismemberHit("The rejection window has closed");
                RecordLoaderDismemberHitDecision(steamId, descriptor, policy, _lastLoaderDismemberHitDecision);
                return;
            }

            _loaderDismemberHitsRejected = IncrementRequestCount(_loaderDismemberHitsRejected);
            RecordLoaderDismemberHitDecision(steamId, descriptor, policy,
                "Rejected jailed player's DismemberHitEvent dispatch; no additional punishment");
        }

        private void DeferLoaderDismemberHit(string detail)
        {
            _loaderDismemberHitsUnresolved = IncrementRequestCount(_loaderDismemberHitsUnresolved);
            _lastLoaderDismemberHitDecision = "Native handling: " + detail + "; no Admin jail punishment";
        }

        private void RecordLoaderDismemberHitDecision(string steamId, string descriptor, string policy, string decision)
        {
            _lastLoaderDismemberHitDecision = CleanConfigText(decision, 320);
            RecordGuardEvidence(steamId, "Server", "Loader DismemberHitEvent / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderDismemberHitsSubscribed => false;
        internal bool LoaderDismemberHitHookInstalled => false;
        internal string LoaderDismemberHitStatus => "Unsupported target";
#endif

        private void ResetLoaderDismemberHitEvidence()
        {
            _loaderDismemberHitsSeen = 0;
            _loaderDismemberHitsRejected = 0;
            _loaderDismemberHitsUnresolved = 0;
            _lastLoaderDismemberHitUtc = DateTime.MinValue;
            _lastLoaderDismemberHitDecision = "No request observed";
        }
    }
}
