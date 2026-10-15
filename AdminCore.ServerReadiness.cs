#if NET11_0_OR_GREATER
using System.Collections.Generic;
using OpenACAI.Loader;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
#if NET11_0_OR_GREATER
        private AdminServerPolicyProvider _loaderPolicyProvider;
        private bool _loaderPolicyRegistered;

        internal string LoaderPolicyStatus { get; private set; } = "Waiting";

        private void InitializeLoaderPolicyProvider()
        {
            if (_loaderPolicyProvider != null) return;

            _loaderPolicyProvider = new AdminServerPolicyProvider();
            try
            {
                _loaderPolicyRegistered = OpenACAIServerPolicy.TryRegisterProvider(_loaderPolicyProvider);
                LoaderPolicyStatus = _loaderPolicyRegistered ? "Waiting" : "Limited: provider ID already registered";
            }
            catch (System.Exception ex)
            {
                LoaderPolicyStatus = "Limited: policy registration unavailable";
                LogWarning("Admin server policy registration failed: " + ex.Message);
            }

            if (!_loaderPolicyRegistered) _loaderPolicyProvider.Stop();
        }

        // Called on the main thread; the public provider never reads live game state.
        private void RefreshLoaderPolicySnapshot()
        {
            if (!_loaderPolicyRegistered || !_loaderPolicyProvider.Enabled) return;

            bool guardEnabled = _disallowCheats;
            bool muteRequired = _mutedPlayers.Count > 0;
            bool auditRequired = _clientInventoryAudit;
            bool identityRequired = guardEnabled || muteRequired || auditRequired;
            bool enabled = identityRequired || _blockServerSleepEvents;
            bool hostReady = _worldReady && _networkRunning == true && _networkServer == true;
            bool adminReady = LoaderAdminRequestsSubscribed && LoaderAdminRequestHookInstalled;
            bool identityReady = LoaderSessionHooksAvailable;
            bool legacyRequired = guardEnabled && (_strictItemEventGuard || _jailedWorldEventGuard || _nativeServerValidationGuard);
            bool legacyPresent = _runtimePatchesApplied && _antiCheatPatchCount > 0;
            bool chatRequired = muteRequired || auditRequired;
            bool chatReady = LoaderChatSubscribed && LoaderChatHookInstalled;
            bool playerHitsRequired = guardEnabled && _jailedWorldEventGuard;
            bool playerHitsReady = LoaderPlayerHitsSubscribed && LoaderPlayerHitHookInstalled;
            bool structureHitsRequired = guardEnabled && (_jailedWorldEventGuard || _nativeServerValidationGuard);
            bool structureHitsReady = LoaderStructureHitsSubscribed && LoaderStructureHitHookInstalled;
            bool blueprintsRequired = guardEnabled && (_jailedWorldEventGuard || _nativeServerValidationGuard);
            bool blueprintsReady = LoaderBlueprintHookInstalled;
            bool structureRelocationRequired = guardEnabled && (_jailedWorldEventGuard || _nativeServerValidationGuard);
            bool structureRelocationReady = LoaderStructureRelocationHookInstalled;
            bool constructionResourcesRequired = guardEnabled && _jailedWorldEventGuard;
            bool ingredientReady = LoaderIngredientHookInstalled;
            bool completionReady = LoaderCompletionHookInstalled;
            bool storageRequired = guardEnabled && (_jailedWorldEventGuard || _nativeServerValidationGuard || _strictItemEventGuard);
            bool storageAddReady = LoaderStorageAddHookInstalled;
            bool storageRemoveReady = LoaderStorageRemoveHookInstalled;
            bool vailHitsRequired = guardEnabled && _jailedWorldEventGuard;
            bool vailHitsReady = LoaderVailHitsSubscribed && LoaderVailHitHookInstalled;
            bool dismemberHitsRequired = guardEnabled && _jailedWorldEventGuard;
            bool dismemberHitsReady = LoaderDismemberHitsSubscribed && LoaderDismemberHitHookInstalled;
            bool worldObjectsRequired = guardEnabled && _jailedWorldEventGuard;
            bool containerOpenReady = LoaderContainerOpenHookInstalled;
            bool objectBreakReady = LoaderObjectBreakHookInstalled;
            bool auditReceiverPresent = _inventoryAuditPatchCount > 0;
            bool sdkAuditPresent = LoaderAuditTransportAvailable;
            bool auditPresent = sdkAuditPresent || (chatReady && auditReceiverPresent);
            var requirements = new List<OpenACAIServerPolicyRequirementSnapshot>
            {
                new OpenACAIServerPolicyRequirementSnapshot(
                    "host-world", "Host world", enabled, hostReady,
                    hostReady ? "READY" : "WAITING",
                    !_worldReady ? "Waiting for a local world."
                        : _networkRunning != true ? "Network session is not running."
                        : _networkServer != true ? "Server authority is unavailable."
                        : "Local world and network server signals are available."),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "admin-command", "Native AdminCommand policy", guardEnabled, adminReady,
                    adminReady ? "REGISTERED" : "WAITING",
                    "AdminCommand only. " + LoaderAdminRequestStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "session-identity", "Session identity and lifecycle", identityRequired, identityReady,
                    identityReady ? "REGISTERED" : "WAITING",
                    "Modstoke SDK session tracking and Admin lifecycle subscriber: " + LoaderSessionStatus
                        + ". Tracking describes observed connection lifetimes, not account authentication or verified runtime behavior."),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "legacy-events", "Other server event rules", legacyRequired, false,
                    legacyPresent ? "PARTIAL" : "UNAVAILABLE",
                    legacyPresent ? "Legacy event patches are present; comprehensive event coverage is not established. Native AdminCommand hooks do not cover these rules."
                        : "Legacy event patch coverage is unavailable. Native AdminCommand hooks do not cover these rules."),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "server-chat", "Shared server chat policy", chatRequired, chatReady,
                    chatReady ? "REGISTERED" : "UNAVAILABLE",
                    LoaderChatStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "player-hits", "Jailed player-hit requests", playerHitsRequired, playerHitsReady,
                    playerHitsReady ? "REGISTERED" : "UNAVAILABLE",
                    "Observed HitPlayer requests only, using exact source-session identity for currently jailed players; policy exemptions remain. Direct/local damage and other routes are outside this coverage. " + LoaderPlayerHitStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "structure-hits", "Structure-hit rules", structureHitsRequired, structureHitsReady,
                    structureHitsReady ? "REGISTERED" : "UNAVAILABLE",
                    "StructureHitEvent jail restrictions, payload limits and native range validation use the exact source session; policy exemptions remain. Unavailable validation is not a rejection. Direct damage and undo are outside this coverage. " + LoaderStructureHitStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "blueprint-placement", "Blueprint placement rules", blueprintsRequired, blueprintsReady,
                    blueprintsReady ? "REGISTERED" : "UNAVAILABLE",
                    "StructureCraftingNodeCreate on the server receiver only; jail restrictions and native range validation use the exact source session and resolved world position. No resource proof or rollback. " + LoaderBlueprintStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "structure-relocation", "Structure relocation rules", structureRelocationRequired, structureRelocationReady,
                    structureRelocationReady ? "REGISTERED" : "UNAVAILABLE",
                    "DismantleScrewStructure on the server receiver only; existing jail and 50m position-range rules use the exact source session. The game's own relocation checks still apply. No ownership or relocation receipt. " + LoaderStructureRelocationStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "construction-ingredients", "Jailed ingredient requests", constructionResourcesRequired, ingredientReady,
                    ingredientReady ? "REGISTERED" : "UNAVAILABLE",
                    "StructureCraftingNodeAddIngredient before routing; jailed-source restrictions only, with role and learned exemptions. No range check, additional punishment or inventory refund. " + LoaderIngredientStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "construction-completion", "Jailed completion requests", constructionResourcesRequired, completionReady,
                    completionReady ? "REGISTERED" : "UNAVAILABLE",
                    "StructureCraftingNodeComplete before routing; jailed-source restrictions only, with role and learned exemptions. A request does not prove completed construction or paid resources. " + LoaderCompletionStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "storage-add", "Storage-add rules", storageRequired, storageAddReady,
                    storageAddReady ? "REGISTERED" : "UNAVAILABLE",
                    "ItemStorageAddItemEvent on the server receiver; existing jail, 40m native range and item rules use exact source identity and the consumed item token. No transfer receipt or refund. " + LoaderStorageAddStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "storage-remove", "Storage-remove rules", storageRequired, storageRemoveReady,
                    storageRemoveReady ? "REGISTERED" : "UNAVAILABLE",
                    "ItemStorageRemoveItemEvent on the server receiver; existing jail, 40m native range and item rules include configured story locations. Unavailable range or location reads are not violations. " + LoaderStorageRemoveStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "vail-hits", "Jailed NPC-hit requests", vailHitsRequired, vailHitsReady,
                    vailHitsReady ? "REGISTERED" : "UNAVAILABLE",
                    "Observed VailHitEvent requests only, using exact source-session identity for currently jailed players; policy exemptions remain. Direct damage and undo are outside this coverage. " + LoaderVailHitStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "dismember-hits", "Jailed dismemberment requests", dismemberHitsRequired, dismemberHitsReady,
                    dismemberHitsReady ? "REGISTERED" : "UNAVAILABLE",
                    "Observed DismemberHitEvent requests only, using exact source-session identity for currently jailed players; policy exemptions remain. Direct damage and undo are outside this coverage. " + LoaderDismemberHitStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "container-open", "Jailed container-open requests", worldObjectsRequired, containerOpenReady,
                    containerOpenReady ? "REGISTERED" : "UNAVAILABLE",
                    "StorageContainerOpened callback only, using exact source-session identity for currently jailed players; policy exemptions remain. Direct component calls and prior effects are outside this coverage. " + LoaderContainerOpenStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "object-break", "Jailed object-break requests", worldObjectsRequired, objectBreakReady,
                    objectBreakReady ? "REGISTERED" : "UNAVAILABLE",
                    "BreakableObjectBroken callback only, using exact source-session identity for currently jailed players; policy exemptions remain. Earlier client changes, separate locator-state messages and undo are outside this coverage. " + LoaderObjectBreakStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "sleep-events", "Host sleep event policy", _blockServerSleepEvents, _blockServerSleepEvents && LoaderSleepHookInstalled,
                    !_blockServerSleepEvents ? "OFF" : LoaderSleepHookInstalled ? "REGISTERED" : "UNAVAILABLE",
                    "Optional host-wide Sleep dispatch blocking. This does not cancel active sleep, clear votes or waiting indicators, or cover direct sleeping-manager calls. " + LoaderSleepHookStatus),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "inventory-audit", "Local audit routes", auditRequired, auditPresent,
                    auditPresent ? "REGISTERED" : chatReady || auditReceiverPresent ? "PARTIAL" : "UNAVAILABLE",
                    "Modstoke SDK notice and private command route: " + (sdkAuditPresent ? "registered" : "unavailable")
                        + "; legacy chat receiver: " + (auditReceiverPresent ? "installed" : "unavailable")
                        + ". SDK audits need a compatible Admin client. Peer negotiation does not establish report delivery or inventory accuracy."),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "movement-polling", "Movement polling", false, guardEnabled && _movementGuard,
                    guardEnabled && _movementGuard ? "ENABLED" : "OFF",
                    "Optional polling setting; this is not a native request hook or runtime acceptance result."),
                new OpenACAIServerPolicyRequirementSnapshot(
                    "equipment-polling", "Equipment polling", false, guardEnabled && _passivePlayerStateAudit,
                    guardEnabled && _passivePlayerStateAudit ? "ENABLED" : "OFF",
                    "Optional passive equipment audit setting; this is not a native request hook or runtime acceptance result.")
            };

            bool missingRequired = requirements.Exists(requirement => requirement.Required && !requirement.Satisfied);
            string status = !enabled ? "Off" : !hostReady ? "Waiting" : missingRequired ? "Limited" : "Registered";
            string detail = !enabled ? "Guard, client audit and sleep blocking policies are off, and no players are muted."
                : !hostReady ? "Waiting for a host world."
                : missingRequired ? "One or more required policy routes are unavailable or partial."
                : "Required policy routes report registration.";
            _loaderPolicyProvider.Update(OpenACAIServerPolicySnapshot.Create(
                AdminServerPolicyProvider.ProviderId, "Modstoke Admin", status,
                detail + " Registration and settings do not establish runtime behavior.", requirements));
            LoaderPolicyStatus = status;
        }

        private void StopLoaderPolicyProvider()
        {
            _loaderPolicyProvider?.Stop();
            LoaderPolicyStatus = "Off";
            if (!_loaderPolicyRegistered) return;

            _loaderPolicyRegistered = false;
            try
            {
                OpenACAIServerPolicy.TryUnregisterProvider(AdminServerPolicyProvider.ProviderId);
            }
            catch (System.Exception ex)
            {
                LogWarning("Admin server policy unregister failed: " + ex.Message);
            }
        }

        private sealed class AdminServerPolicyProvider : IOpenACAIServerPolicyProvider
        {
            internal const string ProviderId = "com.openacai.sotf.admin";
            private readonly object _sync = new object();
            private bool _enabled = true;
            private OpenACAIServerPolicySnapshot _snapshot = OpenACAIServerPolicySnapshot.Create(
                ProviderId, "Modstoke Admin", "Waiting", "Waiting for the first main-thread policy snapshot.",
                System.Array.Empty<OpenACAIServerPolicyRequirementSnapshot>());

            public string Id => ProviderId;
            public string DisplayName => "Modstoke Admin";
            public string Source => "Modstoke Admin by OpenACAI";
            public string Description => "Host policy settings and available request routes; registration does not establish runtime behavior.";
            public bool RequiresAuthorization => false;
            public bool Enabled { get { lock (_sync) return _enabled; } }

            public OpenACAIServerPolicySnapshot CreatePolicy()
            {
                lock (_sync) return _snapshot;
            }

            internal void Update(OpenACAIServerPolicySnapshot snapshot)
            {
                lock (_sync)
                {
                    if (_enabled) _snapshot = snapshot;
                }
            }

            internal void Stop()
            {
                lock (_sync) _enabled = false;
            }
        }
#else
        internal string LoaderPolicyStatus => "Unsupported on this runtime";
        private void InitializeLoaderPolicyProvider() { }
        private void RefreshLoaderPolicySnapshot() { }
        private void StopLoaderPolicyProvider() { }
#endif
    }
}
