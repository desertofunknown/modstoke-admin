using System;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool? ValidateWorldCommandArgument(string command, ref string argument)
        {
            int minimumParts;
            int maximumParts;
            int radiusIndex = 0;
            string grammar;
            switch (command)
            {
                case "creepyvillage":
                case "diggingclear":
                    if (!String.IsNullOrWhiteSpace(argument))
                    {
                        ActionFail(command + " takes no arguments");
                        return false;
                    }
                    argument = "";
                    return true;
                case "treeradius":
                    minimumParts = maximumParts = 2;
                    radiusIndex = 1;
                    grammar = "a mode (clear, damage, or destroy) followed by a radius";
                    break;
                case "killradius":
                case "dismemberradius":
                case "gibradius":
                case "clearbushradius":
                case "burnbodyradius":
                    minimumParts = maximumParts = 1;
                    grammar = "exactly one radius";
                    break;
                case "igniteradius":
                case "shockradius":
                    minimumParts = 1;
                    maximumParts = 2;
                    grammar = "a radius followed by an optional duration";
                    break;
                case "damageradius":
                    minimumParts = maximumParts = 2;
                    grammar = "a radius followed by damage";
                    break;
                default:
                    return null;
            }

            string[] parts = (argument ?? "").Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < minimumParts || parts.Length > maximumParts)
            {
                ActionFail(command + " requires " + grammar);
                return false;
            }

            string mode = "";
            if (radiusIndex == 1)
            {
                if (String.Equals(parts[0], "clear", StringComparison.OrdinalIgnoreCase)) mode = "clear";
                else if (String.Equals(parts[0], "damage", StringComparison.OrdinalIgnoreCase)) mode = "damage";
                else if (String.Equals(parts[0], "destroy", StringComparison.OrdinalIgnoreCase)) mode = "destroy";
                else
                {
                    ActionFail("treeradius mode must be clear, damage, or destroy, followed by a radius");
                    return false;
                }
            }

            float radius;
            if (!TryParseLocalDebugFloat(parts[radiusIndex], "Radius", 1f, 80f, out radius)) return false;

            bool hasSecondNumber = radiusIndex == 0 && parts.Length == 2;
            float secondNumber = 0f;
            if (hasSecondNumber
                && (!Single.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out secondNumber)
                    || !IsFiniteDebugValue(secondNumber) || secondNumber <= 0f))
            {
                ActionFail((command == "damageradius" ? "Damage" : "Duration") + " must be a finite number greater than zero");
                return false;
            }

            NumberFormatInfo format;
            try
            {
                Il2CppSystem.Globalization.NumberFormatInfo nativeFormat = Il2CppSystem.Globalization.NumberFormatInfo.CurrentInfo;
                format = new NumberFormatInfo
                {
                    NumberDecimalSeparator = nativeFormat.NumberDecimalSeparator,
                    PositiveSign = nativeFormat.PositiveSign,
                    NegativeSign = nativeFormat.NegativeSign
                };
            }
            catch (Exception ex)
            {
                ActionFail(command + " unavailable: could not read the game's number format (" + ex.Message + ")");
                return false;
            }

            string normalized = radius.ToString("G9", format);
            if (radiusIndex == 1) normalized = mode + " " + normalized;
            if (hasSecondNumber) normalized += " " + secondNumber.ToString("G9", format);
            argument = normalized;
            return true;
        }
    }
}
