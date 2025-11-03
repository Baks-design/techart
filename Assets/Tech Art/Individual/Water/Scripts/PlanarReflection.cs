#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Effects/Planar Reflection Volume")]
public class PlanarReflectionVolume : MonoBehaviour
{
    [Header("Reflection Settings")]
    [SerializeField] private GameObject reflectionTarget;
    [SerializeField] private LayerMask reflectionLayer = -1;
    [SerializeField, Range(0.01f, 1f)] private float renderScale = 1f;
    [SerializeField] private bool hideReflectionCamera = true;
    [SerializeField] private bool reflectSkybox = true;
    [SerializeField, Range(-2f, 3f)] private float reflectionPlaneOffset = 0f;

    [Header("Volume Settings")]
    [SerializeField] private Vector3 volumeSize = new(10f, 10f, 10f);
    [SerializeField, Min(0f)] private float blendDistance = 2f;

    private bool _wasInRange;
    private Transform _transform;
    private Vector3 _halfSize;
    private Vector3 _blendHalfSize;
    private Material _targetMaterial;
    private Renderer _targetRenderer;
    private RenderTextureDescriptor _previousDescriptor;
    private static Camera _reflectionCamera;
    private static RenderTexture _reflectionTexture;
    private readonly int _planarReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTexture");
    private readonly int _planarReflectionBlendId = Shader.PropertyToID("_PlannerReflectionBlend");

    public static event Action<ScriptableRenderContext, Camera> BeginPlanarReflections;

    private void OnValidate()
    {
        CacheVolumeBounds();

        if (_targetRenderer == null || _targetRenderer.gameObject != reflectionTarget)
            UpdateTargetMaterial();
    }

    private void Awake()
    {
        _transform = transform;
        CacheVolumeBounds();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += DoPlanarReflections;
        CacheVolumeBounds();
        UpdateTargetMaterial();
    }

    private void CacheVolumeBounds()
    {
        _halfSize = volumeSize * 0.5f;
        var blendExpansion = new Vector3(blendDistance, blendDistance, blendDistance);
        _blendHalfSize = _halfSize + blendExpansion;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= DoPlanarReflections;
        CleanUp();
        ResetMaterialBlend();
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= DoPlanarReflections;
        CleanUp();
        ResetMaterialBlend();
    }

    private void CleanUp()
    {
        if (_reflectionCamera != null)
        {
            _reflectionCamera.targetTexture = null;
            SafeDestroyObject(_reflectionCamera.gameObject);
            _reflectionCamera = null;
        }

        if (_reflectionTexture != null)
        {
            RenderTexture.ReleaseTemporary(_reflectionTexture);
            _reflectionTexture = null;
        }
    }

    private void SafeDestroyObject(Object obj)
    {
        if (obj == null) return;

        if (Application.isEditor)
            DestroyImmediate(obj);
        else
            Destroy(obj);
    }

    private void ResetMaterialBlend()
    {
        if (_targetMaterial == null) return;
        _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
    }

    private void DoPlanarReflections(ScriptableRenderContext context, Camera camera)
    {
        if (ShouldSkipRender(camera)) return;

        UpdateTargetMaterial();
        if (_targetMaterial == null) return;

        var isInRange = IsCameraInRange(camera);
        var blendFactor = GetBlendFactor(camera);

        if (!isInRange && _wasInRange)
        {
            _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
            _wasInRange = false;
            return;
        }

        _targetMaterial.SetFloat(_planarReflectionBlendId, blendFactor);
        _wasInRange = isInRange;

        if (blendFactor >= 1f) return;

        UpdateReflectionCamera(camera);
        CreateReflectionTexture(camera);

        if (_reflectionTexture == null || _reflectionCamera == null) return;

#if UNITY_EDITOR
        if (!IsValidForRendering(_reflectionCamera))
            return;
#endif

        using (var settings = new PlanarReflectionSettingData())
        {
            BeginPlanarReflections?.Invoke(context, _reflectionCamera);

            if (ShouldRenderReflectionTarget())
            {
                try
                {
#pragma warning disable CS0618
                    UniversalRenderPipeline.RenderSingleCamera(context, _reflectionCamera);
#pragma warning restore CS0618
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to render planar reflection: {e.Message}");
                }
            }
        }

        Shader.SetGlobalTexture(_planarReflectionTextureId, _reflectionTexture);
    }

    private bool ShouldSkipRender(Camera camera)
    => camera == null ||
        camera.cameraType == CameraType.Reflection ||
        camera.cameraType == CameraType.Preview ||
        camera.cameraType == CameraType.SceneView ||
        reflectionTarget == null;

    private void UpdateTargetMaterial()
    {
        _targetMaterial = null;
        _targetRenderer = null;

        if (reflectionTarget == null) return;

        if (reflectionTarget.TryGetComponent(out _targetRenderer))
            _targetMaterial = _targetRenderer.sharedMaterial;
    }

