using UnityEngine;

namespace Quieter.Player
{
    public static class ClientPreferences
    {
        private const string HeadBobKey = "quieter.camera.head-bob";
        private const string MouseSensitivityKey = "quieter.input.mouse-sensitivity";
        private const string SymptomTextKey = "quieter.accessibility.symptom-text";
        private const string ScreenEffectsKey = "quieter.accessibility.screen-effects";
        private const float DefaultMouseSensitivity = 0.12f;

        public const float MinimumMouseSensitivity = 0.03f;
        public const float MaximumMouseSensitivity = 0.3f;

        public static bool HeadBobEnabled
        {
            get => PlayerPrefs.GetInt(HeadBobKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(HeadBobKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static float MouseSensitivity
        {
            get => Mathf.Clamp(
                PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultMouseSensitivity),
                MinimumMouseSensitivity,
                MaximumMouseSensitivity);
            set
            {
                PlayerPrefs.SetFloat(
                    MouseSensitivityKey,
                    Mathf.Clamp(value, MinimumMouseSensitivity, MaximumMouseSensitivity));
                PlayerPrefs.Save();
            }
        }

        public static bool SymptomTextEnabled
        {
            get => PlayerPrefs.GetInt(SymptomTextKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SymptomTextKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static bool ScreenEffectsEnabled
        {
            get => PlayerPrefs.GetInt(ScreenEffectsKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(ScreenEffectsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
