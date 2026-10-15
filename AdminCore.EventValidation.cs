using System;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MethodInfo _validateEventRangeMethod;
        private MethodInfo _validateStructureRangeMethod;

        private bool TryNativeServerValidationViolation(string eventName, object evnt, object connection, string steamId,
            out string reason, out string validation)
        {
            reason = "";
            validation = "Unavailable: no authenticated connection";
            if (evnt == null || connection == null) return false;

            if (eventName == "StructureHitEvent")
            {
                object damageValue = ReflectionTools.GetMemberValue(evnt, "Damage");
                object radiusValue = ReflectionTools.GetMemberValue(evnt, "Radius");
                if (!(damageValue is float damage) || !(radiusValue is float radius))
                {
                    validation = "Unavailable: structure damage or radius unreadable";
                    return false;
                }
                object center = ReflectionTools.GetMemberValue(evnt, "Center");
                if (TryStructureHitPayloadViolation(damage, radius, center is Vector3 point ? point : null,
                    out reason, out validation)) return true;
                object target = ReflectionTools.GetMemberValue(evnt, "Target");
                if (!IsLiveEventEntity(target))
                {
                    validation = "Unavailable: structure target is not attached";
                    return false;
                }
                float range = StructureHitRange(radius);
                bool accepted;
                if (!TryValidateEventRange(target, connection, steamId, range, true, out accepted, out validation)) return false;
                validation += "; live Target; limit " + range.ToString("0.#", CultureInfo.InvariantCulture) + "m";
                if (accepted) return false;
                reason = "Structure hit rejected by native range validation";
                return true;
            }

            Vector3 position;
            string source;
            bool malformed;
            if (!TryGetEventTargetPosition(eventName, evnt, out position, out source, out malformed))
            {
                validation = "Unavailable: " + source;
                if (!malformed) return false;
                reason = eventName + " contains a nonfinite " + source;
                validation = "Rejected nonfinite payload; native range check not run";
                return true;
            }
            int maxDistance = GetServerValidationDistance(eventName);
            bool valid;
            if (!TryValidateEventRange(position, connection, steamId, maxDistance, false, out valid, out validation)) return false;
            validation += "; " + source + " " + FormatVector(position) + "; limit " + maxDistance.ToString(CultureInfo.InvariantCulture) + "m";
            if (valid) return false;
            reason = eventName + " rejected by native range validation at " + FormatVector(position)
                + " via " + source + ", limit " + maxDistance.ToString(CultureInfo.InvariantCulture) + "m";
            return true;
        }

        private bool TryValidateEventRange(object target, object connection, string steamId, float distance, bool structure,
            out bool accepted, out string status)
        {
            accepted = false;
            status = "Unavailable: native range method missing";
            Type callbacks = ReflectionTools.FindType("CoopServerCallbacks");
            Type connectionType = ReflectionTools.FindType("BoltConnection");
            Type entityType = ReflectionTools.FindType("BoltEntity");
            if (callbacks == null || connectionType == null || entityType == null
                || !connectionType.IsInstanceOfType(connection)) return false;
            try
            {
                MethodInfo method;
                object[] arguments;
                if (structure)
                {
                    if (_validateStructureRangeMethod == null)
                        _validateStructureRangeMethod = callbacks.GetMethod("ValidateStructureHitEvent", BindingFlags.Public | BindingFlags.Static,
                            null, new[] { entityType, connectionType, typeof(float), entityType.MakeByRefType() }, null);
                    method = _validateStructureRangeMethod;
                    arguments = new[] { target, connection, (object)distance, null };
                }
                else
                {
                    if (_validateEventRangeMethod == null)
                        _validateEventRangeMethod = callbacks.GetMethod("ValidateEvent", BindingFlags.Public | BindingFlags.Static,
                            null, new[] { typeof(Vector3), connectionType, typeof(int) }, null);
                    method = _validateEventRangeMethod;
                    arguments = new[] { target, connection, (object)(int)distance };
                }
                if (method == null || method.ReturnType != typeof(bool)) return false;
                if (!TryReadRangeValidationSource(connection, steamId,
                    out IntPtr sourceEntity, out ulong sourceNetworkId, out uint sourceConnectionId, out status)) return false;
                status = "Unavailable: native range result unreadable";
                object result = method.Invoke(null, arguments);
                if (!(result is bool value)) return false;
                if (structure && (!(arguments[3] is Il2CppObjectBase validatedSource)
                    || validatedSource.Pointer != sourceEntity))
                {
                    status = "Unavailable: native structure validation did not retain the sender entity";
                    return false;
                }
                if (!TryReadRangeValidationSource(connection, steamId,
                    out IntPtr currentEntity, out ulong currentNetworkId, out uint currentConnectionId, out status)) return false;
                if (currentEntity != sourceEntity || currentNetworkId != sourceNetworkId || currentConnectionId != sourceConnectionId)
                {
                    status = "Unavailable: range source changed during native validation";
                    return false;
                }
                accepted = value;
                status = value ? "Native range check accepted" : "Native range check rejected";
                return true;
            }
            catch (Exception ex)
            {
                status = "Unavailable: native range call failed (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private bool TryGetEventTargetPosition(string eventName, object evnt, out Vector3 position,
            out string source, out bool malformed)
        {
            position = Vector3.zero;
            source = "no mapped target for " + eventName;
            malformed = false;
            if (evnt == null) return false;

            string positionMember = null;
            string entityMember = null;
            switch (eventName)
            {
                case "FreeFormPlaceElement":
                    return TryGetFreeFormWorldPosition(evnt, out position, out source, out malformed);
                case "DropItem":
                case "SpawnLootItemEvent":
                case "AttachItemToZiplineEvent":
                case "PlaceConstruction":
                case "PlaceFoundationEx":
                case "PlaceDryingFood":
                case "PlaceRope":
                case "DropVailActorEvent":
                case "SpawnVailPrefabEvent":
                case "SpawnCutTree": positionMember = "Position"; break;
                case "StructureCraftingNodeCreate":
                    return TryGetBlueprintWorldPosition(evnt, out position, out source, out malformed);
                case "PickupGlider":
                    object dropping = ReflectionTools.GetMemberValue(evnt, "dropGlider");
                    if (!(dropping is bool drop)) { source = "glider action unreadable"; return false; }
                    if (drop) positionMember = "dropPos";
                    else entityMember = "targetEntity";
                    break;
                case "ItemStorageAddItemEvent":
                case "ItemStorageRemoveItemEvent":
                case "ItemHolderAddItem":
                case "ItemHolderTakeItem":
                case "StructureHitEvent":
                case "StructureCraftingNodeAddIngredient":
                case "StructureCraftingNodeComplete":
                case "ArtifactStructureActivateEvent":
                case "ItemPlatingStructureActivateEvent": entityMember = "Target"; break;
                case "PlayerGiveActorItem":
                case "PlayerRequestActorItem": entityMember = "Actor"; break;
                case "PickupRequest": entityMember = "Item"; break;
                case "DismantleScrewStructure": entityMember = "TargetStructure"; break;
                case "DestroyPickUp": entityMember = "PickUpEntity"; break;
                case "DisablePickup": entityMember = "Entity"; break;
            }
            if (positionMember != null)
            {
                source = "payload " + positionMember;
                object value = ReflectionTools.GetMemberValue(evnt, positionMember);
                if (!(value is Vector3 point)) { source += " unreadable"; return false; }
                if (!IsFiniteEventPosition(point)) { malformed = true; return false; }
                position = point;
                return true;
            }
            if (entityMember == null) return false;
            source = "live " + entityMember;
            object entity = ReflectionTools.GetMemberValue(evnt, entityMember);
            if (!IsLiveEventEntity(entity) || !TryGetObjectWorldPosition(entity, out position))
            {
                source += " unavailable";
                return false;
            }
            return true;
        }

        private static bool TryGetFreeFormWorldPosition(object evnt, out Vector3 position, out string source, out bool malformed)
        {
            position = Vector3.zero;
            malformed = false;
            source = "freeform action unreadable";
            if (!(ReflectionTools.GetMemberValue(evnt, "IsPlacing") is bool placing)) return false;
            if (!placing)
            {
                source = "freeform animation request; placement range not evaluated";
                return false;
            }

            source = "freeform PlacePosition unreadable";
            if (!(ReflectionTools.GetMemberValue(evnt, "PlacePosition") is Vector3 requested)) return false;
            if (!IsFiniteEventPosition(requested))
            {
                source = "payload PlacePosition";
                malformed = true;
                return false;
            }

            source = "freeform target identifiers unreadable; coordinate space unknown";
            if (!(ReflectionTools.GetMemberValue(evnt, "TargetTypeID") is int targetType)
                || !(ReflectionTools.GetMemberValue(evnt, "TargetInstanceID") is int targetInstance)) return false;
            // Native placement checks the structure version or sender anticipation before transforming the point.
            if (targetType > 0 && targetInstance > 0)
            {
                source = "freeform structure-relative PlacePosition; native target resolution unavailable";
                return false;
            }

            source = "freeform pickup identifier unreadable; coordinate space unknown";
            object pickupId = ReflectionTools.InvokeNoArgMethod(evnt, "GetPickupEntityNetworkIdValue");
            if (!(ReflectionTools.GetMemberValue(pickupId, "PackedValue") is ulong packedPickupId)) return false;
            if (packedPickupId != 0)
            {
                source = "freeform pickup-relative PlacePosition; native target resolution unavailable";
                return false;
            }

            position = requested;
            source = "unparented freeform PlacePosition";
            return true;
        }

        private bool TryGetBlueprintWorldPosition(object evnt, out Vector3 position, out string source, out bool malformed)
        {
            position = Vector3.zero;
            malformed = false;
            source = "blueprint Position unreadable";
            if (!(ReflectionTools.GetMemberValue(evnt, "Position") is Vector3 requested)) return false;
            if (!IsFiniteEventPosition(requested))
            {
                source = "payload Position";
                malformed = true;
                return false;
            }

            try
            {
                object parentId = ReflectionTools.InvokeNoArgMethod(evnt, "GetParentNetworkIdValue");
                if (!(ReflectionTools.GetMemberValue(parentId, "PackedValue") is ulong packedParentId))
                {
                    source = "blueprint parent ID unreadable; coordinate space unknown";
                    return false;
                }
                if (packedParentId == 0)
                {
                    if (ReflectionTools.GetMemberValue(evnt, "Parent") is UnityEngine.Object unexpectedParent && unexpectedParent != null)
                    {
                        source = "blueprint parent and network ID disagree; coordinate space unknown";
                        return false;
                    }
                    position = requested;
                    source = "unparented blueprint Position";
                    return true;
                }

                source = "blueprint parent unavailable or does not match the requested network ID";
                if (!TryReadBlueprintParent(evnt, packedParentId, out Component parent)) return false;
                Transform transform = parent.transform;
                if (transform == null) return false;

                // Native placement transforms the local point with this parent's full transform.
                Vector3 worldPosition = transform.TransformPoint(requested);
                if (!IsFiniteEventPosition(worldPosition))
                {
                    source = "blueprint parent produced a nonfinite world position";
                    return false;
                }
                if (!TryReadBlueprintParent(evnt, packedParentId, out Component currentParent)
                    || currentParent.Pointer != parent.Pointer)
                {
                    source = "blueprint parent changed while resolving the world position";
                    return false;
                }
                position = worldPosition;
                source = "parent-transformed blueprint Position";
                return true;
            }
            catch (Exception ex)
            {
                source = "blueprint parent read failed (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private static bool TryReadBlueprintParent(object evnt, ulong expectedNetworkId, out Component parent)
        {
            parent = null;
            object value = ReflectionTools.GetMemberValue(evnt, "Parent");
            if (!IsLiveEventEntity(value) || !(value is Component component)) return false;
            object networkId = ReflectionTools.GetMemberValue(value, "networkId");
            if (ReflectionTools.GetMemberValue(networkId, "PackedValue") is not ulong packed || packed != expectedNetworkId) return false;
            parent = component;
            return true;
        }

        private static bool IsLiveEventEntity(object value)
        {
            Type entityType = ReflectionTools.FindType("BoltEntity");
            return value is UnityEngine.Object native && native != null && entityType != null
                && entityType.IsInstanceOfType(value) && ReflectionTools.GetMemberValue(value, "isAttached") is true;
        }

        private bool TryGetItemEventPosition(object evnt, out Vector3 position, out string source)
        {
            bool malformed;
            return TryGetEventTargetPosition(evnt?.GetType().Name ?? "", evnt, out position, out source, out malformed);
        }

        private static bool IsFiniteEventPosition(Vector3 position)
        {
            return IsFiniteDebugValue(position.x) && IsFiniteDebugValue(position.y) && IsFiniteDebugValue(position.z);
        }

        private bool TryGetObjectWorldPosition(object value, out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                Transform transform = null;
                if (value is Component component && component != null) transform = component.transform;
                else if (value is GameObject gameObject && gameObject != null) transform = gameObject.transform;
                if (transform == null) return false;
                position = transform.position;
                return IsFiniteEventPosition(position);
            }
            catch { return false; }
        }
    }
}
