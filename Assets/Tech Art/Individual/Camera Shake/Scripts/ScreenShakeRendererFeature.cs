using UnityEngine.Rendering.Universal;

public class ScreenShakeRendererFeature : ScriptableRendererFeature
{
    private ScreenShakeRenderPass _screenShakeRenderPass;

    public override void Create()
    {
        _screenShakeRenderPass = new ScreenShakeRenderPass();
        name = "ScreenShake";
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_screenShakeRenderPass.Setup())
            renderer.EnqueuePass(_screenShakeRenderPass);
    }

    protected override void Dispose(bool disposing)
    {
        _screenShakeRenderPass?.Dispose();
        _screenShakeRenderPass = null;
    }
}