    private bool IsCameraInRange(Camera camera)
    {
        if (camera == null) return false;

        var cameraLocalPos = _transform.InverseTransformPoint(camera.transform.position);

        return Mathf.Abs(cameraLocalPos.x) <= _blendHalfSize.x &&
               Mathf.Abs(cameraLocalPos.y) <= _blendHalfSize.y &&
               Mathf.Abs(cameraLocalPos.z) <= _blendHalfSize.z;
    }

    private float GetBlendFactor(Camera camera)
    {
        if (camera == null) return 1f;

        if (blendDistance <= 0f) return IsCameraInVolume(camera) ? 0f : 1f;

        var cameraLocalPos = _transform.InverseTransformPoint(camera.transform.position);

        var distanceX = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.x) - _halfSize.x);
        var distanceY = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.y) - _halfSize.y);
        var distanceZ = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.z) - _halfSize.z);

        var maxDistance = Mathf.Max(distanceX, Mathf.Max(distanceY, distanceZ));
        if (maxDistance <= 0f) return 0f;

        return Mathf.Clamp01(maxDistance / blendDistance);
    }

    private bool IsCameraInVolume(Camera camera)
    {
        if (camera == null) return false;

        var cameraLocalPos = _transform.InverseTransformPoint(camera.transform.position);

        return Mathf.Abs(cameraLocalPos.x) <= _halfSize.x &&
               Mathf.Abs(cameraLocalPos.y) <= _halfSize.y &&
               Mathf.Abs(cameraLocalPos.z) <= _halfSize.z;
    }

    private void UpdateReflectionCamera(Camera realCamera)
    {
        if (realCamera == null) return;

        if (_reflectionCamera == null)
            _reflectionCamera = InitializeReflectionCamera();

        UpdateCameraHideFlags();
        UpdateCamera(realCamera, _reflectionCamera);

        var reflectionTransform = reflectionTarget != null ? reflectionTarget.transform : _transform;
        var pos = reflectionTransform.position + reflectionTransform.up * reflectionPlaneOffset;
        var normal = reflectionTransform.up;

        var d = -Vector3.Dot(normal, pos);
        var reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        var reflection = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
        CalculateReflectionMatrix(ref reflection, reflectionPlane);

        var oldPosition = realCamera.transform.position - new Vector3(0f, pos.y * 2f, 0f);
        _reflectionCamera.transform.position = ReflectPosition(oldPosition);
        _reflectionCamera.transform.forward = Vector3.Scale(realCamera.transform.forward, new Vector3(1f, -1f, 1f));
        _reflectionCamera.worldToCameraMatrix = realCamera.worldToCameraMatrix * reflection;

        var clipPlane = CameraSpacePlane(_reflectionCamera, pos - Vector3.up * 0.1f, normal, 1f);
        _reflectionCamera.projectionMatrix = realCamera.CalculateObliqueMatrix(clipPlane);
        _reflectionCamera.cullingMask = reflectionLayer;
    }

    private void CreateReflectionTexture(Camera camera)
    {
        if (camera == null || _reflectionCamera == null) return;

        var descriptor = GetDescriptor(camera);

        if (_reflectionTexture == null || !descriptor.Equals(_previousDescriptor))
        {
            if (_reflectionTexture != null)
                RenderTexture.ReleaseTemporary(_reflectionTexture);

            _reflectionTexture = RenderTexture.GetTemporary(descriptor);
            _previousDescriptor = descriptor;
        }

        _reflectionCamera.targetTexture = _reflectionTexture;
        _reflectionCamera.forceIntoRenderTexture = true;
    }

    private RenderTextureDescriptor GetDescriptor(Camera camera)
    {
        if (camera == null || UniversalRenderPipeline.asset == null)
            return new RenderTextureDescriptor(256, 256, RenderTextureFormat.Default, 16);

        var pipelineRenderScale = UniversalRenderPipeline.asset.renderScale;
        var width = (int)Mathf.Max(camera.pixelWidth * pipelineRenderScale * renderScale, 4f);
        var height = (int)Mathf.Max(camera.pixelHeight * pipelineRenderScale * renderScale, 4f);
        var hdr = camera.allowHDR;
        var renderTextureFormat = hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

        return new RenderTextureDescriptor(width, height, renderTextureFormat, 16)
        {
            autoGenerateMips = true,
            useMipMap = true,
            depthBufferBits = 16,
            msaaSamples = 1
        };
    }

    private Camera InitializeReflectionCamera()
    {
        var go = new GameObject($"Reflection Camera [{GetInstanceID()}]", typeof(Camera));

        var camData = go.AddComponent<UniversalAdditionalCameraData>();
        camData.requiresColorOption = CameraOverrideOption.On;
        camData.requiresDepthOption = CameraOverrideOption.On;
        camData.renderShadows = false;
        camData.SetRenderer(0);

        var reflectionCamera = go.GetComponent<Camera>();
        reflectionCamera.transform.SetPositionAndRotation(_transform.position, _transform.rotation);
        reflectionCamera.depth = -10f;
        reflectionCamera.enabled = false;
        if (reflectSkybox)
            reflectionCamera.clearFlags = CameraClearFlags.Skybox;
        else
        {
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor = Color.clear;
        }
        return reflectionCamera;
    }

    private void UpdateCameraHideFlags()
    {
        if (_reflectionCamera == null) return;

        var newHideFlags = hideReflectionCamera ? HideFlags.HideAndDontSave : HideFlags.DontSave;
        if (_reflectionCamera.gameObject.hideFlags != newHideFlags)
        {
            _reflectionCamera.gameObject.hideFlags = newHideFlags;
#if UNITY_EDITOR
            EditorApplication.DirtyHierarchyWindowSorting();
#endif
        }
    }

    private void UpdateCamera(Camera src, Camera dest)
    {
        if (src == null || dest == null) return;

        dest.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
        dest.fieldOfView = src.fieldOfView;
        dest.nearClipPlane = src.nearClipPlane;
        dest.farClipPlane = src.farClipPlane;
        dest.orthographic = src.orthographic;
        dest.orthographicSize = src.orthographicSize;
        dest.aspect = src.aspect;
        dest.useOcclusionCulling = false;
        if (dest.TryGetComponent(out UniversalAdditionalCameraData camData))
        {
            camData.renderShadows = false;
            camData.requiresColorOption = CameraOverrideOption.On;
            camData.requiresDepthOption = CameraOverrideOption.On;

            if (reflectSkybox)
                dest.clearFlags = CameraClearFlags.Skybox;
            else
            {
                dest.clearFlags = CameraClearFlags.SolidColor;
                dest.backgroundColor = Color.clear;
            }
        }
    }

    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMatrix, Vector4 plane)
    {
        float x = plane.x, y = plane.y, z = plane.z, w = plane.w;

        reflectionMatrix.m00 = 1F - 2F * x * x;
        reflectionMatrix.m01 = -2F * x * y;
        reflectionMatrix.m02 = -2F * x * z;
        reflectionMatrix.m03 = -2F * w * x;

        reflectionMatrix.m10 = -2F * y * x;
        reflectionMatrix.m11 = 1F - 2F * y * y;
        reflectionMatrix.m12 = -2F * y * z;
        reflectionMatrix.m13 = -2F * w * y;

        reflectionMatrix.m20 = -2F * z * x;
        reflectionMatrix.m21 = -2F * z * y;
        reflectionMatrix.m22 = 1F - 2F * z * z;
        reflectionMatrix.m23 = -2F * w * z;

        reflectionMatrix.m30 = 0F;
        reflectionMatrix.m31 = 0F;
        reflectionMatrix.m32 = 0F;
        reflectionMatrix.m33 = 1F;
    }

    private static Vector3 ReflectPosition(Vector3 pos) => new(pos.x, -pos.y, pos.z);

    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        if (cam == null) return new Vector4();

        var m = cam.worldToCameraMatrix;
        var cameraPosition = m.MultiplyPoint(pos);
        var cameraNormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
    }

