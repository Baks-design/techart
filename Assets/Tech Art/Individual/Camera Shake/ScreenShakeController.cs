using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ScreenShakeController : MonoBehaviour
{
    [SerializeField] private FullScreenPassRendererFeature screenShakeFeature;
    [SerializeField, Range(0.1f, 50f)] private float falloffPercentage = 5.8f;
    [SerializeField, Range(0.01f, 0.1f)] private float fallbackDeadzone = 0.0331f;
    [SerializeField] private AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    private bool isShaking = false;
    private float shakeTimer = 0f;
    private float shakeDuration = 0f;
    private float targetIntensity = 0f;
    private float targetStrengthX = 0f;
    private float targetStrengthY = 0f;
    private float initialIntensity = 0f;
    private float initialStrengthX = 0f;
    private float initialStrengthY = 0f;

    private static readonly int ShakeIntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int ShakeStrengthXID = Shader.PropertyToID("_StrengthX");
    private static readonly int ShakeStrengthYID = Shader.PropertyToID("_StrengthY");
    private static readonly int OffsetPercentageID = Shader.PropertyToID("_OffsetPercentage");

    public bool IsShaking
        => GetShakeStrengthX > fallbackDeadzone ||
           GetShakeStrengthY > fallbackDeadzone ||
           isShaking;

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
        UpdateShake();
        ApplyStrengthFalloff();
    }

    private void UpdateShake()
    {
        if (!isShaking) return;

        shakeTimer += Time.deltaTime;
        if (shakeTimer >= shakeDuration)
        {
            SetShakeIntensity(targetIntensity);
            SetShakeStrengthX(targetStrengthX);
            SetShakeStrengthY(targetStrengthY);
            isShaking = false;
            return;
        }

        var normalizedTime = shakeTimer / shakeDuration;
        var curveValue = shakeCurve.Evaluate(normalizedTime);

        var currentIntensity = Mathf.Lerp(initialIntensity, targetIntensity, curveValue);
        var currentStrengthX = Mathf.Lerp(initialStrengthX, targetStrengthX, curveValue);
        var currentStrengthY = Mathf.Lerp(initialStrengthY, targetStrengthY, curveValue);

        SetShakeIntensity(currentIntensity);
        SetShakeStrengthX(currentStrengthX);
        SetShakeStrengthY(currentStrengthY);
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

    public void StartShake(float intensity, float strengthX, float strengthY, float duration)
    {
        initialIntensity = GetShakeIntensity;
        initialStrengthX = GetShakeStrengthX;
        initialStrengthY = GetShakeStrengthY;

        targetIntensity = intensity;
        targetStrengthX = strengthX;
        targetStrengthY = strengthY;
        shakeDuration = duration;

        shakeTimer = 0f;
        isShaking = true;
    }
}