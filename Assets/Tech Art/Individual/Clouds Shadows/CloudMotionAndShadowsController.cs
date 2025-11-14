using UnityEngine;

[RequireComponent(typeof(Light))]
public class CloudMotionAndShadowsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Light directionalLight;
    [Header("Cloud")]
    [SerializeField] private Vector2 cloudDistortionSpeedDir = new(-0.01f, 0.01f);
    [SerializeField] private Vector2 cloudLayerSpeedDir = new(0.003f, 0.001f);
    [Header("Shadows")]
    [SerializeField] private bool isShadowsCurrentActive = false;
    [SerializeField] private bool isBackAndForth = false;
    [SerializeField] private float cloudShadowsSpeed = 0.005f;
    [SerializeField] private float cloudShadowsSineWaveSpeed = 2f;
    private Material skyboxMaterial;
    private Material cookieMaterial;
    private CustomRenderTexture customRenderTexture;
    private static readonly int CloudDistortionSpeedOffsetId = Shader.PropertyToID("_CloudDistortionSpeedOffset");
    private static readonly int CloudLayerSpeedOffsetId = Shader.PropertyToID("_CloudLayerSpeedOffset");
    private static readonly int CloudShadowsSpeedId = Shader.PropertyToID("_CloudShadowsSpeed");

    private void Awake() => Setup();

    private void Setup()
    {
        if (directionalLight == null)
            TryGetComponent(out directionalLight);
        if (directionalLight != null)
        {
            customRenderTexture = directionalLight.cookie as CustomRenderTexture;
            if (customRenderTexture != null)
                cookieMaterial = customRenderTexture.material;
            else if (customRenderTexture == null && isShadowsCurrentActive)
                Debug.LogWarning("Directional Light's cookie is not a CustomRenderTexture", this);
        }
        else
            Debug.LogError("No directional light assigned or found!", this);

        skyboxMaterial = RenderSettings.skybox;

        if (skyboxMaterial == null)
            Debug.LogWarning("No skybox material found in RenderSettings", this);
    }

    private void Update()
    {
        UpdateSkyCloud();
        UpdateCloudShadows();
    }

    private void UpdateSkyCloud()
    {
        var cloudDistortion = new Vector2(
            Mathf.Repeat(Time.time * cloudDistortionSpeedDir.x, 1f),
            Mathf.Repeat(Time.time * cloudDistortionSpeedDir.y, 1f)
        );
        if (skyboxMaterial.HasProperty(CloudDistortionSpeedOffsetId))
            skyboxMaterial.SetVector(CloudDistortionSpeedOffsetId, cloudDistortion);

        var cloudLayer = new Vector2(
            Mathf.Repeat(Time.time * cloudLayerSpeedDir.x, 1f),
            Mathf.Repeat(Time.time * cloudLayerSpeedDir.y, 1f)
        );
        if (skyboxMaterial.HasProperty(CloudLayerSpeedOffsetId))
            skyboxMaterial.SetVector(CloudLayerSpeedOffsetId, cloudLayer);
    }

    private void UpdateCloudShadows()
    {
        if (!isShadowsCurrentActive) return;
        
        var motionResult = isBackAndForth ?
            Mathf.Sin(Time.time * cloudShadowsSineWaveSpeed) *
            cloudShadowsSpeed :
            Time.time * cloudShadowsSpeed;
        cookieMaterial.SetFloat(CloudShadowsSpeedId, motionResult);
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (skyboxMaterial != null)
            {
                if (skyboxMaterial.HasProperty(CloudDistortionSpeedOffsetId))
                    skyboxMaterial.SetVector(CloudDistortionSpeedOffsetId, Vector2.zero);
                if (skyboxMaterial.HasProperty(CloudLayerSpeedOffsetId))
                    skyboxMaterial.SetVector(CloudLayerSpeedOffsetId, Vector2.zero);
            }

            if (cookieMaterial != null && cookieMaterial.HasProperty(CloudShadowsSpeedId))
                cookieMaterial.SetFloat(CloudShadowsSpeedId, 0f);
        }
#endif
    }
}