using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void ApplyLocalMovementDebug()
        {
            if (!IsLocalPlayerInWorld())
            {
                ActionFail("Movement tuning unavailable: join a world first");
                return;
            }
            object character = GetLocalFpCharacterForAdmin();
            if (character == null)
            {
                ActionFail("Movement tuning unavailable: local player character is not ready");
                return;
            }

            float walk;
            float run;
            float swim;
            float jump;
            if (!TryParseLocalDebugFloat(_debugWalkInput, "Walk speed", 0.1f, 80f, out walk)
                || !TryParseLocalDebugFloat(_debugRunInput, "Run speed", 0.1f, 120f, out run)
                || !TryParseLocalDebugFloat(_debugSwimInput, "Swim speed", 0.1f, 80f, out swim)
                || !TryParseLocalDebugFloat(_debugJumpInput, "Jump multiplier", 0f, 30f, out jump))
            {
                return;
            }
            _debugWalkInput = walk.ToString("G9", CultureInfo.InvariantCulture);
            _debugRunInput = run.ToString("G9", CultureInfo.InvariantCulture);
            _debugSwimInput = swim.ToString("G9", CultureInfo.InvariantCulture);
            _debugJumpInput = jump.ToString("G9", CultureInfo.InvariantCulture);

            string walkOutcome;
            string runOutcome;
            string swimOutcome;
            string jumpOutcome;
            bool walkApplied = ApplyLocalMovementValue(character, "SetWalkSpeed", "WalkSpeed", walk, out walkOutcome);
            bool runApplied = ApplyLocalMovementValue(character, "SetRunSpeed", "RunSpeed", run, out runOutcome);
            bool swimApplied = ApplyLocalMovementValue(character, "SetSwimSpeed", "SwimSpeed", swim, out swimOutcome);
            bool jumpApplied = ApplyLocalMovementValue(character, "SetSuperJump", "JumpMultiplier", jump, out jumpOutcome);
            bool applied = walkApplied && runApplied && swimApplied && jumpApplied;
            string outcome = "Movement: " + walkOutcome + "; " + runOutcome + "; " + swimOutcome + "; " + jumpOutcome;
            ActionResult(applied, outcome, outcome);
        }

        private bool ApplyLocalMovementValue(object character, string setter, string property, float requested, out string outcome)
        {
            object result;
            bool sent = InvokeMethod(character, character.GetType(), setter, new object[] { requested }, false, out result)
                && NativeActionAccepted(result);
            object readback;
            bool read = InvokeMethod(character, character.GetType(), "get_" + property, new object[0], false, out readback)
                && readback is float;
            outcome = property + " requested " + requested.ToString("G9", CultureInfo.InvariantCulture);
            if (!sent)
            {
                outcome += ", setter unavailable or failed";
            }
            if (!read)
            {
                outcome += ", actual value unavailable";
                return false;
            }

            float actual = (float)readback;
            outcome += ", actual " + actual.ToString("G9", CultureInfo.InvariantCulture);
            bool matches = IsFiniteDebugValue(actual) && Mathf.Approximately(actual, requested);
            if (!matches)
            {
                outcome += " (does not match request)";
            }
            return sent && matches;
        }

        private void ApplyLocalScaleDebug()
        {
            if (!IsLocalPlayerInWorld())
            {
                ActionFail("Scale unavailable: join a world first");
                return;
            }
            object transform = GetLocalPlayerBaseTransformForAdmin() ?? GetLocalPlayerTransformForAdmin();
            if (transform == null)
            {
                ActionFail("Scale unavailable: local player transform is not ready");
                return;
            }
            float scale;
            if (!TryParseLocalDebugFloat(_debugScaleInput, "Scale", 0.1f, 20f, out scale))
            {
                return;
            }
            _debugScaleInput = scale.ToString("G9", CultureInfo.InvariantCulture);
            Vector3 requested = new Vector3(scale, scale, scale);
            object result;
            bool sent = InvokeMethod(transform, transform.GetType(), "set_localScale", new object[] { requested }, false, out result)
                && NativeActionAccepted(result);
            object readback;
            bool read = InvokeMethod(transform, transform.GetType(), "get_localScale", new object[0], false, out readback)
                && readback is Vector3;
            string outcome = "Local scale requested " + FormatLocalDebugScale(requested);
            if (!sent)
            {
                outcome += "; setter unavailable or failed";
            }
            if (!read)
            {
                ActionFail(outcome + "; actual scale unavailable");
                return;
            }

            Vector3 actual = (Vector3)readback;
            bool matches = IsFiniteDebugValue(actual.x) && IsFiniteDebugValue(actual.y) && IsFiniteDebugValue(actual.z)
                && Mathf.Approximately(actual.x, requested.x)
                && Mathf.Approximately(actual.y, requested.y)
                && Mathf.Approximately(actual.z, requested.z);
            outcome += "; actual " + FormatLocalDebugScale(actual);
            if (!matches)
            {
                outcome += " (does not match request)";
            }
            ActionResult(sent && matches, outcome, outcome);
        }

        private bool TryParseLocalDebugFloat(string text, string label, float minimum, float maximum, out float value)
        {
            if (!Single.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || !IsFiniteDebugValue(value) || value < minimum || value > maximum)
            {
                ActionFail(label + " must be a finite number from " + minimum.ToString(CultureInfo.InvariantCulture)
                    + " to " + maximum.ToString(CultureInfo.InvariantCulture));
                return false;
            }
            return true;
        }

        private static bool IsFiniteDebugValue(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }

        private static string FormatLocalDebugScale(Vector3 value)
        {
            return "(" + value.x.ToString("G9", CultureInfo.InvariantCulture) + ", "
                + value.y.ToString("G9", CultureInfo.InvariantCulture) + ", "
                + value.z.ToString("G9", CultureInfo.InvariantCulture) + ")";
        }
    }
}
