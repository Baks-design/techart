using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[RequireComponent(typeof(Volume))]
public class ScreenShakeController : MonoBehaviour
{
    [SerializeField, Range(0.1f, 50f)] private float falloffPercentage = 5f;
    [SerializeField, Range(0.01f, 0.1f)] private float fallbackDeadzone = 0.05f;
    [SerializeField] private Volume volume;
    private ScreenShakeSettings screenShakeSettings;

    public bool IsShaking
    => screenShakeSettings.shakeStrengthX.value > fallbackDeadzone ||
        screenShakeSettings.shakeStrengthY.value > fallbackDeadzone;

    private void Start() => Initialize();

    private void Initialize()
    {
        if (volume == null && !TryGetComponent(out volume) ||
            volume.profile == null ||
            !volume.profile.TryGet(out screenShakeSettings))
            return;
    }

    private void Update()
    {
        ApplyStrengthFalloff();
        HandleDebugInput();
    }

    private void ApplyStrengthFalloff()
    {
        if (screenShakeSettings.shakeStrengthX.value > fallbackDeadzone)
        {
            screenShakeSettings.shakeStrengthX.value *= (100f - falloffPercentage) * 0.01f;

            if (screenShakeSettings.shakeStrengthX.value <= fallbackDeadzone)
                screenShakeSettings.shakeStrengthX.value = 0f;
        }
        else
            screenShakeSettings.shakeStrengthX.value = 0f;

        if (screenShakeSettings.shakeStrengthY.value > fallbackDeadzone)
        {
            screenShakeSettings.shakeStrengthY.value *= (100f - falloffPercentage) * 0.01f;

            if (screenShakeSettings.shakeStrengthY.value <= fallbackDeadzone)
                screenShakeSettings.shakeStrengthY.value = 0f;
        }
        else
            screenShakeSettings.shakeStrengthY.value = 0f;
    }

    private void HandleDebugInput()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("asa");
            ShakeScreen(1f, 1f);
        }
    }

    private void ShakeScreen(float shakeStrengthX, float shakeStrengthY)
    {
        screenShakeSettings.shakeStrengthX.value = Mathf.Clamp01(
            screenShakeSettings.shakeStrengthX.value + shakeStrengthX);
        screenShakeSettings.shakeStrengthY.value = Mathf.Clamp01(
            screenShakeSettings.shakeStrengthY.value + shakeStrengthY);
    }

    public void SetShakeSettings(float offsetPercentage) 
    => screenShakeSettings.offsetPercentage.value = Mathf.Clamp(offsetPercentage, 0f, 100f);
}