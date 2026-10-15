using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MethodInfo _playerStateReader;
        private MethodInfo _golfCartFinder;
        private readonly Dictionary<Type, MethodInfo> _transportComponentReaders = new Dictionary<Type, MethodInfo>();
        private readonly List<object> _currentGolfCarts = new List<object>();
        private int _golfCartScanFrame = -1;

        private object GetNetworkedPlayerState(PlayerEntry entry)
        {
            object entity = entry?.BoltEntity;
            if (!(entity is UnityEngine.Object nativeEntity) || nativeEntity == null
                || !(ReflectionTools.GetMemberValue(entity, "isAttached") is true)) return null;
            Type playerStateType = ReflectionTools.FindType("IPlayerState");
            if (playerStateType == null) return null;
            try
            {
                if (_playerStateReader == null)
                {
                    foreach (MethodInfo method in entity.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (method.Name == "TryFindState" && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1 && method.ReturnType == typeof(bool)
                            && parameters.Length == 1 && parameters[0].IsOut)
                        {
                            _playerStateReader = method.MakeGenericMethod(playerStateType);
                            break;
                        }
                    }
                }
                object[] arguments = { null };
                return _playerStateReader?.Invoke(entity, arguments) is true ? arguments[0] : null;
            }
            catch { return null; }
        }

        private bool EntryHasActiveGlider(PlayerEntry entry)
        {
            return EntryHasTransportAction(entry, "Sons.Gameplay.PlayerHangGliderAction", "IsGliding", false);
        }

        private bool EntryHasActiveKnightV(PlayerEntry entry)
        {
            return EntryHasTransportAction(entry, "Sons.Gameplay.PlayerKnightVAction", "IsActive", true);
        }

        private bool EntryHasTransportAction(PlayerEntry entry, string typeName, string memberName, bool invokeMethod)
        {
            Component entity = entry?.BoltEntity as Component;
            Type type = ReflectionTools.FindType(typeName);
            if (entity == null || type == null || !(ReflectionTools.GetMemberValue(entity, "isAttached") is true)) return false;
            try
            {
                MethodInfo reader;
                if (!_transportComponentReaders.TryGetValue(type, out reader))
                {
                    foreach (MethodInfo method in typeof(GameObject).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (method.Name == "GetComponentsInChildren" && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1 && parameters.Length == 1
                            && parameters[0].ParameterType == typeof(bool))
                        {
                            reader = method.MakeGenericMethod(type);
                            _transportComponentReaders.Add(type, reader);
                            break;
                        }
                    }
                }
                IEnumerable actions = reader?.Invoke(entity.gameObject, new object[] { false }) as IEnumerable;
                if (actions == null) return false;
                foreach (object action in actions)
                {
                    if (!(action is Behaviour behaviour) || behaviour == null || !behaviour.isActiveAndEnabled) continue;
                    object value = invokeMethod ? ReflectionTools.InvokeNoArgMethod(action, memberName)
                        : ReflectionTools.GetMemberValue(action, memberName);
                    if (value is true) return true;
                }
            }
            catch { }
            return false;
        }

        private bool EntryHoldsTransportItem(object state, int itemId)
        {
            if (state == null || itemId <= 0) return false;
            object heldOnly = ReflectionTools.GetMemberValue(state, "HeldOnlyItemId");
            if (heldOnly is int heldId && heldId == itemId) return true;
            return NetworkArrayContainsInt(ReflectionTools.GetMemberValue(state, "HeldItems"), itemId);
        }

        private void RefreshCurrentGolfCarts()
        {
            int frame = Time.frameCount;
            if (_golfCartScanFrame == frame) return;
            _golfCartScanFrame = frame;
            _currentGolfCarts.Clear();
            if (!_worldReady) return;
            Type type = ReflectionTools.FindType("Sons.Gameplay.GolfCartController");
            if (type == null) return;
            try
            {
                if (_golfCartFinder == null)
                {
                    foreach (MethodInfo method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name == "FindObjectsOfType" && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)
                        {
                            _golfCartFinder = method.MakeGenericMethod(type);
                            break;
                        }
                    }
                }
                IEnumerable carts = _golfCartFinder?.Invoke(null, null) as IEnumerable;
                if (carts == null) return;
                foreach (object cart in carts)
                    if (cart is Behaviour behaviour && behaviour != null && behaviour.isActiveAndEnabled) _currentGolfCarts.Add(cart);
            }
            catch { _currentGolfCarts.Clear(); }
        }

        private bool TryFindGolfCartForPlayer(PlayerEntry entry, out GolfCartOccupant occupant)
        {
            occupant = null;
            UnityEngine.Object playerEntity = entry?.BoltEntity as UnityEngine.Object;
            if (entry == null || !entry.Online || entry.PresenceUnresolved || playerEntity == null
                || !(ReflectionTools.GetMemberValue(entry.BoltEntity, "isAttached") is true)) return false;
            RefreshCurrentGolfCarts();
            foreach (object cart in _currentGolfCarts)
            {
                if (!(cart is Behaviour behaviour) || behaviour == null || !behaviour.isActiveAndEnabled) continue;
                object countValue = ReflectionTools.InvokeNoArgMethod(cart, "GetMaxPlayerSlotCount");
                object driverValue = ReflectionTools.InvokeNoArgMethod(cart, "GetDriverSlotIndex");
                if (!(countValue is int count) || count <= 0 || count > 12
                    || !(driverValue is int driver) || driver < 0 || driver >= count) continue;
                for (int slot = 0; slot < count; slot++)
                {
                    object entity = ReflectionTools.InvokeInstanceMethodResult(cart, "GetNetworkEntity", new object[] { slot });
                    if (!(entity is UnityEngine.Object nativeEntity) || nativeEntity == null || nativeEntity != playerEntity
                        || !(ReflectionTools.GetMemberValue(entity, "isAttached") is true)) continue;
                    occupant = new GolfCartOccupant { Cart = cart, Entity = entity, SlotIndex = slot, Driver = slot == driver };
                    return true;
                }
            }
            return false;
        }
    }
}
