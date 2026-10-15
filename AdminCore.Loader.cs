using System;
#if NET11_0_OR_GREATER
using System.Collections.Generic;
using OpenACAI.Loader;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
#if NET11_0_OR_GREATER
        private AdminMenuProvider _loaderMenuProvider;
        private bool _loaderMenuRegistered;
        private bool _loaderIntegrationInitialized;
        private long _nextLoaderContentUpdate;

        internal string LoaderIntegrationStatus { get; private set; } = "Unavailable";

        internal void InitializeLoaderIntegration()
        {
            if (_loaderIntegrationInitialized)
            {
                return;
            }

            _loaderIntegrationInitialized = true;
            _loaderMenuProvider = new AdminMenuProvider();
            try
            {
                _loaderMenuRegistered = OpenACAIMenu.TryRegisterProvider(_loaderMenuProvider);
                if (_loaderMenuRegistered)
                {
                    LoaderIntegrationStatus = "Registered";
                    return;
                }

                LoaderIntegrationStatus = "Registration failed";
                _loaderMenuProvider.Stop();
                LogWarning("Admin loader menu registration skipped: provider id is already registered.");
            }
            catch (Exception ex)
            {
                LoaderIntegrationStatus = "Registration failed";
                _loaderMenuProvider.Stop();
                LogWarning("Admin loader menu registration failed: " + ex.Message);
            }
        }

        internal void TickLoaderIntegration()
        {
            if (!_loaderMenuRegistered)
            {
                return;
            }

            string requestedTab = _loaderMenuProvider.TakeRequestedTab();
            if (requestedTab != null)
            {
                _activeTab = requestedTab;
                if (requestedTab == "Debug")
                {
                    _debugActiveTab = "Items";
                }
                _contentScroll = UnityEngine.Vector2.zero;
                if (!_menuOpen)
                {
                    ToggleMenu();
                }
            }

            long now = Environment.TickCount64;
            if (now < _nextLoaderContentUpdate)
            {
                return;
            }

            _nextLoaderContentUpdate = now + 1000;
            _loaderMenuProvider.Publish(new OpenACAIMenuContentSnapshot(
                "Modstoke Admin",
                "by OpenACAI. Open the private admin console to inspect players and use administrative controls.",
                new[]
                {
                    new OpenACAIMenuSectionSnapshot("Admin console", new[]
                    {
                        new OpenACAIMenuRowSnapshot("Console", _menuOpen ? "Open" : "Closed", "", "Open the console with the commands below or the configured menu hotkey."),
                        new OpenACAIMenuRowSnapshot("Guard", GetGuardStatusText(), "", "Readiness reflects the admin tool's current runtime state."),
                        new OpenACAIMenuRowSnapshot("Player roster", "Available in the private console", "", "Player identities and moderation controls are not published to the loader menu.")
                    })
                }));
        }

        internal void StopLoaderIntegration()
        {
            _loaderMenuProvider?.Stop();
            if (!_loaderMenuRegistered)
            {
                return;
            }

            _loaderMenuRegistered = false;
            LoaderIntegrationStatus = "Stopped";
            try
            {
                OpenACAIMenu.TryUnregisterProvider(AdminMenuProvider.ProviderId);
            }
            catch (Exception ex)
            {
                LogWarning("Admin loader menu unregister failed: " + ex.Message);
            }
        }

        private sealed class AdminMenuProvider : IOpenACAIMenuContentProvider, IOpenACAICommandProvider
        {
            internal const string ProviderId = "com.openacai.sotf.admin";
            private readonly object _sync = new object();
            private bool _enabled = true;
            private string _requestedTab;
            private OpenACAIMenuContentSnapshot _content = new OpenACAIMenuContentSnapshot(
                "Modstoke Admin", "by OpenACAI. Waiting for the first admin update.",
                Array.Empty<OpenACAIMenuSectionSnapshot>());

            private static readonly IReadOnlyList<OpenACAICommandSnapshot> Commands = Array.AsReadOnly(new[]
            {
                new OpenACAICommandSnapshot("open-console", "Open Modstoke Admin", "Open the admin overview.", "", true, false),
                new OpenACAICommandSnapshot("open-roster", "Open player roster", "Open the private player roster.", "", true, false),
                new OpenACAICommandSnapshot("open-evidence", "Open player evidence", "Open observations and decisions in the private admin console.", "", true, false),
                new OpenACAICommandSnapshot("open-host-controls", "Open Host controls", "Open the host controls page in the admin console.", "", true, false),
                new OpenACAICommandSnapshot("open-debug-items", "Open Debug items", "Open the debug items page in the admin console.", "", true, false)
            });

            public string Id => ProviderId;
            public string DisplayName => "Modstoke Admin";
            public string Kind => OpenACAIMenuProviderKinds.Tab;
            public int Priority => 200;
            public string Source => "Modstoke Admin by OpenACAI";
            public string Description => "Modstoke Admin by OpenACAI: player roster, host controls, and debug items for Sons of the Forest.";
            public bool RequiresAuthorization => false;
            public bool Enabled { get { lock (_sync) { return _enabled; } } }

            public OpenACAIMenuContentSnapshot CreateContent()
            {
                lock (_sync)
                {
                    return _content;
                }
            }

            public IReadOnlyList<OpenACAICommandSnapshot> GetCommands()
            {
                lock (_sync)
                {
                    return _enabled ? Commands : Array.Empty<OpenACAICommandSnapshot>();
                }
            }

            public bool TryInvokeCommand(string commandId, out OpenACAICommandResultSnapshot result)
            {
                lock (_sync)
                {
                    if (!_enabled)
                    {
                        result = OpenACAICommandResultSnapshot.Failure("The admin console integration has stopped.");
                        return false;
                    }

                    string tab;
                    switch (commandId)
                    {
                        case "open-console": tab = "Overview"; break;
                        case "open-roster": tab = "Players"; break;
                        case "open-evidence": tab = "Evidence"; break;
                        case "open-host-controls": tab = "Admin"; break;
                        case "open-debug-items": tab = "Debug"; break;
                        default:
                            result = OpenACAICommandResultSnapshot.Failure("Unknown admin navigation command.");
                            return false;
                    }

                    _requestedTab = tab;
                    result = OpenACAICommandResultSnapshot.Success("Requested page queued for the next admin update.");
                    return true;
                }
            }

            internal string TakeRequestedTab()
            {
                lock (_sync)
                {
                    string tab = _enabled ? _requestedTab : null;
                    _requestedTab = null;
                    return tab;
                }
            }

            internal void Publish(OpenACAIMenuContentSnapshot content)
            {
                lock (_sync)
                {
                    if (_enabled)
                    {
                        _content = content;
                    }
                }
            }

            internal void Stop()
            {
                lock (_sync)
                {
                    _enabled = false;
                    _requestedTab = null;
                }
            }
        }
#else
        internal string LoaderIntegrationStatus => "Unsupported target";

        internal void InitializeLoaderIntegration() { }
        internal void TickLoaderIntegration() { }
        internal void StopLoaderIntegration() { }
#endif
    }
}
