using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ScreenShakeRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private RenderPassEvent _renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    private ScreenShakeRenderPass _screenShakeRenderPass;

    public override void Create()
    {
        name = "Screen Shake";
        _screenShakeRenderPass = new ScreenShakeRenderPass { renderPassEvent = _renderPassEvent };
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