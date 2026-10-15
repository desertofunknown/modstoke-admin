using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderIngredientsSeen;
        private int _loaderIngredientsRejected;
        private int _loaderIngredientsUnresolved;
        private int _loaderCompletionsSeen;
        private int _loaderCompletionsRejected;
        private int _loaderCompletionsUnresolved;
        private DateTime _lastLoaderIngredientUtc = DateTime.MinValue;
        private DateTime _lastLoaderCompletionUtc = DateTime.MinValue;
        private string _lastLoaderIngredientDecision = "No request observed";
        private string _lastLoaderCompletionDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderConstructionResourcesSubscribed { get; private set; }
        internal bool LoaderIngredientHookInstalled => LoaderConstructionResourcesSubscribed
            && OpenACAISonsServerConstruction.State.IngredientAdditionStatus == OpenACAISonsConstructionHookStatus.Installed;
        internal bool LoaderCompletionHookInstalled => LoaderConstructionResourcesSubscribed
            && OpenACAISonsServerConstruction.State.CompletionRequestStatus == OpenACAISonsConstructionHookStatus.Installed;
        internal string LoaderIngredientStatus => LoaderConstructionResourcesSubscribed
            ? OpenACAISonsServerConstruction.State.IngredientAdditionStatus.ToString() : "Not subscribed";
        internal string LoaderCompletionStatus => LoaderConstructionResourcesSubscribed
            ? OpenACAISonsServerConstruction.State.CompletionRequestStatus.ToString() : "Not subscribed";

        private void InitializeLoaderConstructionResourceIntegration()
        {
            if (LoaderConstructionResourcesSubscribed) return;
            OpenACAISonsServerConstruction.IngredientAdding += ReceiveLoaderIngredient;
            OpenACAISonsServerConstruction.CompletionRequested += ReceiveLoaderCompletion;
            LoaderConstructionResourcesSubscribed = true;
        }

        private void StopLoaderConstructionResourceIntegration()
        {
            if (!LoaderConstructionResourcesSubscribed) return;
            LoaderConstructionResourcesSubscribed = false;
            OpenACAISonsServerConstruction.IngredientAdding -= ReceiveLoaderIngredient;
            OpenACAISonsServerConstruction.CompletionRequested -= ReceiveLoaderCompletion;
        }

        private void ReceiveLoaderIngredient(OpenACAISonsBlueprintIngredientRequest request)
        {
            if (!LoaderIngredientHookInstalled || request == null || request.IsRejected) return;
            _loaderIngredientsSeen = IncrementRequestCount(_loaderIngredientsSeen);
            _lastLoaderIngredientUtc = request.ReceivedAt.UtcDateTime;
            string diagnostics = "Raw RequestedItemId=" + request.RequestedItemId.ToString(CultureInfo.InvariantCulture)
                + "; raw RoutingTargets=" + request.RoutingTargets.ToString(CultureInfo.InvariantCulture)
                + "; raw RoutedEntityNetworkId=" + request.RoutedEntityNetworkId?.ToString(CultureInfo.InvariantCulture)
                + "; raw RoutedConnectionId=" + request.RoutedConnectionId?.ToString(CultureInfo.InvariantCulture);
            ReceiveLoaderConstructionResource(request, true, "StructureCraftingNodeAddIngredient",
                request.RequestedTargetNetworkId, request.RequestedAmount, diagnostics);
        }

        private void ReceiveLoaderCompletion(OpenACAISonsBlueprintCompletionRequest request)
        {
            if (!LoaderCompletionHookInstalled || request == null || request.IsRejected) return;
            _loaderCompletionsSeen = IncrementRequestCount(_loaderCompletionsSeen);
            _lastLoaderCompletionUtc = request.ReceivedAt.UtcDateTime;
            string diagnostics = "Raw RoutingTargets=" + request.RoutingTargets.ToString(CultureInfo.InvariantCulture)
                + "; raw RoutedEntityNetworkId=" + request.RoutedEntityNetworkId?.ToString(CultureInfo.InvariantCulture)
                + "; raw RoutedConnectionId=" + request.RoutedConnectionId?.ToString(CultureInfo.InvariantCulture);
            ReceiveLoaderConstructionResource(request, false, "StructureCraftingNodeComplete",
                request.RequestedTargetNetworkId, null, diagnostics);
        }

        private void ReceiveLoaderConstructionResource(OpenACAISonsConstructionRequest request, bool ingredient,
            string eventName, ulong targetId, int? amount, string diagnostics)
        {
            if (!_disallowCheats || !_jailedWorldEventGuard || !HasHostSession())
            {
                SetLoaderConstructionResourceDecision(ingredient, "Native handling: jail lock is off or no local host world is available");
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                SetLoaderConstructionResourceDecision(ingredient, "Native handling: server-local request");
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderConstructionResource(ingredient, "Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderConstructionResource(ingredient, "Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderConstructionResource(ingredient, "Source Steam64 ID is unavailable");
                return;
            }
            if (!IsJailedSteamId(steamId))
            {
                SetLoaderConstructionResourceDecision(ingredient, "Native handling: source player is not jailed");
                return;
            }

            // Routing and claimed ingredients do not establish a local receiver, ownership or resource receipt.
            bool described = TryDescribeLoaderConstructionTarget(eventName, "Target", targetId, amount,
                out string descriptor, out string detail);
            string policy = "Jail lock; exact source session; game identity " + identity.IdentitySource
                + "; stored role " + (identity.StoredRole.HasValue ? identity.StoredRole.Value.ToString() : identity.RoleStatus.ToString())
                + "; " + eventName + " routing only; no refund or prediction reset; " + diagnostics;
            if (!described)
                descriptor = eventName + " raw RequestedTargetNetworkId=" + targetId.ToString(CultureInfo.InvariantCulture)
                    + (amount.HasValue ? "; raw RequestedAmount=" + amount.Value.ToString(CultureInfo.InvariantCulture) : "");

            if (IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, "Native handling: configured exemption");
                return;
            }
            if (identity.RoleStatus != OpenACAISonsRoleDataStatus.Present || !identity.StoredRole.HasValue
                || identity.StoredRole.Value == OpenACAISonsGameRole.Unknown)
            {
                string decision = DeferLoaderConstructionResource(ingredient, "Stored role is unresolved");
                RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, decision);
                return;
            }
            switch (identity.StoredRole.Value)
            {
                case OpenACAISonsGameRole.Admin:
                case OpenACAISonsGameRole.Owner:
                    RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, "Native handling: admin/owner exemption");
                    return;
                case OpenACAISonsGameRole.Guest:
                case OpenACAISonsGameRole.Trusted:
                case OpenACAISonsGameRole.Banned:
                    break;
                default:
                    string decision = DeferLoaderConstructionResource(ingredient, "Stored role value is unsupported");
                    RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, decision);
                    return;
            }

            if (!described)
            {
                string decision = DeferLoaderConstructionResource(ingredient, "Legacy target descriptor unavailable: " + detail);
                RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, decision);
                return;
            }

            string reason = "Jailed player attempted blocked server event: " + descriptor;
            if (LearnedPolicyAllowsBlockedEvent(steamId, eventName, reason, descriptor, out var learnedNote))
            {
                RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, "Native handling: learned allowance " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                string decision = DeferLoaderConstructionResource(ingredient, "The rejection window has closed");
                RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy, decision);
                return;
            }

            if (ingredient)
                _loaderIngredientsRejected = IncrementRequestCount(_loaderIngredientsRejected);
            else
                _loaderCompletionsRejected = IncrementRequestCount(_loaderCompletionsRejected);
            RecordLoaderConstructionResourceDecision(ingredient, eventName, steamId, descriptor, policy,
                "Rejected jailed player's " + eventName + " routing; no refund, prediction reset or additional punishment");
        }

        private void SetLoaderConstructionResourceDecision(bool ingredient, string decision)
        {
            if (ingredient)
                _lastLoaderIngredientDecision = CleanConfigText(decision, 320);
            else
                _lastLoaderCompletionDecision = CleanConfigText(decision, 320);
        }

        private string DeferLoaderConstructionResource(bool ingredient, string detail)
        {
            if (ingredient)
                _loaderIngredientsUnresolved = IncrementRequestCount(_loaderIngredientsUnresolved);
            else
                _loaderCompletionsUnresolved = IncrementRequestCount(_loaderCompletionsUnresolved);
            string decision = "Native handling: " + detail + "; no Admin jail punishment";
            SetLoaderConstructionResourceDecision(ingredient, decision);
            return decision;
        }

        private void RecordLoaderConstructionResourceDecision(bool ingredient, string eventName, string steamId,
            string descriptor, string policy, string decision)
        {
            SetLoaderConstructionResourceDecision(ingredient, decision);
            RecordGuardEvidence(steamId, "Construction", "Loader " + eventName + " / exact session", descriptor, policy, decision);
        }
#else
        internal bool LoaderConstructionResourcesSubscribed => false;
        internal bool LoaderIngredientHookInstalled => false;
        internal bool LoaderCompletionHookInstalled => false;
        internal string LoaderIngredientStatus => "Unsupported target";
        internal string LoaderCompletionStatus => "Unsupported target";
#endif

        private void ResetLoaderConstructionResourceEvidence()
        {
            _loaderIngredientsSeen = 0;
            _loaderIngredientsRejected = 0;
            _loaderIngredientsUnresolved = 0;
            _loaderCompletionsSeen = 0;
            _loaderCompletionsRejected = 0;
            _loaderCompletionsUnresolved = 0;
            _lastLoaderIngredientUtc = DateTime.MinValue;
            _lastLoaderCompletionUtc = DateTime.MinValue;
            _lastLoaderIngredientDecision = "No request observed";
            _lastLoaderCompletionDecision = "No request observed";
        }
    }
}
