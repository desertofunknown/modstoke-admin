using System;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MovementContext GetMovementContext(PlayerEntry entry, object playerState)
        {
            MovementContext context = MovementContext.Foot(_maxHorizontalSpeed, _maxVerticalSpeed, _teleportDistanceThreshold);
            if (playerState == null) context.Evidence = "Player state unavailable; no active transport observation";
            float networkSpeed = 0f;
            TryReadFloatMember(playerState, "overallSpeed", out networkSpeed);
            if (float.IsNaN(networkSpeed) || float.IsInfinity(networkSpeed) || networkSpeed <= 0f)
            {
                TryReadFloatMember(playerState, "hSpeed", out networkSpeed);
            }
            context.NetworkedSpeed = float.IsNaN(networkSpeed) || float.IsInfinity(networkSpeed) ? 0f : Math.Abs(networkSpeed);

            int dyingState;
            int tiedUpState;
            object dyingValue = ReflectionTools.GetMemberValue(playerState, "Dying") ?? ReflectionTools.InvokeNoArgMethod(playerState, "get_Dying");
            object tiedUpValue = ReflectionTools.GetMemberValue(playerState, "TiedUpState") ?? ReflectionTools.InvokeNoArgMethod(playerState, "get_TiedUpState");
            if ((TryConvertToInt(dyingValue, out dyingState) && dyingState > 0)
                || (TryConvertToInt(tiedUpValue, out tiedUpState) && tiedUpState > 0))
            {
                context.ObservedMode = (dyingValue is int && (int)dyingValue > 0)
                    || (tiedUpValue is int && (int)tiedUpValue > 0) ? MovementMode.Story : MovementMode.Unknown;
                context.Label = "story/death transition";
                context.Evidence = "Replicated PlayerState death or tied-up state";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _golfCartHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, 120f);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 260f);
                context.TeleportMultiplier = 5.0f;
                context.AllowsAirborne = true;
                return context;
            }

            bool? drivingGolfCart = ReadMovementBoolMember(playerState, "DrivingGolfCart");
            bool? passengerGolfCart = ReadMovementBoolMember(playerState, "PassengerGolfCart");
            if (drivingGolfCart == true || passengerGolfCart == true)
            {
                context.ObservedMode = MovementMode.GolfCart;
                context.Label = drivingGolfCart == true ? "golf cart driver" : "golf cart passenger";
                context.Evidence = "Replicated PlayerState vehicle flags";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _golfCartHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, _vehicleVerticalSpeed);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 140f);
                context.TeleportMultiplier = 4.0f;
                context.HasVehicle = true;
                return context;
            }

            GolfCartOccupant golfCartOccupant;
            if (TryFindGolfCartForPlayer(entry, out golfCartOccupant))
            {
                context.ObservedMode = MovementMode.GolfCart;
                context.Label = golfCartOccupant.Driver ? "golf cart driver" : "golf cart passenger";
                context.Evidence = "Current native golf-cart seat";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _golfCartHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, _vehicleVerticalSpeed);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 140f);
                context.TeleportMultiplier = 4.0f;
                context.HasVehicle = true;
                return context;
            }

            bool? onRaft = ReadMovementBoolMember(playerState, "onRaft");
            if (onRaft == true)
            {
                context.ObservedMode = MovementMode.Raft;
                context.Label = "raft";
                context.Evidence = "Replicated PlayerState raft flag";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _golfCartHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, _vehicleVerticalSpeed);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 120f);
                context.TeleportMultiplier = 3.5f;
                context.HasVehicle = true;
                return context;
            }

            bool? zipLineAttach = ReadMovementBoolMember(playerState, "zipLineAttach");
            if (zipLineAttach == true)
            {
                context.ObservedMode = MovementMode.Zipline;
                context.Label = "zipline";
                context.Evidence = "Replicated PlayerState zipline attachment flag";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _golfCartHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, _vehicleVerticalSpeed);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 120f);
                context.TeleportMultiplier = 3.5f;
                context.AllowsAirborne = true;
                return context;
            }

            if (EntryHasActiveGlider(entry))
            {
                context = ConfigureGliderContext(context, "glider");
                context.ObservedMode = MovementMode.Glider;
                context.Evidence = "Active hang-glider component";
                return context;
            }

            if (EntryHoldsTransportItem(playerState, 626))
            {
                context = ConfigureGliderContext(context, "glider held");
                context.Evidence = "Replicated held-item state; active flight is unconfirmed";
                return context;
            }

            bool activeKnightV = EntryHasActiveKnightV(entry);
            if (activeKnightV || EntryHoldsTransportItem(playerState, 630))
            {
                context.ObservedMode = activeKnightV ? MovementMode.KnightV : MovementMode.Unknown;
                context.Label = activeKnightV ? "KnightV" : "KnightV held";
                context.Evidence = activeKnightV ? "Active KnightV component" : "Replicated held-item state; active riding is unconfirmed";
                context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _knightVHorizontalSpeed);
                context.VerticalLimit = Math.Max(_maxVerticalSpeed, _vehicleVerticalSpeed);
                context.TeleportBase = Math.Max(_teleportDistanceThreshold, 130f);
                context.TeleportMultiplier = 3.75f;
                context.HasVehicle = true;
                return context;
            }

            if (dyingValue is int && tiedUpValue is int && drivingGolfCart.HasValue
                && passengerGolfCart.HasValue && onRaft.HasValue && zipLineAttach.HasValue)
            {
                context.ObservedMode = MovementMode.Foot;
            }

            return context;
        }

        private static bool? ReadMovementBoolMember(object value, string name)
        {
            object found = ReflectionTools.GetMemberValue(value, name) ?? ReflectionTools.InvokeNoArgMethod(value, "get_" + name);
            return found is bool ? (bool?)found : null;
        }

        private MovementContext ApplyDescentMovementContext(MovementContext context, MovementSignalState state)
        {
            // Descent changes the movement limits; it does not establish a transport transition.
            if (context != null && context.Label == "foot" && state != null
                && state.GliderLikeUntilUtc > DateTime.UtcNow && state.GliderLikeSamples >= 4)
            {
                return ConfigureAirborneDescentContext(context);
            }
            return context;
        }

        private MovementContext ConfigureGliderContext(MovementContext context, string label)
        {
            context = context ?? MovementContext.Foot(_maxHorizontalSpeed, _maxVerticalSpeed, _teleportDistanceThreshold);
            context.Label = label;
            context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, _gliderHorizontalSpeed);
            context.VerticalLimit = Math.Max(_maxVerticalSpeed, _gliderVerticalSpeed);
            context.TeleportBase = Math.Max(_teleportDistanceThreshold, 220f);
            context.TeleportMultiplier = 4.5f;
            context.AllowsAirborne = true;
            return context;
        }

        private MovementContext ConfigureAirborneDescentContext(MovementContext context)
        {
            context = context ?? MovementContext.Foot(_maxHorizontalSpeed, _maxVerticalSpeed, _teleportDistanceThreshold);
            context.Label = "airborne descent";
            context.Evidence = "Observed descent pattern; no active glider confirmed";
            context.HorizontalLimit = Math.Max(_maxHorizontalSpeed, Math.Min(_gliderHorizontalSpeed * 0.55f, 46f));
            context.VerticalLimit = Math.Max(_maxVerticalSpeed, Math.Min(Math.Max(_gliderVerticalSpeed, 140f), _gliderFallbackVerticalSpeed));
            context.TeleportBase = Math.Max(_teleportDistanceThreshold, 140f);
            context.TeleportMultiplier = 2.75f;
            context.AllowsAirborne = true;
            return context;
        }
    }
}
