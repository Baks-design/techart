using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable, VolumeComponentMenu("Custom/ScreenShake")]
public class ScreenShakeSettings : VolumeComponent, IPostProcessComponent
{
    [Header("Shake Power")]
    public ClampedFloatParameter intensity = new(1f, 0f, 1f, true);
    public NoInterpClampedFloatParameter shakeStrengthX = new(1f, 0f, 1f, true);
    public NoInterpClampedFloatParameter shakeStrengthY = new(1f, 0f, 1f, true);

    [Header("Shake Behaviour")]
    public BoolParameter randomShake = new(true);
    public NoInterpClampedFloatParameter noiseScale = new(15f, 0f, 100f, true);
    public NoInterpClampedFloatParameter noiseSpeed = new(9f, 0f, 100f, true);
    public NoInterpClampedFloatParameter offsetPercentage = new(2f, 0f, 100f, true);

    [Header("Shake Shape")]
    public NoInterpClampedFloatParameter offsetX = new(0f, -2f, 2f, true);
    public NoInterpClampedFloatParameter offsetY = new(0f, -2f, 2f, true); 
    public NoInterpClampedFloatParameter radiusX = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter radiusY = new(0f, 0f, 1f, true);
    public NoInterpClampedFloatParameter edge = new(0.01f, 0.01f, 2f, true);

    public bool IsActive() => intensity.value > 0f && active;

    public bool IsTileCompatible() => false;
}