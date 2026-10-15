using System;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed class PossessionController
    {
        private readonly AdminCore _core;
        private object _actor;
        private Component _actorComponent;
        private Transform _playerTransform;
        private Type _localPlayerType;
        private MethodInfo _readPosition;
        private MethodInfo _moveActor;
        private MethodInfo _turnActor;
        private MethodInfo _movePlayer;
        private MethodInfo _isDead;
        private MethodInfo _isInjured;
        private PropertyInfo _stoppedByPlayer;
        private bool _originalStoppedState;
        private bool _needsRelease;
        private bool _active;
        private string _lastStatus = "Ready to control an active Kelvin.";

        public PossessionController(AdminCore core)
        {
            _core = core;
        }

        public bool Active { get { return _active; } }
        public bool NeedsRelease { get { return _needsRelease; } }
        public string LastStatus { get { return _lastStatus; } }

        public bool Start()
        {
            if (_active) return true;
            if (_needsRelease)
            {
                _lastStatus = "Release the previous control state before starting again.";
                return false;
            }
            if (!_core.HasWorldAuthority())
            {
                _lastStatus = "World authority is unavailable.";
                return false;
            }

            try
            {
                _localPlayerType = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
                _playerTransform = GetPlayerTransform();
                if (_playerTransform == null) return StartFailed("Local player is not ready.");
                Vector3 playerPosition = _playerTransform.position;
                if (!IsFinite(playerPosition)) return StartFailed("Local player position is unavailable.");

                Type managerType = ReflectionTools.FindType("Sons.Ai.Vail.VailActorManager");
                Type actorIdType = ReflectionTools.FindType("Sons.Ai.Vail.VailActorTypeId");
                Type actorType = ReflectionTools.FindType("Sons.Ai.Vail.VailActor");
                if (managerType == null || actorIdType == null || !actorIdType.IsEnum || actorType == null)
                    return StartFailed("Kelvin's actor registry is unavailable.");

                object robbyId = Enum.Parse(actorIdType, "Robby");
                MethodInfo find = FindMethod(managerType, "FindActiveActor", actorType, true, actorIdType);
                if (find == null) return StartFailed("Kelvin lookup is unavailable.");
                _actor = find.Invoke(null, new object[] { robbyId });
                _actorComponent = _actor as Component;
                if (_actorComponent == null || !_actorComponent.gameObject.activeInHierarchy)
                    return StartFailed("Kelvin is not active in this part of the world.");
                object actualId = ReflectionTools.GetMemberValue(_actor, "TypeId");
                if (!robbyId.Equals(actualId)) return StartFailed("The actor registry did not return Kelvin.");

                _stoppedByPlayer = actorType.GetProperty("_stoppedByPlayer", BindingFlags.Public | BindingFlags.Instance);
                _readPosition = FindMethod(actorType, "Position", typeof(Vector3), false);
                _moveActor = FindMethod(actorType, "MoveActor", typeof(bool), false, typeof(Vector3), typeof(float));
                _turnActor = FindMethod(actorType, "SetPositionAndRotation", typeof(void), false, typeof(Vector3), typeof(Quaternion), typeof(bool));
                _movePlayer = FindMethod(_localPlayerType, "SetPosition", typeof(void), true, typeof(Vector3));
                _isDead = FindMethod(actorType, "IsDead", typeof(bool), false);
                _isInjured = FindMethod(actorType, "IsRobbyInjured", typeof(bool), false);
                if (_stoppedByPlayer == null || _stoppedByPlayer.PropertyType != typeof(bool)
                    || !_stoppedByPlayer.CanRead || !_stoppedByPlayer.CanWrite || _readPosition == null
                    || _moveActor == null || _turnActor == null || _movePlayer == null || _isDead == null || _isInjured == null)
                    return StartFailed("Kelvin's control APIs are unavailable.");

                string unavailable = GetActorUnavailableReason();
                if (unavailable != null) return StartFailed(unavailable);
                ReadActorPosition();
                if (GetCameraTransform() == null) return StartFailed("The player camera is not ready.");

                _originalStoppedState = (bool)_stoppedByPlayer.GetValue(_actor);
                // Capture ownership before writing so a partially applied setter can still be released.
                _needsRelease = !_originalStoppedState;
                if (_needsRelease) _stoppedByPlayer.SetValue(_actor, true);
                if (!(bool)_stoppedByPlayer.GetValue(_actor))
                    return StartFailed("Kelvin did not accept the stop flag.");

                _active = true;
                _lastStatus = "Kelvin control active. Close the console to move.";
                return true;
            }
            catch (Exception ex)
            {
                LogFailure("Starting Kelvin control", ex);
                return StartFailed("The game could not start Kelvin control.");
            }
        }

        public bool Stop(string reason = "Control released")
        {
            _active = false;
            try
            {
                if (_needsRelease && _actorComponent != null)
                {
                    bool current = (bool)_stoppedByPlayer.GetValue(_actor);
                    if (current != _originalStoppedState)
                    {
                        _stoppedByPlayer.SetValue(_actor, _originalStoppedState);
                        if ((bool)_stoppedByPlayer.GetValue(_actor) != _originalStoppedState)
                        {
                            _lastStatus = reason + ". Restoring Kelvin's stop flag is still pending; use Retry Release.";
                            return false;
                        }
                    }
                }
                ClearControlReferences();
                _lastStatus = reason + ".";
                return true;
            }
            catch (Exception ex)
            {
                LogFailure("Releasing Kelvin control", ex);
                _lastStatus = reason + ". Restoring Kelvin's stop flag failed; use Retry Release.";
                return false;
            }
        }

        public void Update()
        {
            if (!_active) return;
            try
            {
                if (!_core.HasWorldAuthority())
                {
                    Stop("Kelvin control ended because world authority was lost");
                    return;
                }
                if (_playerTransform == null || GetPlayerTransform() != _playerTransform)
                {
                    Stop("Kelvin control ended because the local player changed");
                    return;
                }
                string unavailable = GetActorUnavailableReason();
                if (unavailable != null)
                {
                    Stop("Kelvin control ended: " + unavailable.TrimEnd('.'));
                    return;
                }
                if (!(bool)_stoppedByPlayer.GetValue(_actor))
                {
                    Stop("Kelvin control ended because the stop flag changed");
                    return;
                }
                if (!_core.PossessionInputAllowed) return;

                Transform camera = GetCameraTransform();
                if (camera == null)
                {
                    Stop("Kelvin control ended because the camera is unavailable");
                    return;
                }
                float deltaTime = Time.deltaTime;
                float speed = _core.PossessSpeed;
                if (!IsFinite(deltaTime) || deltaTime < 0f || !IsFinite(speed) || speed <= 0f)
                {
                    Stop("Kelvin control ended because movement settings are invalid");
                    return;
                }
                if (deltaTime == 0f) return;

                Vector3 position = ReadActorPosition();
                Vector3 movement = GetMovement(camera);
                bool accepted = true;
                if (movement.sqrMagnitude > 0.0001f)
                {
                    if (Input.GetKey(KeyCode.LeftShift)) speed *= 2f;
                    Vector3 delta = movement.normalized * speed * deltaTime;
                    if (!IsFinite(delta)) throw new InvalidOperationException("Movement delta was not finite.");
                    accepted = (bool)_moveActor.Invoke(_actor, new object[] { delta, deltaTime });
                    Vector3 observed = ReadActorPosition();
                    _lastStatus = !accepted ? "Kelvin movement was not accepted."
                        : (observed - position).sqrMagnitude <= 0.000001f ? "Kelvin movement requested; position is unchanged."
                        : "Kelvin control active.";
                    position = observed;
                }
                if (accepted)
                {
                    float yaw = camera.eulerAngles.y;
                    if (!IsFinite(yaw)) throw new InvalidOperationException("Camera rotation was not finite.");
                    _turnActor.Invoke(_actor, new object[] { position, Quaternion.Euler(0f, yaw, 0f), false });
                    position = ReadActorPosition();
                }
                // Follow the observed actor position, including when native movement was blocked.
                _movePlayer.Invoke(null, new object[] { position });
            }
            catch (Exception ex)
            {
                LogFailure("Updating Kelvin control", ex);
                Stop("Kelvin control ended after a game API failure");
            }
        }

        private string GetActorUnavailableReason()
        {
            if (_actorComponent == null || !_actorComponent.gameObject.activeInHierarchy) return "Kelvin is no longer active.";
            if ((bool)_isDead.Invoke(_actor, null)) return "Kelvin is dead.";
            if ((bool)_isInjured.Invoke(_actor, null)) return "Kelvin is injured.";
            object ragdoll = ReflectionTools.GetMemberValue(_actor, "RagdollManager");
            object ragdollEnabled = ReflectionTools.GetMemberValue(ragdoll, "IsRagdollEnabled");
            if (!(ragdollEnabled is bool)) return "Kelvin's physical state is unavailable.";
            return (bool)ragdollEnabled ? "Kelvin is ragdolled." : null;
        }

        private bool StartFailed(string reason)
        {
            if (_needsRelease)
            {
                Stop(reason.TrimEnd('.'));
            }
            else
            {
                ClearControlReferences();
                _lastStatus = reason;
            }
            return false;
        }

        private void ClearControlReferences()
        {
            _active = false;
            _needsRelease = false;
            _actor = null;
            _actorComponent = null;
            _playerTransform = null;
            _localPlayerType = null;
            _readPosition = null;
            _moveActor = null;
            _turnActor = null;
            _movePlayer = null;
            _isDead = null;
            _isInjured = null;
            _stoppedByPlayer = null;
        }

        private Vector3 ReadActorPosition()
        {
            Vector3 position = (Vector3)_readPosition.Invoke(_actor, null);
            if (!IsFinite(position)) throw new InvalidOperationException("Kelvin position was not finite.");
            return position;
        }

        private Transform GetPlayerTransform()
        {
            return ReflectionTools.GetStaticMemberValue(_localPlayerType, "Transform") as Transform;
        }

        private Transform GetCameraTransform()
        {
            Transform transform = ReflectionTools.GetStaticMemberValue(_localPlayerType, "MainCamTr") as Transform;
            if (transform != null) return transform;
            Camera camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        private static Vector3 GetMovement(Transform camera)
        {
            Vector3 forward = camera.forward;
            Vector3 right = camera.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement += forward;
            if (Input.GetKey(KeyCode.S)) movement -= forward;
            if (Input.GetKey(KeyCode.D)) movement += right;
            if (Input.GetKey(KeyCode.A)) movement -= right;
            if (Input.GetKey(KeyCode.Space)) movement.y += 1f;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) movement.y -= 1f;
            if (!IsFinite(movement)) throw new InvalidOperationException("Camera movement was not finite.");
            return movement;
        }

        private static MethodInfo FindMethod(Type type, string name, Type returnType, bool isStatic, params Type[] parameters)
        {
            if (type == null) return null;
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic
                | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, parameters, null);
            return method != null && method.ReturnType == returnType ? method : null;
        }

        private static bool IsFinite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static void LogFailure(string action, Exception ex)
        {
            Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            Plugin.LogSource?.LogWarning(action + " failed: " + cause.Message);
        }
    }
}
