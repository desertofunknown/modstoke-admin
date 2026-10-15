using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MethodInfo _coopServerReceiverCast;
        private MethodInfo _constructionServerReceiverCast;
        private int _constructionGuardPatchCount;

        private bool IsGuardServerReceiver(object listener)
        {
            if (!(listener is Component component) || component == null) return false;
            return IsNativeGuardReceiver(listener, "CoopServerCallbacks", ref _coopServerReceiverCast)
                || IsNativeGuardReceiver(listener, "Construction.Multiplayer.FreeFormSystemNetworkProxyServer", ref _constructionServerReceiverCast);
        }

        private static bool IsNativeGuardReceiver(object listener, string typeName, ref MethodInfo cast)
        {
            try
            {
                if (cast == null)
                {
                    Type type = ReflectionTools.FindType(typeName);
                    if (type == null) return false;
                    MethodInfo method = typeof(Il2CppObjectBase).GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (method == null || !method.IsGenericMethodDefinition) return false;
                    cast = method.MakeGenericMethod(type);
                }
                return cast.Invoke(listener, null) != null;
            }
            catch { return false; }
        }

        private int PatchConstructionEventGuards(Harmony harmony, MethodInfo prefix)
        {
            // Placement is declared on the shared base. The prefix checks the native receiver before enforcing.
            int count = PatchEventListener(harmony, "Construction.Multiplayer.FreeFormSystemNetworkProxyBase",
                ReflectionTools.FindType("FreeFormPlaceElement"), prefix, "Construction guard");
            count += PatchEventListener(harmony, "Construction.Multiplayer.FreeFormSystemNetworkProxyServer",
                ReflectionTools.FindType("StructureCraftingNodeCreate"), prefix, "Construction guard");
            count += PatchEventListener(harmony, "Construction.Multiplayer.FreeFormSystemNetworkProxyServer",
                ReflectionTools.FindType("DismantleScrewStructure"), prefix, "Construction guard");
            _constructionGuardPatchCount = count;
            return count;
        }
    }
}
