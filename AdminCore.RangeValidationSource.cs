using System;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MethodInfo _rangeValidationGetSteamId;
        private MethodInfo _rangeValidationEntityCast;

        private bool TryReadRangeValidationSource(object connection, string steamId, out IntPtr entityPointer,
            out ulong networkId, out uint connectionId, out string status)
        {
            entityPointer = IntPtr.Zero;
            networkId = 0;
            connectionId = 0;
            status = "Unavailable: host session";
            try
            {
                if (!HasHostSession()) return false;

                status = "Unavailable: sender Steam ID";
                if (!UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong expectedSteamId)
                    || expectedSteamId == 0) return false;

                status = "Unavailable: sender connection";
                Type connectionType = ReflectionTools.FindType("BoltConnection");
                if (connectionType == null || connection == null || connection.GetType() != connectionType
                    || !(connection is Il2CppObjectBase nativeConnection) || nativeConnection.Pointer == IntPtr.Zero)
                    return false;
                IntPtr connectionPointer = nativeConnection.Pointer;

                status = "Unavailable: connected transport";
                object udpConnection = ReflectionTools.GetMemberValue(connection, "udpConnection");
                if (!(ReflectionTools.GetMemberValue(udpConnection, "IsConnected") is true)) return false;

                status = "Unavailable: connection ID";
                if (!(ReflectionTools.GetMemberValue(connection, "ConnectionId") is uint capturedConnectionId)) return false;

                status = "Unavailable: sender identity binding";
                if (_rangeValidationGetSteamId == null)
                {
                    Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities");
                    MethodInfo method = utilities?.GetMethod("GetSteamId", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { connectionType }, null);
                    if (method == null || method.ReturnType != typeof(ulong) || method.ContainsGenericParameters
                        || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != connectionType)
                        return false;
                    _rangeValidationGetSteamId = method;
                }
                if (!(_rangeValidationGetSteamId.Invoke(null, new[] { connection }) is ulong actualSteamId)
                    || actualSteamId != expectedSteamId) return false;

                status = "Unavailable: sender entity data";
                if (!(ReflectionTools.GetMemberValue(connection, "UserData") is Il2CppObjectBase userData)
                    || userData.Pointer == IntPtr.Zero) return false;

                status = "Unavailable: sender entity cast";
                Type entityType = ReflectionTools.FindType("BoltEntity");
                if (entityType == null) return false;
                if (_rangeValidationEntityCast == null)
                {
                    MethodInfo cast = typeof(Il2CppObjectBase).GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (cast == null || !cast.IsGenericMethodDefinition) return false;
                    _rangeValidationEntityCast = cast.MakeGenericMethod(entityType);
                }
                object entity = _rangeValidationEntityCast.Invoke(userData, null);

                status = "Unavailable: live sender entity";
                if (entity == null || entity.GetType() != entityType || !(entity is UnityEngine.Object unityEntity)
                    || unityEntity == null || !(entity is Il2CppObjectBase nativeEntity)
                    || nativeEntity.Pointer == IntPtr.Zero) return false;
                IntPtr capturedEntityPointer = nativeEntity.Pointer;
                if (!(ReflectionTools.GetMemberValue(entity, "isAttached") is true)) return false;

                status = "Unavailable: sender network ID";
                object entityNetworkId = ReflectionTools.GetMemberValue(entity, "networkId");
                if (!(ReflectionTools.GetMemberValue(entityNetworkId, "PackedValue") is ulong capturedNetworkId)) return false;

                status = "Unavailable: sender entity source binding";
                object source = ReflectionTools.GetMemberValue(entity, "source");
                if (!(source is Il2CppObjectBase nativeSource) || nativeSource.Pointer != connectionPointer
                    || !(ReflectionTools.GetMemberValue(source, "ConnectionId") is uint sourceConnectionId)
                    || sourceConnectionId != capturedConnectionId) return false;

                status = "Unavailable: sender world position";
                if (!TryGetObjectWorldPosition(entity, out Vector3 position) || !IsFiniteEventPosition(position)) return false;

                entityPointer = capturedEntityPointer;
                networkId = capturedNetworkId;
                connectionId = capturedConnectionId;
                status = "Available";
                return true;
            }
            catch
            {
                entityPointer = IntPtr.Zero;
                networkId = 0;
                connectionId = 0;
                return false;
            }
        }
    }
}
