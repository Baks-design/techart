using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[RequireComponent(typeof(Volume))]
public class ScreenShakeController : MonoBehaviour
{
    [SerializeField, Range(0.1f, 50f)] private float fallbackPercentage;
    [SerializeField, Range(0.01f, 0.1f)] private float fallbackDeadzone;
    [SerializeField] private Volume volume;
    private ScreenShakeSettings screenShakeSettings;

    private void Start()
    {
        if (volume == null) TryGetComponent(out volume);
        volume.profile.TryGet(out screenShakeSettings);
    }

    private void Update()
    {
        ShakeStrengthFallback();

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            ShakeScreen(1f, 1f);
    }

    private void ShakeStrengthFallback()
    {
        if (screenShakeSettings == null) return;

        if (screenShakeSettings.shakeStrengthX.value > fallbackDeadzone)
            screenShakeSettings.shakeStrengthX.value *= (100f - fallbackDeadzone) * 0.01f;
        else
            screenShakeSettings.shakeStrengthX.value = 0f;

        if (screenShakeSettings.shakeStrengthY.value > fallbackDeadzone)
            screenShakeSettings.shakeStrengthY.value *= (100f - fallbackDeadzone) * 0.01f;
        else
            screenShakeSettings.shakeStrengthY.value = 0f;
    }

    public void SetShakeSettings(float offsetPercentage)
    => screenShakeSettings.offsetPercentage.value = offsetPercentage;

    public void ShakeScreen(float shakeStrengthX, float shakeStrengthY)
    {
        screenShakeSettings.shakeStrengthX.value += shakeStrengthX;
        screenShakeSettings.shakeStrengthY.value += shakeStrengthY;
    }
}