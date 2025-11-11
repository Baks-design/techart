using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

public class ScreenShakeController : MonoBehaviour
{
    [SerializeField] FullScreenPassRendererFeature screenShakeFeature;
    [SerializeField, Range(0.1f, 50f)] private float falloffPercentage = 5.8f;
    [SerializeField, Range(0.01f, 0.1f)] private float fallbackDeadzone = 0.0331f;
    [SerializeField] private AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    private Coroutine currentShakeCoroutine;
    private bool isShakeCoroutineRunning = false;

    private static readonly int ShakeIntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int ShakeStrengthXID = Shader.PropertyToID("_StrengthX");
    private static readonly int ShakeStrengthYID = Shader.PropertyToID("_StrengthY");
    private static readonly int OffsetPercentageID = Shader.PropertyToID("_OffsetPercentage");

    public bool IsShaking
    => GetShakeStrengthX > fallbackDeadzone ||
        GetShakeStrengthY > fallbackDeadzone ||
        isShakeCoroutineRunning;

    private void Start() => Initialize();

    private void Initialize()
    {
        if (screenShakeFeature.passMaterial == null)
        {
            Debug.LogError("screenShakeFeature.passMaterial is not assigned!");
            return;
        }

        if (!screenShakeFeature.passMaterial.HasProperty(ShakeStrengthXID))
            Debug.LogWarning($"screenShakeFeature.passMaterial doesn't have property: _ShakeStrengthX");
        if (!screenShakeFeature.passMaterial.HasProperty(ShakeStrengthYID))
            Debug.LogWarning($"screenShakeFeature.passMaterial doesn't have property: _ShakeStrengthY");
        if (!screenShakeFeature.passMaterial.HasProperty(OffsetPercentageID))
            Debug.LogWarning($"screenShakeFeature.passMaterial doesn't have property: _OffsetPercentage");
    }

    private void Update()
    {
        HandleDebugInput();
        ApplyStrengthFalloff();
    }

    private void HandleDebugInput()
    {
        if (!Keyboard.current.spaceKey.wasPressedThisFrame) return;

        StartShakeCoroutine(1f, 1f, 1f, 0.5f);
    }

    private void ApplyStrengthFalloff()
    {
        var currentStrengthX = GetShakeStrengthX;
        var currentStrengthY = GetShakeStrengthY;

        if (currentStrengthX > fallbackDeadzone)
        {
            var newStrengthX = currentStrengthX * (100f - falloffPercentage) * 0.01f;
            if (newStrengthX <= fallbackDeadzone) newStrengthX = 0f;
            SetShakeStrengthX(newStrengthX);
        }

        if (currentStrengthY > fallbackDeadzone)
        {
            var newStrengthY = currentStrengthY * (100f - falloffPercentage) * 0.01f;
            if (newStrengthY <= fallbackDeadzone) newStrengthY = 0f;
            SetShakeStrengthY(newStrengthY);
        }
    }

    #region Gets
    private float GetShakeIntensity
    => screenShakeFeature.passMaterial.HasProperty(ShakeIntensityID) ?
        screenShakeFeature.passMaterial.GetFloat(ShakeIntensityID) : 0f;
    private float GetShakeStrengthX
    => screenShakeFeature.passMaterial.HasProperty(ShakeStrengthXID) ?
        screenShakeFeature.passMaterial.GetFloat(ShakeStrengthXID) : 0f;
    private float GetShakeStrengthY
    => screenShakeFeature.passMaterial.HasProperty(ShakeStrengthYID) ?
        screenShakeFeature.passMaterial.GetFloat(ShakeStrengthYID) : 0f;
    #endregion

    #region Sets
    private void SetShakeIntensity(float value)
    {
        if (screenShakeFeature.passMaterial.HasProperty(ShakeIntensityID))
            screenShakeFeature.passMaterial.SetFloat(ShakeIntensityID, value);
    }
    private void SetShakeStrengthX(float value)
    {
        if (screenShakeFeature.passMaterial.HasProperty(ShakeStrengthXID))
            screenShakeFeature.passMaterial.SetFloat(ShakeStrengthXID, value);
    }
    private void SetShakeStrengthY(float value)
    {
        if (screenShakeFeature.passMaterial.HasProperty(ShakeStrengthYID))
            screenShakeFeature.passMaterial.SetFloat(ShakeStrengthYID, value);
    }
    #endregion

    #region Calls
    public void StartShakeCoroutine(float targetIntensity, float shakeStrengthX, float shakeStrengthY, float duration)
    {
        if (currentShakeCoroutine != null)
        {
            StopCoroutine(currentShakeCoroutine);
            isShakeCoroutineRunning = false;
        }

        currentShakeCoroutine = StartCoroutine(ShakeScreenRoutine(targetIntensity, shakeStrengthX, shakeStrengthY, duration));
    }

    private IEnumerator ShakeScreenRoutine(float targetIntensity, float targetStrengthX, float targetStrengthY, float duration)
    {
        isShakeCoroutineRunning = true;
        var elapsedTime = 0f;

        var initialIntensity = GetShakeIntensity;
        var initialStrengthX = GetShakeStrengthX;
        var initialStrengthY = GetShakeStrengthY;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            var normalizedTime = elapsedTime / duration;
            var curveValue = shakeCurve.Evaluate(normalizedTime);

            var currentIntensity = Mathf.Lerp(initialIntensity, targetIntensity, curveValue);
            var currentStrengthX = Mathf.Lerp(initialStrengthX, targetStrengthX, curveValue);
            var currentStrengthY = Mathf.Lerp(initialStrengthY, targetStrengthY, curveValue);

            SetShakeIntensity(currentIntensity);
            SetShakeStrengthX(currentStrengthX);
            SetShakeStrengthY(currentStrengthY);

            yield return null;
        }

        SetShakeIntensity(targetIntensity);
        SetShakeStrengthX(targetStrengthX);
        SetShakeStrengthY(targetStrengthY);

        isShakeCoroutineRunning = false;
        currentShakeCoroutine = null;
    }
    #endregion
}