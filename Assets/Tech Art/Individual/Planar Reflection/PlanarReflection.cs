#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Object = UnityEngine.Object;
using Unity.Burst;

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

    // Job-related fields
    private NativeArray<float3> _cameraLocalPosition;
    private NativeArray<bool> _isInRange;
    private NativeArray<float> _blendFactor;
    private NativeArray<float4x4> _reflectionMatrix;
    private bool _nativeArraysInitialized = false;

    public static event Action<ScriptableRenderContext, Camera> BeginPlanarReflections;

    // Job struct for camera position calculations
    [BurstCompile]
    public struct CameraPositionJob : IJob
    {
        public float3 CameraWorldPosition;
        public float4x4 VolumeWorldToLocal;
        public NativeArray<float3> CameraLocalPosition;
        public NativeArray<bool> IsInRange;
        public NativeArray<float> BlendFactor;

        public float3 HalfSize;
        public float3 BlendHalfSize;
        public float BlendDistance;

        public void Execute()
        {
            // Convert camera position to local space of volume
            var localPos = math.mul(VolumeWorldToLocal, new float4(CameraWorldPosition, 1f)).xyz;
            CameraLocalPosition[0] = localPos;

            // Check if camera is in blend range
            bool inRange = math.abs(localPos.x) <= BlendHalfSize.x &&
                          math.abs(localPos.y) <= BlendHalfSize.y &&
                          math.abs(localPos.z) <= BlendHalfSize.z;
            IsInRange[0] = inRange;

            // Calculate blend factor
            if (BlendDistance <= 0f)
            {
                bool inVolume = math.abs(localPos.x) <= HalfSize.x &&
                               math.abs(localPos.y) <= HalfSize.y &&
                               math.abs(localPos.z) <= HalfSize.z;
                BlendFactor[0] = inVolume ? 0f : 1f;
            }
            else
            {
                var distanceX = math.max(0f, math.abs(localPos.x) - HalfSize.x);
                var distanceY = math.max(0f, math.abs(localPos.y) - HalfSize.y);
                var distanceZ = math.max(0f, math.abs(localPos.z) - HalfSize.z);

                var maxDistance = math.max(distanceX, math.max(distanceY, distanceZ));
                BlendFactor[0] = maxDistance <= 0f ? 0f : math.clamp(maxDistance / BlendDistance, 0f, 1f);
            }
        }
    }

    // Job struct for reflection matrix calculations
    [BurstCompile]
    public struct ReflectionMatrixJob : IJob
    {
        public float4 ReflectionPlane;
        public NativeArray<float4x4> ReflectionMatrix;

        public void Execute()
        {
            float x = ReflectionPlane.x, y = ReflectionPlane.y, z = ReflectionPlane.z, w = ReflectionPlane.w;

            var matrix = new float4x4(
                1F - 2F * x * x, -2F * x * y, -2F * x * z, -2F * w * x,
                -2F * y * x, 1F - 2F * y * y, -2F * y * z, -2F * w * y,
                -2F * z * x, -2F * z * y, 1F - 2F * z * z, -2F * w * z,
                0F, 0F, 0F, 1F
            );

            ReflectionMatrix[0] = matrix;
        }
    }

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
        InitializeNativeArrays();
    }

    private void InitializeNativeArrays()
    {
        if (!_nativeArraysInitialized)
        {
            // Initialize native arrays for job communication
            _cameraLocalPosition = new NativeArray<float3>(1, Allocator.Persistent);
            _isInRange = new NativeArray<bool>(1, Allocator.Persistent);
            _blendFactor = new NativeArray<float>(1, Allocator.Persistent);
            _reflectionMatrix = new NativeArray<float4x4>(1, Allocator.Persistent);
            _nativeArraysInitialized = true;
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += DoPlanarReflections;
        CacheVolumeBounds();
        UpdateTargetMaterial();
        InitializeNativeArrays();
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
        DisposeNativeArrays();
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= DoPlanarReflections;
        CleanUp();
        ResetMaterialBlend();
        DisposeNativeArrays();
    }

    private void DisposeNativeArrays()
    {
        if (_nativeArraysInitialized)
        {
            if (_cameraLocalPosition.IsCreated) _cameraLocalPosition.Dispose();
            if (_isInRange.IsCreated) _isInRange.Dispose();
            if (_blendFactor.IsCreated) _blendFactor.Dispose();
            if (_reflectionMatrix.IsCreated) _reflectionMatrix.Dispose();
            _nativeArraysInitialized = false;
        }
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

        // Ensure native arrays are initialized
        if (!_nativeArraysInitialized)
            InitializeNativeArrays();

        // Check if native arrays are valid
        if (!_cameraLocalPosition.IsCreated || !_isInRange.IsCreated || !_blendFactor.IsCreated)
            return;

        // Schedule camera position job
        var cameraPosJob = new CameraPositionJob
        {
            CameraWorldPosition = camera.transform.position,
            VolumeWorldToLocal = _transform.worldToLocalMatrix,
            CameraLocalPosition = _cameraLocalPosition,
            IsInRange = _isInRange,
            BlendFactor = _blendFactor,
            HalfSize = _halfSize,
            BlendHalfSize = _blendHalfSize,
            BlendDistance = blendDistance
        };

        cameraPosJob.Run(); // Run immediately on main thread

        bool isInRange = _isInRange[0];
        float blendFactor = _blendFactor[0];

        if (!isInRange && _wasInRange)
        {
            _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
            _wasInRange = false;
            return;
        }

        _targetMaterial.SetFloat(_planarReflectionBlendId, blendFactor);
        _wasInRange = isInRange;

        if (blendFactor >= 1f) return;

        UpdateReflectionCameraWithJobs(camera, _cameraLocalPosition[0]);
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

    private void UpdateReflectionCameraWithJobs(Camera realCamera, float3 cameraLocalPos)
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

        // Check if reflection matrix array is valid
        if (!_reflectionMatrix.IsCreated)
            return;

        // Schedule reflection matrix job
        var matrixJob = new ReflectionMatrixJob
        {
            ReflectionPlane = reflectionPlane,
            ReflectionMatrix = _reflectionMatrix
        };

        matrixJob.Run(); // Run immediately on main thread

        // Convert float4x4 to Matrix4x4 for compatibility with Unity's Camera API
        var reflection = ConvertToMatrix4x4(_reflectionMatrix[0]);

        var oldPosition = realCamera.transform.position - new Vector3(0f, pos.y * 2f, 0f);
        _reflectionCamera.transform.position = ReflectPosition(oldPosition);
        _reflectionCamera.transform.forward = Vector3.Scale(realCamera.transform.forward, new Vector3(1f, -1f, 1f));
        _reflectionCamera.worldToCameraMatrix = realCamera.worldToCameraMatrix * reflection;

        var clipPlane = CameraSpacePlane(_reflectionCamera, pos - Vector3.up * 0.1f, normal, 1f);
        _reflectionCamera.projectionMatrix = realCamera.CalculateObliqueMatrix(clipPlane);
        _reflectionCamera.cullingMask = reflectionLayer;
    }

    // Helper method to convert float4x4 to Matrix4x4
    private Matrix4x4 ConvertToMatrix4x4(float4x4 f4x4)
    {
        return new Matrix4x4(
            new Vector4(f4x4.c0.x, f4x4.c0.y, f4x4.c0.z, f4x4.c0.w),
            new Vector4(f4x4.c1.x, f4x4.c1.y, f4x4.c1.z, f4x4.c1.w),
            new Vector4(f4x4.c2.x, f4x4.c2.y, f4x4.c2.z, f4x4.c2.w),
            new Vector4(f4x4.c3.x, f4x4.c3.y, f4x4.c3.z, f4x4.c3.w)
        );
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
            if (sceneView == null || !sceneView.hasFocus || sceneView.in2DMode)
                return false;
        }

        if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0 || !_reflectionTexture.IsCreated())
            return false;

        return true;
    }
#endif

    private bool ShouldRenderReflectionTarget()
    {
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