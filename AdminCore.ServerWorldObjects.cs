using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderContainerOpensSeen;
        private int _loaderContainerOpensRejected;
        private int _loaderContainerOpensUnresolved;
        private int _loaderObjectBreaksSeen;
        private int _loaderObjectBreaksRejected;
        private int _loaderObjectBreaksUnresolved;
        private DateTime _lastLoaderContainerOpenUtc = DateTime.MinValue;
        private DateTime _lastLoaderObjectBreakUtc = DateTime.MinValue;
        private string _lastLoaderContainerOpenDecision = "No request observed";
        private string _lastLoaderObjectBreakDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderWorldObjectsSubscribed { get; private set; }
        internal bool LoaderContainerOpenHookInstalled => LoaderWorldObjectsSubscribed
            && OpenACAISonsServerWorldObjects.State.OpenStatus == OpenACAISonsWorldObjectHookStatus.Installed;
        internal bool LoaderObjectBreakHookInstalled => LoaderWorldObjectsSubscribed
            && OpenACAISonsServerWorldObjects.State.BreakStatus == OpenACAISonsWorldObjectHookStatus.Installed;
        internal string LoaderContainerOpenStatus => LoaderWorldObjectsSubscribed
            ? OpenACAISonsServerWorldObjects.State.OpenStatus.ToString() : "Not subscribed";
        internal string LoaderObjectBreakStatus => LoaderWorldObjectsSubscribed
            ? OpenACAISonsServerWorldObjects.State.BreakStatus.ToString() : "Not subscribed";

        private void InitializeLoaderWorldObjectIntegration()
        {
            if (LoaderWorldObjectsSubscribed) return;
            OpenACAISonsServerWorldObjects.Receiving += ReceiveLoaderWorldObject;
            LoaderWorldObjectsSubscribed = true;
        }

        private void StopLoaderWorldObjectIntegration()
        {
            if (!LoaderWorldObjectsSubscribed) return;
            LoaderWorldObjectsSubscribed = false;
            OpenACAISonsServerWorldObjects.Receiving -= ReceiveLoaderWorldObject;
        }

        private void ReceiveLoaderWorldObject(OpenACAISonsWorldObjectRequest request)
        {
            if (!LoaderWorldObjectsSubscribed || request == null || request.IsRejected) return;
            bool containerOpen;
            string eventName;
            switch (request.Operation)
            {
                case OpenACAISonsWorldObjectOperation.OpenContainer:
                    containerOpen = true;
                    eventName = "StorageContainerOpened";
                    _loaderContainerOpensSeen = IncrementRequestCount(_loaderContainerOpensSeen);
                    _lastLoaderContainerOpenUtc = request.ReceivedAt.UtcDateTime;
                    break;
                case OpenACAISonsWorldObjectOperation.BreakObject:
                    containerOpen = false;
                    eventName = "BreakableObjectBroken";
                    _loaderObjectBreaksSeen = IncrementRequestCount(_loaderObjectBreaksSeen);
                    _lastLoaderObjectBreakUtc = request.ReceivedAt.UtcDateTime;
                    break;
                default:
                    return;
            }

            if (!_disallowCheats || !_jailedWorldEventGuard || !HasHostSession())
            {
                SetLoaderWorldObjectDecision(containerOpen, "Native handling: jail lock is off or no local host world is available");
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                SetLoaderWorldObjectDecision(containerOpen, "Native handling: server-local request");
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderWorldObject(containerOpen, "Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderWorldObject(containerOpen, "Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderWorldObject(containerOpen, "Source Steam64 ID is unavailable");
                return;
            }
            if (!IsJailedSteamId(steamId))
            {
                SetLoaderWorldObjectDecision(containerOpen, "Native handling: source player is not jailed");
                return;
            }

            // Raw target and signed seed are diagnostics, not actor, item, quantity, range or ownership evidence.
            // Negative container seeds are valid native random selection requests.
            string descriptor = eventName + " TargetNetworkId=" + request.RequestedTargetNetworkId.ToString(CultureInfo.InvariantCulture);
            if (request.RequestedContentsSeed.HasValue)
                descriptor += ", ContentsSeed=" + request.RequestedContentsSeed.Value.ToString(CultureInfo.InvariantCulture);
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; " + eventName + " dispatch only; no rollback of client prediction, separate locator-state messages or direct component effects";

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                string decision = DeferLoaderWorldObject(containerOpen, "Stored role is unresolved");
                RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, decision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    string decision = DeferLoaderWorldObject(containerOpen, "Stored role value is unsupported");
                    RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, decision);
                    return;
            }

            string reason = "Jailed player attempted blocked server event: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, eventName, reason, descriptor, out var learnedNote))
            {
                RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                string decision = DeferLoaderWorldObject(containerOpen, "The rejection window has closed");
                RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy, decision);
                return;
            }

            if (containerOpen)
                _loaderContainerOpensRejected = IncrementRequestCount(_loaderContainerOpensRejected);
            else
                _loaderObjectBreaksRejected = IncrementRequestCount(_loaderObjectBreaksRejected);
            RecordLoaderWorldObjectDecision(containerOpen, eventName, steamId, descriptor, policy,
                "Rejected jailed player's " + eventName + " dispatch; no additional punishment");
        }

        private void SetLoaderWorldObjectDecision(bool containerOpen, string decision)
        {
            if (containerOpen)
                _lastLoaderContainerOpenDecision = CleanConfigText(decision, 320);
            else
                _lastLoaderObjectBreakDecision = CleanConfigText(decision, 320);
        }

        private string DeferLoaderWorldObject(bool containerOpen, string detail)
        {
            if (containerOpen)
                _loaderContainerOpensUnresolved = IncrementRequestCount(_loaderContainerOpensUnresolved);
            else
                _loaderObjectBreaksUnresolved = IncrementRequestCount(_loaderObjectBreaksUnresolved);
            string decision = "Native handling: " + detail + "; no Admin jail punishment";
            SetLoaderWorldObjectDecision(containerOpen, decision);
            return decision;
        }

        private void RecordLoaderWorldObjectDecision(bool containerOpen, string eventName, string steamId,
            string descriptor, string policy, string decision)
        {
            SetLoaderWorldObjectDecision(containerOpen, decision);
            RecordGuardEvidence(steamId, "Server", "Modstoke SDK " + eventName + " / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderWorldObjectsSubscribed => false;
        internal bool LoaderContainerOpenHookInstalled => false;
        internal bool LoaderObjectBreakHookInstalled => false;
        internal string LoaderContainerOpenStatus => "Unsupported target";
        internal string LoaderObjectBreakStatus => "Unsupported target";
#endif

        private void ResetLoaderWorldObjectEvidence()
        {
            _loaderContainerOpensSeen = 0;
            _loaderContainerOpensRejected = 0;
            _loaderContainerOpensUnresolved = 0;
            _loaderObjectBreaksSeen = 0;
            _loaderObjectBreaksRejected = 0;
            _loaderObjectBreaksUnresolved = 0;
            _lastLoaderContainerOpenUtc = DateTime.MinValue;
            _lastLoaderObjectBreakUtc = DateTime.MinValue;
            _lastLoaderContainerOpenDecision = "No request observed";
            _lastLoaderObjectBreakDecision = "No request observed";
        }
    }
}
