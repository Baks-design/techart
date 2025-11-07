using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class ScreenShakeRenderPass : ScriptableRenderPass
{
    private class PassData
    {
        public Material material;
        public float intensity;
        public float strengthX;
        public float strengthY;
        public float offsetPercentage;
        public bool randomShake;
        public float noiseScale;
        public float noiseSpeed;
        public float offsetX;
        public float offsetY;
        public float radiusX;
        public float radiusY;
        public float edge;
        public TextureHandle sourceTexture;
        public TextureHandle destTexture;
    }

    private Material _material;
    private ScreenShakeSettings _screenShakeSettings;
    private readonly ProfilingSampler _profilingSampler;

    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int StrengthXID = Shader.PropertyToID("_StrengthX");
    private static readonly int StrengthYID = Shader.PropertyToID("_StrengthY");
    private static readonly int OffsetPercentageID = Shader.PropertyToID("_OffsetPercentage");
    private static readonly int RandomShakeID = Shader.PropertyToID("_RandomShake");
    private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedID = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int ShapeOffsetXID = Shader.PropertyToID("_ShapeOffsetX");
    private static readonly int ShapeOffsetYID = Shader.PropertyToID("_ShapeOffsetY");
    private static readonly int ShapeRadiusXID = Shader.PropertyToID("_ShapeRadiusX");
    private static readonly int ShapeRadiusYID = Shader.PropertyToID("_ShapeRadiusY");
    private static readonly int ShapeEdgeID = Shader.PropertyToID("_ShapeEdge");

    public ScreenShakeRenderPass()
    {
        _profilingSampler = new ProfilingSampler("ScreenShake");
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public bool Setup()
    {
        _screenShakeSettings = VolumeManager.instance.stack.GetComponent<ScreenShakeSettings>();
        return _screenShakeSettings != null && _screenShakeSettings.IsActive() && EnsureMaterial();
    }

    private bool EnsureMaterial()
    {
        if (_material == null) 
            _material = new Material(Shader.Find("Custom/ScreenShake"));
        return _material != null;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer contextContainer)
    {
        if (!Setup()) return;

        var resourceData = contextContainer.Get<UniversalResourceData>();
        var cameraData = contextContainer.Get<UniversalCameraData>();

        // Create temporary texture descriptor
        var textureDesc = CreateTextureDescriptor(cameraData.cameraTargetDescriptor);

        // Two-pass approach: source -> temp -> source
        var tempTexture = renderGraph.CreateTexture(textureDesc);

        // First pass: source -> temp with screen shake
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("ScreenShake Apply", out var passData, _profilingSampler))
        {
            passData.material = _material;
            passData.intensity = _screenShakeSettings.intensity.value;
            passData.strengthX = _screenShakeSettings.shakeStrengthX.value;
            passData.strengthY = _screenShakeSettings.shakeStrengthY.value;
            passData.offsetPercentage = _screenShakeSettings.offsetPercentage.value;
            passData.randomShake = _screenShakeSettings.randomShake.value;
            passData.noiseScale = _screenShakeSettings.noiseScale.value;
            passData.noiseSpeed = _screenShakeSettings.noiseSpeed.value;
            passData.offsetX = _screenShakeSettings.offsetX.value;
            passData.offsetY = _screenShakeSettings.offsetY.value;
            passData.radiusX = _screenShakeSettings.radiusX.value;
            passData.radiusY = _screenShakeSettings.radiusY.value;
            passData.edge = _screenShakeSettings.edge.value;
            passData.sourceTexture = resourceData.activeColorTexture;
            passData.destTexture = tempTexture;

            builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
            builder.UseTexture(tempTexture, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                ExecuteBlitPass(data, context.cmd);
            });
        }

        // Second pass: temp -> source (copy back)
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("ScreenShake Copy Back", out var passData, _profilingSampler))
        {
            passData.material = _material;
            passData.intensity = _screenShakeSettings.intensity.value;
            passData.strengthX = _screenShakeSettings.shakeStrengthX.value;
            passData.strengthY = _screenShakeSettings.shakeStrengthY.value;
            passData.offsetPercentage = _screenShakeSettings.offsetPercentage.value;
            passData.randomShake = _screenShakeSettings.randomShake.value;
            passData.noiseScale = _screenShakeSettings.noiseScale.value;
            passData.noiseSpeed = _screenShakeSettings.noiseSpeed.value;
            passData.offsetX = _screenShakeSettings.offsetX.value;
            passData.offsetY = _screenShakeSettings.offsetY.value;
            passData.radiusX = _screenShakeSettings.radiusX.value;
            passData.radiusY = _screenShakeSettings.radiusY.value;
            passData.edge = _screenShakeSettings.edge.value;
            passData.sourceTexture = tempTexture;
            passData.destTexture = resourceData.activeColorTexture;

            builder.UseTexture(tempTexture, AccessFlags.Read);
            builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                ExecuteBlitPass(data, context.cmd);
            });
        }
    }

    private TextureDesc CreateTextureDescriptor(RenderTextureDescriptor cameraDesc)
    => new(cameraDesc.width, cameraDesc.height)
    {
        colorFormat = cameraDesc.graphicsFormat,
        depthBufferBits = 0,
        name = "ScreenShakeTemp"
    };

    private static void ExecuteBlitPass(PassData data, RasterCommandBuffer cmd)
    {
        data.material.SetTexture(MainTexID, data.sourceTexture);
        data.material.SetFloat(IntensityID, data.intensity);
        data.material.SetFloat(StrengthXID, data.strengthX);
        data.material.SetFloat(StrengthYID, data.strengthY);
        data.material.SetFloat(OffsetPercentageID, data.offsetPercentage);
        data.material.SetFloat(RandomShakeID, data.randomShake == true ? 1f : 0f);
        data.material.SetFloat(NoiseScaleID, data.noiseScale);
        data.material.SetFloat(NoiseSpeedID, data.noiseSpeed);
        data.material.SetFloat(ShapeOffsetXID, data.offsetX);
        data.material.SetFloat(ShapeOffsetYID, data.offsetY);
        data.material.SetFloat(ShapeRadiusXID, data.radiusX);
        data.material.SetFloat(ShapeRadiusYID, data.radiusY);
        data.material.SetFloat(ShapeEdgeID, data.edge);

        Blitter.BlitTexture(cmd, Vector4.one, data.material, 0);
    }

    public void Dispose()
    {
        if (_material == null) return;

        CoreUtils.Destroy(_material);
        _material = null;
    }
}