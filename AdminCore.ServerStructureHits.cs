using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderStructureHitsSeen;
        private int _loaderStructureHitsRejected;
        private int _loaderStructureHitsUnresolved;
        private DateTime _lastLoaderStructureHitUtc = DateTime.MinValue;
        private string _lastLoaderStructureHitDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderStructureHitsSubscribed { get; private set; }
        internal bool LoaderStructureHitHookInstalled => LoaderStructureHitsSubscribed
            && OpenACAISonsServerStructureHits.State.Status == OpenACAISonsStructureHitHookStatus.Installed;
        internal string LoaderStructureHitStatus => LoaderStructureHitsSubscribed
            ? OpenACAISonsServerStructureHits.State.Status.ToString() : "Not subscribed";

        private void InitializeLoaderStructureHitIntegration()
        {
            if (LoaderStructureHitsSubscribed) return;
            OpenACAISonsServerStructureHits.Receiving += ReceiveLoaderStructureHit;
            LoaderStructureHitsSubscribed = true;
        }

        private void StopLoaderStructureHitIntegration()
        {
            if (!LoaderStructureHitsSubscribed) return;
            LoaderStructureHitsSubscribed = false;
            OpenACAISonsServerStructureHits.Receiving -= ReceiveLoaderStructureHit;
        }

        private void ReceiveLoaderStructureHit(OpenACAISonsStructureHitRequest request)
        {
            if (!LoaderStructureHitsSubscribed || request == null || request.IsRejected) return;
            _loaderStructureHitsSeen = IncrementRequestCount(_loaderStructureHitsSeen);
            _lastLoaderStructureHitUtc = request.ReceivedAt.UtcDateTime;
            bool unresolved = false;

            if (!_disallowCheats || !HasHostSession() || (!_jailedWorldEventGuard && !_nativeServerValidationGuard))
            {
                _lastLoaderStructureHitDecision = "Native handling: structure-hit rules are off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderStructureHitDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderStructureHit("Source identity " + readStatus, ref unresolved);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderStructureHit("Source identity and registration disagree", ref unresolved);
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderStructureHit("Source Steam64 ID is unavailable", ref unresolved);
                return;
            }

            if (_jailedWorldEventGuard && IsJailedSteamId(steamId)
                && ApplyLoaderStructureHitJailRule(request, identity, steamId, ref unresolved)) return;
            if (_nativeServerValidationGuard)
            {
                if (LoaderStructureHitHookInstalled)
                    ValidateLoaderStructureHit(request, identity, steamId, ref unresolved);
                else
                    _lastLoaderStructureHitDecision = "Shared hook registration is uncertain; native validation remains with the private event hook";
            }
            else if (!_jailedWorldEventGuard || !IsJailedSteamId(steamId))
                _lastLoaderStructureHitDecision = "Native handling: source player is not jailed; native validation is disabled";
        }

        // A jail allowance still leaves the separate payload and range rules in effect.
        private bool ApplyLoaderStructureHitJailRule(OpenACAISonsStructureHitRequest request,
            OpenACAISonsServerIdentitySnapshot identity, string steamId, ref bool unresolved)
        {
            // These fields describe the requested impact, not the sender or a validated structure.
            string descriptor = "StructureHitEvent TargetNetworkId=" + request.RequestedTargetNetworkId.ToString(CultureInfo.InvariantCulture)
                + ", Radius=" + request.RequestedRadius.ToString(CultureInfo.InvariantCulture)
                + ", Damage=" + request.RequestedDamage.ToString(CultureInfo.InvariantCulture)
                + ", Center=(" + request.RequestedCenter.X.ToString(CultureInfo.InvariantCulture)
                + ", " + request.RequestedCenter.Y.ToString(CultureInfo.InvariantCulture)
                + ", " + request.RequestedCenter.Z.ToString(CultureInfo.InvariantCulture)
                + "), ImpactType=" + request.RequestedImpactType.ToString(CultureInfo.InvariantCulture);
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; StructureHitEvent dispatch only; requested damage is not actual health loss";

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderStructureHitDecision(steamId, descriptor, policy, "Native handling: configured exemption");
                return false;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                DeferLoaderStructureHit("Stored role is unresolved", ref unresolved);
                RecordLoaderStructureHitDecision(steamId, descriptor, policy, _lastLoaderStructureHitDecision);
                return false;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderStructureHitDecision(steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return false;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    DeferLoaderStructureHit("Stored role value is unsupported", ref unresolved);
                    RecordLoaderStructureHitDecision(steamId, descriptor, policy, _lastLoaderStructureHitDecision);
                    return false;
            }

            string reason = "Jailed player attempted blocked server event: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, "StructureHitEvent", reason, descriptor, out var learnedNote))
            {
                RecordLoaderStructureHitDecision(steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return false;
            }
            if (!request.Reject())
            {
                DeferLoaderStructureHit("The rejection window has closed", ref unresolved);
                RecordLoaderStructureHitDecision(steamId, descriptor, policy, _lastLoaderStructureHitDecision);
                return true;
            }

            _loaderStructureHitsRejected = IncrementRequestCount(_loaderStructureHitsRejected);
            RecordLoaderStructureHitDecision(steamId, descriptor, policy,
                "Rejected jailed player's StructureHitEvent dispatch; no additional punishment");
            return true;
        }

        private void DeferLoaderStructureHit(string detail, ref bool unresolved)
        {
            if (!unresolved) _loaderStructureHitsUnresolved = IncrementRequestCount(_loaderStructureHitsUnresolved);
            unresolved = true;
            _lastLoaderStructureHitDecision = "Native handling: " + detail + "; no Admin punishment";
        }

        private void RecordLoaderStructureHitDecision(string steamId, string descriptor, string policy, string decision)
        {
            _lastLoaderStructureHitDecision = CleanConfigText(decision, 320);
            RecordGuardEvidence(steamId, "Server", "Loader StructureHitEvent / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderStructureHitsSubscribed => false;
        internal bool LoaderStructureHitHookInstalled => false;
        internal string LoaderStructureHitStatus => "Unsupported target";
#endif

        private void ResetLoaderStructureHitEvidence()
        {
            _loaderStructureHitsSeen = 0;
            _loaderStructureHitsRejected = 0;
            _loaderStructureHitsUnresolved = 0;
            _lastLoaderStructureHitUtc = DateTime.MinValue;
            _lastLoaderStructureHitDecision = "No request observed";
        }
    }
}
