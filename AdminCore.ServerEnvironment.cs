using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _serverTimeInput = "12:00";
        private string _serverTimeStatus = "No host clock request recorded.";
        private string _serverWeatherStatus = "No host precipitation request recorded.";

        private void ResetServerEnvironmentRequests()
        {
            _serverTimeStatus = "No host clock request recorded.";
            _serverWeatherStatus = "No host precipitation request recorded.";
        }

        private static bool TryParseServerTime(string text, out int hour, out int minute, out int second)
        {
            hour = minute = second = 0;
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length < 2 || parts.Length > 3
                || parts[0].Length < 1 || parts[0].Length > 2 || parts[1].Length != 2
                || !Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)
                || !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute)
                || hour > 23 || minute > 59) return false;
            return parts.Length == 2 || parts[2].Length == 2
                && Int32.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out second) && second <= 59;
        }

        private void CaptureServerTime()
        {
            int day, hour, minute, second;
            if (!HasHostSession() || !TryReadWorldClock(out day, out hour, out minute, out second))
            {
                ActionFail("Host clock unavailable: the current time could not be read");
                return;
            }
            _serverTimeInput = FormatServerTime(hour, minute, second);
            ActionOk("Copied observed host time " + _serverTimeInput);
        }

        private static string FormatServerTime(int hour, int minute, int second)
        {
            return hour.ToString("00", CultureInfo.InvariantCulture) + ":"
                + minute.ToString("00", CultureInfo.InvariantCulture) + ":"
                + second.ToString("00", CultureInfo.InvariantCulture);
        }

        private void SetServerTime()
        {
            int hour, minute, second;
            if (!TryParseServerTime(_serverTimeInput, out hour, out minute, out second))
            {
                ReportServerTimeResult(false, "Enter a 24-hour time as HH:mm or HH:mm:ss, from 00:00:00 to 23:59:59.");
                return;
            }
            if (!HasHostSession())
            {
                ReportServerTimeResult(false, "Clock adjustment requires the multiplayer host in a loaded world.");
                return;
            }
#if NET11_0_OR_GREATER
            string requested = FormatServerTime(hour, minute, second);
            _serverTimeInput = requested;
            OpenACAISonsServerTimeStatus status;
            try { status = OpenACAISonsServerTime.SetTimeOfDay(hour, minute, second); }
            catch (Exception ex)
            {
                LogWarning("Host clock request failed: " + ex.GetType().Name);
                ReportServerTimeResult(false, "Clock request for " + requested + " has an unknown outcome. No retry was made.");
                RefreshWorldEnvironmentStatus();
                return;
            }
            string detail;
            switch (status)
            {
                case OpenACAISonsServerTimeStatus.Applied:
                    detail = "Native clock accepted " + requested + "; client synchronization is unconfirmed."; break;
                case OpenACAISonsServerTimeStatus.ApplyUncertain:
                    detail = "Clock request for " + requested + " has an uncertain outcome; some receivers may have changed. No retry was made."; break;
                case OpenACAISonsServerTimeStatus.Locked:
                    detail = "Clock is locked. The existing lock was left in place."; break;
                case OpenACAISonsServerTimeStatus.Busy:
                    detail = "Clock is busy with an override, hold, transition or another adjustment."; break;
                case OpenACAISonsServerTimeStatus.ReplicationUnavailable:
                    detail = "Clock adjustment unavailable: no unambiguous owned time publisher is registered."; break;
                case OpenACAISonsServerTimeStatus.ClockUnavailable:
                    detail = "The game clock is not active."; break;
                case OpenACAISonsServerTimeStatus.NotServer:
                    detail = "Host authority is no longer available."; break;
                case OpenACAISonsServerTimeStatus.InvalidTime:
                    detail = "The clock service rejected the time values."; break;
                case OpenACAISonsServerTimeStatus.Rejected:
                    detail = "The native clock setter did not accept the adjustment."; break;
                case OpenACAISonsServerTimeStatus.Unavailable:
                    detail = "The loader's clock service is not ready."; break;
                case OpenACAISonsServerTimeStatus.WrongThread:
                    detail = "Clock adjustment must run on the game's thread."; break;
                case OpenACAISonsServerTimeStatus.Failed:
                    detail = "Clock preflight failed before the native setter was called."; break;
                default:
                    detail = "Clock service returned an unrecognized result; the outcome is unknown."; break;
            }
            ReportServerTimeResult(status == OpenACAISonsServerTimeStatus.Applied, detail);
            RefreshWorldEnvironmentStatus();
#else
            ReportServerTimeResult(false, "Host clock controls require the net11 Modstoke SDK.");
#endif
        }

        private void SetServerPrecipitation(int strength)
        {
            if (strength < 0 || strength > 3)
            {
                ReportServerWeatherResult(false, "Choose Stop, Light, Medium or Heavy precipitation.");
                return;
            }
            if (!HasHostSession())
            {
                ReportServerWeatherResult(false, "Precipitation changes require the multiplayer host in a loaded world.");
                return;
            }
#if NET11_0_OR_GREATER
            OpenACAISonsServerWeatherStatus status;
            try { status = OpenACAISonsServerWeather.RequestPrecipitation((OpenACAISonsPrecipitation)strength); }
            catch (Exception ex)
            {
                LogWarning("Host precipitation request failed: " + ex.GetType().Name);
                ReportServerWeatherResult(false, "Precipitation request has an unknown outcome. No retry was made.");
                RefreshWorldEnvironmentStatus();
                return;
            }
            string requested = strength == 0 ? "Stop" : strength == 1 ? "Light" : strength == 2 ? "Medium" : "Heavy";
            string detail;
            switch (status)
            {
                case OpenACAISonsServerWeatherStatus.Requested:
                    detail = requested + " precipitation requested; the transition and client delivery are unconfirmed."; break;
                case OpenACAISonsServerWeatherStatus.RequestUncertain:
                    detail = requested + " precipitation has an uncertain outcome; weather may already have changed. No retry was made."; break;
                case OpenACAISonsServerWeatherStatus.Blocked:
                    detail = "The game currently blocks precipitation. Stop remains available."; break;
                case OpenACAISonsServerWeatherStatus.ReplicationUnavailable:
                    detail = "Precipitation unavailable: no unambiguous owned weather publisher is registered."; break;
                case OpenACAISonsServerWeatherStatus.WeatherUnavailable:
                    detail = "The required weather systems are not active."; break;
                case OpenACAISonsServerWeatherStatus.NotServer:
                    detail = "Host authority is no longer available."; break;
                case OpenACAISonsServerWeatherStatus.InvalidPrecipitation:
                    detail = "The weather service rejected the precipitation strength."; break;
                case OpenACAISonsServerWeatherStatus.Busy:
                    detail = "Another precipitation request is in progress."; break;
                case OpenACAISonsServerWeatherStatus.Unavailable:
                    detail = "The loader's weather service is not ready."; break;
                case OpenACAISonsServerWeatherStatus.WrongThread:
                    detail = "Precipitation changes must run on the game's thread."; break;
                case OpenACAISonsServerWeatherStatus.Failed:
                    detail = "Weather preflight failed before the native request was called."; break;
                default:
                    detail = "Weather service returned an unrecognized result; the outcome is unknown."; break;
            }
            ReportServerWeatherResult(status == OpenACAISonsServerWeatherStatus.Requested, detail);
            RefreshWorldEnvironmentStatus();
#else
            ReportServerWeatherResult(false, "Host precipitation controls require the net11 Modstoke SDK.");
#endif
        }

        private void ReportServerTimeResult(bool accepted, string detail)
        {
            _serverTimeStatus = "Last clock request (" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "): " + detail;
            ActionResult(accepted, detail, detail);
        }

        private void ReportServerWeatherResult(bool accepted, string detail)
        {
            _serverWeatherStatus = "Last precipitation request (" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "): " + detail;
            ActionResult(accepted, detail, detail);
        }
    }
}
