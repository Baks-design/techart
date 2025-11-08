using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class ScreenShakeRenderPass : ScriptableRenderPass
{
    private class ShakePassData
    {
        public TextureHandle source;
        public TextureHandle tempTex;
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
    }

    private Material _material;
    private ScreenShakeSettings _screenShakeSettings;
    private readonly ProfilingSampler _profilingSampler;

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

    public ScreenShakeRenderPass() => _profilingSampler = new ProfilingSampler("Screen Shake");

    public bool Setup()
    {
        _screenShakeSettings = VolumeManager.instance.stack.GetComponent<ScreenShakeSettings>();
        if (_screenShakeSettings != null && _screenShakeSettings.IsActive())
        {
            _material = new Material(Shader.Find("Custom/ScreenShake"));
            return true;
        }
        return false;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer contextContainer)
    {
        if (_screenShakeSettings == null || !_screenShakeSettings.IsActive()) return;

        // Get rendering data and resources
        var resourceData = contextContainer.Get<UniversalResourceData>();
        var cameraData = contextContainer.Get<UniversalCameraData>();

        // Create texture descriptor for temporary texture
        var cameraTextureDesc = cameraData.cameraTargetDescriptor;
        cameraTextureDesc.depthBufferBits = 0; // We don't need depth for temp texture

        // Import the source texture (camera color)
        var source = resourceData.activeColorTexture;

        // Create temporary texture
        var tempTex = renderGraph.CreateTexture(new TextureDesc(cameraTextureDesc)
        {
            name = "_TempTex",
            clearBuffer = true
        });

        // Add the render pass
        using var builder = renderGraph.AddRasterRenderPass<ShakePassData>("Shake Pass", out var passData, _profilingSampler);

        // Setup pass data
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
        passData.source = source;
        passData.tempTex = tempTex;

        // Declare inputs and outputs
        builder.UseTexture(source, AccessFlags.Read);
        builder.SetRenderAttachment(tempTex, 0, AccessFlags.Write);

        // Set render function
        builder.SetRenderFunc((ShakePassData data, RasterGraphContext context) =>
        {
            data.material.SetFloat(IntensityID, data.intensity);
            data.material.SetFloat(StrengthXID, data.strengthX);
            data.material.SetFloat(StrengthYID, data.strengthY);
            data.material.SetFloat(OffsetPercentageID, data.offsetPercentage);
            data.material.SetFloat(RandomShakeID, data.randomShake ? 1f : 0f);
            data.material.SetFloat(NoiseScaleID, data.noiseScale);
            data.material.SetFloat(NoiseSpeedID, data.noiseSpeed);
            data.material.SetFloat(ShapeOffsetXID, data.offsetX);
            data.material.SetFloat(ShapeOffsetYID, data.offsetY);
            data.material.SetFloat(ShapeRadiusXID, data.radiusX);
            data.material.SetFloat(ShapeRadiusYID, data.radiusY);
            data.material.SetFloat(ShapeEdgeID, data.edge);

            Blitter.BlitTexture(context.cmd, data.source, Vector2.one, data.material, 0);
        });

        resourceData.cameraColor = source;
    }

    public void Dispose()
    {
        if (_material == null) return;
        CoreUtils.Destroy(_material);
        _material = null;
    }
}