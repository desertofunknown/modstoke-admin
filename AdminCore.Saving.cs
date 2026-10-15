using System;
using System.Globalization;
using System.Reflection;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void ForceSave()
        {
            if (!HasWorldAuthority())
            {
                ReportSaveRequest(false, "World save unavailable: a loaded single-player world or multiplayer host is required.");
                return;
            }
            RequestSelectedSlotSave(false);
        }

        private void RequestPlayerSave()
        {
            if (!IsLocalPlayerInWorld())
            {
                ReportSaveRequest(false, "Player save unavailable: the local player is not in a loaded world.");
                return;
            }
            RequestSelectedSlotSave(true);
        }

        private void RequestSelectedSlotSave(bool playerSave)
        {
            string label = playerSave ? "Player save" : "World save";
            Type saveManager = ReflectionTools.FindType("Sons.Save.SaveGameManager");
            Type saveType = ReflectionTools.FindType("Sons.Save.SaveGameType");
            Type setupManager = ReflectionTools.FindType("Sons.Gameplay.GameSetup.GameSetupManager");
            Type gameState = ReflectionTools.FindType("Sons.Save.GameState");
            if (saveManager == null || saveType == null || !saveType.IsEnum || setupManager == null || gameState == null)
            {
                ReportSaveRequest(false, label + " unavailable: required save metadata types could not be resolved.");
                return;
            }

            object result;
            if (!InvokeMethod(null, saveManager, "LoadInProgress", new object[0], true, out result) || !(result is bool))
            {
                ReportSaveRequest(false, label + " unavailable: loading state could not be read.");
                return;
            }
            if ((bool)result)
            {
                ReportSaveRequest(false, label + " unavailable while a save is loading.");
                return;
            }

            if (!InvokeMethod(null, setupManager, "GetSelectedSaveId", new object[0], true, out result) || !(result is uint))
            {
                ReportSaveRequest(false, label + " unavailable: the selected save slot could not be read.");
                return;
            }
            uint selectedId = (uint)result;
            if (selectedId == 0 || selectedId > Int32.MaxValue)
            {
                ReportSaveRequest(false, label + " unavailable: no supported nonzero selected save slot was observed.");
                return;
            }

            object selectedType;
            if (!InvokeMethod(null, setupManager, "GetSaveGameType", new object[0], true, out selectedType)
                || selectedType == null || selectedType.GetType() != saveType)
            {
                ReportSaveRequest(false, label + " unavailable: the selected save type could not be read.");
                return;
            }

            object[] managerArgs = { null, "OpenACAI save request" };
            if (!InvokeMethod(null, gameState, "TryGetManager", managerArgs, true, out result)
                || !(result is bool) || !(bool)result || managerArgs[0] == null || !gameState.IsInstanceOfType(managerArgs[0]))
            {
                ReportSaveRequest(false, label + " unavailable: the current game-state manager could not be read.");
                return;
            }
            if (!InvokeMethod(managerArgs[0], gameState, "GetGameName", new object[0], false, out result)
                || !(result is string) || String.IsNullOrWhiteSpace((string)result))
            {
                ReportSaveRequest(false, label + " unavailable: the existing game name could not be read.");
                return;
            }
            string gameName = (string)result;

            bool? running = ReadNetworkFlag("isRunning");
            bool? server = running == true ? ReadNetworkFlag("isServer") : null;
            bool matchesSession = playerSave
                ? IsLocalPlayerInWorld() && running == true && server == false
                    && selectedType.Equals(Enum.ToObject(saveType, 2))
                : HasWorldAuthority() && ((running == false && selectedType.Equals(Enum.ToObject(saveType, 0)))
                    || (running == true && server == true && selectedType.Equals(Enum.ToObject(saveType, 1))));
            if (!matchesSession)
            {
                ReportSaveRequest(false, label + (playerSave
                    ? " unavailable: a live multiplayer client with a MultiplayerClient save slot is required."
                    : " unavailable: the selected save type does not match a confirmed single-player or host session."));
                return;
            }

            bool invocationStarted = false;
            try
            {
                MethodInfo save = saveManager.GetMethod("Save", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new Type[] { saveType, typeof(string), typeof(int) }, null);
                if (save == null || save.ReturnType != typeof(void))
                {
                    ReportSaveRequest(false, label + " unavailable: the supported selected-slot Save method could not be resolved.");
                    return;
                }
                invocationStarted = true;
                save.Invoke(null, new object[] { selectedType, gameName, (int)selectedId });
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                string outcome = invocationStarted ? " outcome unknown after invoking native Save: " : " request failed before invocation: ";
                LogWarning(label + outcome + cause.Message);
                ReportSaveRequest(false, label + outcome + cause.Message);
                return;
            }

            ReportSaveRequest(true, label + " requested for selected slot " + selectedId.ToString(CultureInfo.InvariantCulture)
                + " (" + selectedType + "), game " + CleanConfigText(gameName, 96) + ". Disk completion is unconfirmed.");
        }
    }
}