#if UNITY_EDITOR
    private bool IsValidForRendering(Camera camera)
    {
        if (camera == null) return false;

        if (camera.cameraType == CameraType.SceneView)
        {
            var sceneView = SceneView.currentDrawingSceneView;
            if (sceneView == null) return false;

            if (!sceneView.hasFocus || sceneView.in2DMode) return false;
        }

        if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return false;

        if (_reflectionTexture == null || !_reflectionTexture.IsCreated()) return false;

        return true;
    }
#endif

    private bool ShouldRenderReflectionTarget()
    {
        if (_reflectionCamera == null || reflectionTarget == null) return false;

        var viewportPoint = _reflectionCamera.WorldToViewportPoint(reflectionTarget.transform.position);
        return viewportPoint.z < 100000f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_transform == null) _transform = transform;

        // Draw inner volume
        Gizmos.color = new Color(0f, 1f, 1f, 0f);
        Gizmos.matrix = _transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, volumeSize);

        // Draw inner volume wireframe
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, volumeSize);

        // Draw blend volume wireframe
        if (blendDistance > 0f)
        {
            Gizmos.color = new Color(0f, 0.5f, 0.5f, 0.5f);
            var blendSize = volumeSize + new Vector3(blendDistance * 2f, blendDistance * 2f, blendDistance * 2f);
            Gizmos.DrawWireCube(Vector3.zero, blendSize);
        }
    }
#endif

    private class PlanarReflectionSettingData : IDisposable
    {
        private readonly bool _fog;
        private readonly int _maximumLODLevel;
        private readonly float _lodBias;

        public PlanarReflectionSettingData()
        {
            _fog = RenderSettings.fog;
            _maximumLODLevel = QualitySettings.maximumLODLevel;
            _lodBias = QualitySettings.lodBias;

            ApplySettings();
        }

        private void ApplySettings()
        {
            GL.invertCulling = true;
            RenderSettings.fog = false;
            QualitySettings.maximumLODLevel = 1;
            QualitySettings.lodBias = _lodBias * 0.5f;
        }

        public void Dispose()
        {
            GL.invertCulling = false;
            RenderSettings.fog = _fog;
            QualitySettings.maximumLODLevel = _maximumLODLevel;
            QualitySettings.lodBias = _lodBias;
        }
    }
}
