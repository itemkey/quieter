using Quieter.Survival;
using UnityEngine;

namespace Quieter.Player
{
    public readonly struct ConditionCameraPose
    {
        public ConditionCameraPose(Vector3 position, Vector3 rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Vector3 Rotation { get; }
    }

    public static class ConditionCameraEffects
    {
        public static float CalculateFieldOfView(float normalFieldOfView, SymptomFlags symptoms)
        {
            var target = normalFieldOfView;
            if ((symptoms & SymptomFlags.TunnelVision) != 0) target -= 12f;
            else if ((symptoms & SymptomFlags.Dizzy) != 0) target -= 4f;
            if ((symptoms & SymptomFlags.Breathless) != 0) target += 2f;
            return Mathf.Clamp(target, 35f, 90f);
        }

        public static ConditionCameraPose CalculateShake(SymptomFlags symptoms, float time)
        {
            var positionStrength = 0f;
            var rotationStrength = 0f;
            if ((symptoms & SymptomFlags.Shivering) != 0)
            {
                positionStrength += 0.008f;
                rotationStrength += 0.3f;
            }
            if ((symptoms & SymptomFlags.SeverePain) != 0)
            {
                positionStrength += 0.004f;
                rotationStrength += 0.18f;
            }
            if ((symptoms & (SymptomFlags.Dizzy | SymptomFlags.Confusion)) != 0)
            {
                positionStrength += 0.003f;
                rotationStrength += 0.22f;
            }

            if (positionStrength <= 0f && rotationStrength <= 0f)
            {
                return new ConditionCameraPose(Vector3.zero, Vector3.zero);
            }

            var slow = time * 3.1f;
            var fast = time * 10.7f;
            var position = new Vector3(
                Mathf.Sin(fast * 1.13f) * positionStrength,
                Mathf.Sin(fast * 1.71f + 0.8f) * positionStrength * 0.7f,
                0f);
            var rotation = new Vector3(
                Mathf.Sin(slow * 1.37f + 1.3f) * rotationStrength,
                Mathf.Sin(slow * 0.91f) * rotationStrength * 0.55f,
                Mathf.Sin(fast * 0.73f + 2.1f) * rotationStrength * 0.45f);
            return new ConditionCameraPose(position, rotation);
        }
    }
}
