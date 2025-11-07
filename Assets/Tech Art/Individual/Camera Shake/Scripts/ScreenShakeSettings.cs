using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable, VolumeComponentMenu("Custom/ScreenShake")]
public class ScreenShakeSettings : VolumeComponent, IPostProcessComponent
{
    [Header("Shake Power")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter shakeStrengthX = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter shakeStrengthY = new(0f, 0f, 1f, true);
    [Header("Shake Behaviour")]
    public BoolParameter randomShake = new(false);
    public NoInterpClampedFloatParameter noiseScale = new(0f, 0f, 100f, true);
    public NoInterpClampedFloatParameter noiseSpeed = new(0f, 0f, 100f, true);
    public NoInterpClampedFloatParameter offsetPercentage = new(0f, 0f, 100f, true);
    [Header("Shake Shape")]
    public NoInterpClampedFloatParameter offsetX = new(0f, -2f, 2f, true);
    public NoInterpClampedFloatParameter offsetY = new(0f, 2f, 2f, true);
    public NoInterpClampedFloatParameter radiusX = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter radiusY = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter edge = new(0f, 0.01f, 2f, true);

    public bool IsActive() => active;

    public bool IsTileCompatible() => false;
}
