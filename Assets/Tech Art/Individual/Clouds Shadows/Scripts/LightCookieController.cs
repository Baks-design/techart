using UnityEngine;

[ExecuteAlways]
public class LightCookieController : MonoBehaviour
{
    [SerializeField] private Light directionalLight;
    [SerializeField] private float cloudeMoveSpeed = 0.002f;
    [SerializeField] private float sineWaveSpeed = 2f;
    private Material cookieMaterial;
    private CustomRenderTexture customRenderTexture;
    private static readonly int _MoveSpeedId = Shader.PropertyToID("_MoveSpeed");
    private static readonly int _SineWaveSpeedId = Shader.PropertyToID("_SineWaveSpeed");

    void Start() => Setup();

    private void Setup()
    {
        if (directionalLight == null) return;

        customRenderTexture = directionalLight.cookie as CustomRenderTexture;
        if (customRenderTexture != null)
            cookieMaterial = customRenderTexture.material;
    }

    private void Update() => UpdateCloudValues();

    private void UpdateCloudValues()
    {
        SetCookieFloat(_MoveSpeedId, cloudeMoveSpeed);
        SetCookieFloat(_SineWaveSpeedId, sineWaveSpeed);
    }

    private void SetCookieFloat(int propertyName, float value)
    {
        if (cookieMaterial == null || !cookieMaterial.HasProperty(propertyName)) return;

        cookieMaterial.SetFloat(propertyName, value);
    }
}