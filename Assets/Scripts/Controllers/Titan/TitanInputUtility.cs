using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Titan
{
    public static class TitanInputUtility
    {
        public static Vector2 ComputeSphericalAngles(
            Vector2 mousePosition,
            Vector2 origin,
            float thetaRadiusPixels,
            float maxThetaDegrees,
            float sensitivity,
            float secondaryMaxDegrees = -1f,
            bool applySensitivityToSecondary = true)
        {
            Vector2 fromOrigin = mousePosition - origin;
            float radius = fromOrigin.magnitude;
            float theta01 = thetaRadiusPixels > 0f ? Mathf.Clamp01(radius / thetaRadiusPixels) : 0f;
            float theta = theta01 * maxThetaDegrees * sensitivity;
            float phiRad = Mathf.Atan2(fromOrigin.y, fromOrigin.x);
            float normalizedY = thetaRadiusPixels > 0f ? Mathf.Clamp(fromOrigin.y / thetaRadiusPixels, -1f, 1f) : 0f;

            float yaw = theta * Mathf.Cos(phiRad);
            float resolvedSecondaryMax = secondaryMaxDegrees > 0f ? secondaryMaxDegrees : maxThetaDegrees;
            float secondaryScale = applySensitivityToSecondary ? sensitivity : 1f;
            float pitchOrRoll = -(resolvedSecondaryMax * secondaryScale) * normalizedY;
            return new Vector2(yaw, pitchOrRoll);
        }

        public static Vector2 ReadMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }

            return Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.mousePosition.x, Input.mousePosition.y);
#else
            return Vector2.zero;
#endif
        }

        public static float GetAxis(
            KeyCode positiveLegacy,
            KeyCode negativeLegacy,
            Key positiveInputSystem,
            Key negativeInputSystem)
        {
#if ENABLE_INPUT_SYSTEM
            float axis = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current[positiveInputSystem].isPressed)
                {
                    axis += 1f;
                }

                if (Keyboard.current[negativeInputSystem].isPressed)
                {
                    axis -= 1f;
                }
            }

            return axis;
#elif ENABLE_LEGACY_INPUT_MANAGER
            float axis = 0f;
            if (Input.GetKey(positiveLegacy))
            {
                axis += 1f;
            }

            if (Input.GetKey(negativeLegacy))
            {
                axis -= 1f;
            }

            return axis;
#else
            return 0f;
#endif
        }

        public static bool WasDigitPressedThisFrame(int digitOneToFive)
        {
            switch (digitOneToFive)
            {
                case 1:
                    return WasPressedThisFrame(KeyCode.Alpha1, Key.Digit1);
                case 2:
                    return WasPressedThisFrame(KeyCode.Alpha2, Key.Digit2);
                case 3:
                    return WasPressedThisFrame(KeyCode.Alpha3, Key.Digit3);
                case 4:
                    return WasPressedThisFrame(KeyCode.Alpha4, Key.Digit4);
                case 5:
                    return WasPressedThisFrame(KeyCode.Alpha5, Key.Digit5);
                default:
                    return false;
            }
        }

        private static bool WasPressedThisFrame(KeyCode legacyKey, Key inputSystemKey)
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[inputSystemKey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(legacyKey);
#else
            return false;
#endif
        }
    }
}
