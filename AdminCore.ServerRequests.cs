using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderAdminRequestsSeen;
        private int _loaderAdminRequestsBlocked;
        private int _loaderAdminRequestsDeferred;
        private DateTime _lastLoaderAdminRequestUtc = DateTime.MinValue;
        private string _lastLoaderAdminDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderAdminRequestsSubscribed { get; private set; }
        internal bool LoaderAdminRequestHookInstalled => LoaderAdminRequestsSubscribed
            && OpenACAISonsServerAdminRequests.State.Status == OpenACAISonsAdminRequestHookStatus.Installed;
        internal string LoaderAdminRequestStatus => LoaderAdminRequestsSubscribed
            ? OpenACAISonsServerAdminRequests.State.Status.ToString() : "Not subscribed";

        internal void InitializeLoaderServerIntegration()
        {
            if (LoaderAdminRequestsSubscribed) return;
            OpenACAISonsServerAdminRequests.Receiving += ReceiveLoaderAdminRequest;
            LoaderAdminRequestsSubscribed = true;
            InitializeLoaderSessionIntegration();
            InitializeLoaderChatIntegration();
            InitializeLoaderAuditIntegration();
            InitializeLoaderPlayerHitIntegration();
            InitializeLoaderStructureHitIntegration();
            InitializeLoaderBlueprintIntegration();
            InitializeLoaderStructureRelocationIntegration();
            InitializeLoaderConstructionResourceIntegration();
            InitializeLoaderStorageIntegration();
            InitializeLoaderVailHitIntegration();
            InitializeLoaderDismemberHitIntegration();
            InitializeLoaderWorldObjectIntegration();
            InitializeLoaderPolicyProvider();
        }

        internal void StopLoaderServerIntegration()
        {
            if (LoaderAdminRequestsSubscribed)
            {
                LoaderAdminRequestsSubscribed = false;
                OpenACAISonsServerAdminRequests.Receiving -= ReceiveLoaderAdminRequest;
            }
            StopLoaderPolicyProvider();
            StopLoaderAuditIntegration();
            StopLoaderSessionIntegration();
            StopLoaderChatIntegration();
            StopLoaderPlayerHitIntegration();
            StopLoaderStructureHitIntegration();
            StopLoaderBlueprintIntegration();
            StopLoaderStructureRelocationIntegration();
            StopLoaderConstructionResourceIntegration();
            StopLoaderStorageIntegration();
            StopLoaderVailHitIntegration();
            StopLoaderDismemberHitIntegration();
            StopLoaderWorldObjectIntegration();
            ResetServerSleepPolicy();
            ClearServerItemApprovals();
            ClearServerRoleObservations();
            ClearServerConnectionObservations();
            ResetServerAdmissionObservations();
            ClearModerationNoticeTracking();
        }

        private void ReceiveLoaderAdminRequest(OpenACAISonsAdminRequest request)
        {
            if (!LoaderAdminRequestsSubscribed || request == null || request.IsRejected) return;
            _loaderAdminRequestsSeen = IncrementRequestCount(_loaderAdminRequestsSeen);
            _lastLoaderAdminRequestUtc = request.ReceivedAt.UtcDateTime;

            if (!_disallowCheats || !HasHostSession())
            {
                _lastLoaderAdminDecision = "Native handling: Admin guard is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderAdminDecision = "Native handling: server-local request";
                return;
            }

            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderAdminRequest("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderAdminRequest("Source identity changed during dispatch");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderAdminRequest("Source identity is unavailable");
                return;
            }

            string descriptor = "AdminCommand Command " + CleanConfigText(request.Command, 64)
                + ", Data " + CleanConfigText(request.Data, 160);
            string policy = "Native AdminCommand restrictions; exact source session; game identity "
                + identity.IdentitySource + "; stored role "
                + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString());

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderAdminDecision(steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                DeferLoaderAdminRequest("Stored role is unresolved");
                RecordLoaderAdminDecision(steamId, descriptor, policy, _lastLoaderAdminDecision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderAdminDecision(steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    DeferLoaderAdminRequest("Stored role value is unsupported");
                    RecordLoaderAdminDecision(steamId, descriptor, policy, _lastLoaderAdminDecision);
                    return;
            }

            string reason = "Unauthorized cheat command: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, "AdminCommand", reason, descriptor, out var learnedNote))
            {
                RecordLoaderAdminDecision(steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }

            // Decisions must remain inside this callback. The loader closes the request on return.
            if (!request.Reject())
            {
                DeferLoaderAdminRequest("The rejection window has closed");
                RecordLoaderAdminDecision(steamId, descriptor, policy, _lastLoaderAdminDecision);
                return;
            }
            _loaderAdminRequestsBlocked = IncrementRequestCount(_loaderAdminRequestsBlocked);
            if (LearnedPolicySuppressesSelfAdmin(steamId, "AdminCommand", reason, descriptor, out learnedNote))
            {
                RecordLoaderAdminDecision(steamId, descriptor, policy, "Blocked without punishment: " + learnedNote);
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, "AdminCommand", reason, descriptor, out learnedNote))
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);

            RecordLoaderAdminDecision(steamId, descriptor, policy, "Blocked: " + reason);
            PlayerEntry entry = FindPlayerBySteamId(steamId);
            AnnounceGuardBlock(entry, steamId, "AdminCommand", reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat AdminCommand", false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", null, null);
            }
            LogWarning("Anti-cheat blocked AdminCommand from " + steamId + " (" + reason + ")");
        }

        private void DeferLoaderAdminRequest(string detail)
        {
            _loaderAdminRequestsDeferred = IncrementRequestCount(_loaderAdminRequestsDeferred);
            _lastLoaderAdminDecision = "Native handling: " + detail + "; no Admin punishment";
        }

        private void RecordLoaderAdminDecision(string steamId, string descriptor, string policy, string decision)
        {
            _lastLoaderAdminDecision = CleanConfigText(decision, 320);
            RecordGuardEvidence(steamId, "Server", "Loader AdminCommand / exact session", descriptor,
                policy + "; native permission checks still apply to continued requests", decision);
        }
#else
        internal bool LoaderAdminRequestsSubscribed => false;
        internal bool LoaderAdminRequestHookInstalled => false;
        internal string LoaderAdminRequestStatus => "Unsupported target";
        internal void InitializeLoaderServerIntegration() { }
        internal void StopLoaderServerIntegration() { }
#endif

        private static int IncrementRequestCount(int count) => count < Int32.MaxValue ? count + 1 : count;

        private void ResetLoaderRequestEvidence()
        {
            _loaderAdminRequestsSeen = 0;
            _loaderAdminRequestsBlocked = 0;
            _loaderAdminRequestsDeferred = 0;
            _lastLoaderAdminRequestUtc = DateTime.MinValue;
            _lastLoaderAdminDecision = "No request observed";
        }
    }
}
