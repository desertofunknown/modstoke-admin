using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _worldClockStatus = "World clock unavailable";
        private string _worldWeatherStatus = "Season and weather unavailable";

        private void RefreshWorldEnvironmentStatus()
        {
            ResetWorldEnvironmentStatus();
            if (!_worldReady) return;

            Type clockType = ReflectionTools.FindType("Sons.Environment.TimeOfDayHolder");
            int days, hours, minutes, seconds;
            if (TryReadWorldClock(out days, out hours, out minutes, out seconds))
            {
                _worldClockStatus = "Observed day " + days.ToString(CultureInfo.InvariantCulture)
                    + " | Time " + FormatServerTime(hours, minutes, seconds)
                    + " | Clock lock: " + FormatEnvironmentFlag(ReflectionTools.InvokeStaticNoArgMethod(clockType, "IsLocked"));
            }

            Type seasonsType = ReflectionTools.FindType("Sons.Atmosphere.SeasonsManager");
            object seasons = GetLiveEnvironmentComponent(seasonsType, "Instance");
            string season = seasons == null ? "unavailable"
                : FormatEnvironmentEnum(ReflectionTools.GetStaticMemberValue(seasonsType, "ActiveSeason"));
            string seasonLock = FormatEnvironmentFlag(ReflectionTools.GetMemberValue(seasons, "_seasonIsLocked"));
            object weather = GetLiveEnvironmentComponent(ReflectionTools.FindType("TheForest.World.WeatherSystem"), "Instance");
            string raining = FormatEnvironmentFlag(ReflectionTools.GetMemberValue(weather, "IsRaining"));
            string rainType = FormatEnvironmentEnum(ReflectionTools.GetMemberValue(weather, "CurrentRainType"));
            _worldWeatherStatus = "Observed season: " + season + " | Season lock: " + seasonLock
                + " | Raining: " + raining + " | Rain type: " + rainType;
        }

        private void ResetWorldEnvironmentStatus()
        {
            _worldClockStatus = "World clock unavailable";
            _worldWeatherStatus = "Season and weather unavailable";
        }

        private static bool TryReadWorldClock(out int day, out int hour, out int minute, out int second)
        {
            day = hour = minute = second = 0;
            Type clockType = ReflectionTools.FindType("Sons.Environment.TimeOfDayHolder");
            if (GetLiveEnvironmentComponent(clockType, "_instance") == null) return false;
            object time = ReflectionTools.InvokeStaticNoArgMethod(clockType, "GetTimeOfDay");
            object days = ReflectionTools.GetMemberValue(time, "Days");
            object hours = ReflectionTools.GetMemberValue(time, "Hours");
            object minutes = ReflectionTools.GetMemberValue(time, "Minutes");
            object seconds = ReflectionTools.GetMemberValue(time, "Seconds");
            if (!(days is int d) || !(hours is int h) || !(minutes is int m) || !(seconds is int s)
                || h < 0 || h > 23 || m < 0 || m > 59 || s < 0 || s > 59) return false;
            day = d;
            hour = h;
            minute = m;
            second = s;
            return true;
        }

        private static object GetLiveEnvironmentComponent(Type type, string member)
        {
            object instance = ReflectionTools.GetStaticMemberValue(type, member);
            Component component = instance as Component;
            if (component == null) return null;
            try
            {
                GameObject gameObject = component.gameObject;
                return gameObject != null && gameObject.activeInHierarchy ? instance : null;
            }
            catch { return null; }
        }

        private static string FormatEnvironmentFlag(object value)
        {
            return value is bool state ? (state ? "on" : "off") : "unavailable";
        }

        private static string FormatEnvironmentEnum(object value)
        {
            if (value == null || !value.GetType().IsEnum) return "unavailable";
            return Enum.GetName(value.GetType(), value) ?? "unavailable";
        }

        private bool? TryRunSeasonDebugCommand(string methodName, string argument)
        {
            string command = GetDebugCommandName(methodName);
            if (command != "season" && command != "lockseason" && command != "unlockseason") return null;

            Type type = ReflectionTools.FindType("Sons.Atmosphere.SeasonsManager");
            object instance = GetLiveEnvironmentComponent(type, "Instance");
            if (instance == null)
            {
                _lastDebugCommandFailure = "Season manager is not active";
                return false;
            }

            string method = "UnlockSeason";
            object[] args = Array.Empty<object>();
            if (command != "unlockseason")
            {
                Type seasonType = type.GetNestedType("Season", BindingFlags.Public);
                object season;
                if (seasonType == null || !seasonType.IsEnum
                    || !Enum.TryParse(seasonType, argument, true, out season)
                    || !Enum.IsDefined(seasonType, season))
                {
                    _lastDebugCommandFailure = "Choose Spring, Summer, Fall or Winter";
                    return false;
                }
                method = command == "season" ? "SetSeason" : "LockSeason";
                args = new object[] { season };
            }

            object result;
            bool sent = InvokeMethod(instance, type, method, args, false, out result) && NativeActionAccepted(result);
            if (!sent) _lastDebugCommandFailure = "Season command is unavailable or failed";
            _nextDebugToggleReadUtc = DateTime.MinValue;
            return sent;
        }

        private string GetEnvironmentCommandObservation(string command)
        {
            switch (command)
            {
                case "season":
                case "lockseason":
                case "unlockseason":
                case "forcerain":
                    RefreshWorldEnvironmentStatus();
                    return "; " + _worldWeatherStatus;
                case "settimeofday":
                case "locktimeofday":
                case "jumptimeofday":
                case "setcurrentday":
                    RefreshWorldEnvironmentStatus();
                    return "; " + _worldClockStatus;
                default:
                    return "";
            }
        }
    }
}
