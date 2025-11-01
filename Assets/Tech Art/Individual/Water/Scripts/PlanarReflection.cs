#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Effects/Planar Reflection Volume")]
public class PlanarReflectionVolume : MonoBehaviour
{
    [Header("Planar Settings")]
    [Range(0.01f, 1f)] public float renderScale = 1f;
    public LayerMask reflectionLayer = -1;
    public bool reflectSkybox = true;
    public GameObject reflectionTarget;
    [Range(-2f, 3f)] public float reflectionPlaneOffset;
    public bool hideReflectionCamera;

    [Header("Volume Settings")]
    public Vector3 volumeSize = new(10f, 10f, 10f);
    [Min(0f)] public float blendDistance = 2f;

    private Material _targetMaterial;
    private RenderTextureDescriptor _previousDescriptor;
    private static Camera _reflectionCamera;
    private static RenderTexture _reflectionTexture;
    private readonly int _planarReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTexture");
    private readonly int _planarReflectionBlendId = Shader.PropertyToID("_PlannerReflectionBlend");

    public static event Action<ScriptableRenderContext, Camera> BeginPlanarReflections;

    private void OnValidate() => UpdateBounds();

    private void UpdateBounds() => new Bounds(transform.position, volumeSize);

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += DoPlanarReflections;

        reflectionLayer = ~(1 << 4);

        UpdateBounds();

        // Get the material from the reflection target
        if (reflectionTarget != null && reflectionTarget.TryGetComponent<Renderer>(out var renderer))
            _targetMaterial = renderer.sharedMaterial;
    }

    private void OnDisable()
    {
        CleanUp();

        RenderPipelineManager.beginCameraRendering -= DoPlanarReflections;

        // Reset blend parameter on the specific material when disabled
        if (_targetMaterial != null)
            _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
    }

    private void OnDestroy()
    {
        CleanUp();

        RenderPipelineManager.beginCameraRendering -= DoPlanarReflections;

        // Reset blend parameter on the specific material when destroyed
        if (_targetMaterial != null)
            _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
    }

    private void UpdateTargetMaterial()
    {
        if (reflectionTarget == null ||
            _targetMaterial != null ||
            !reflectionTarget.TryGetComponent<Renderer>(out var renderer))
            return;

        _targetMaterial = renderer.sharedMaterial;
    }


    private float GetBlendFactor(Camera camera)
    {
        if (blendDistance <= 0f) return IsCameraInVolume(camera) ? 0f : 1f;

        // Transform camera position to local space
        var cameraLocalPos = transform.InverseTransformPoint(camera.transform.position);
        var halfSize = volumeSize * 0.5f;

        // Calculate distance from each boundary
        var distanceX = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.x) - halfSize.x);
        var distanceY = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.y) - halfSize.y);
        var distanceZ = Mathf.Max(0f, Mathf.Abs(cameraLocalPos.z) - halfSize.z);

        // Get the maximum distance from any boundary
        var maxDistance = Mathf.Max(distanceX, Mathf.Max(distanceY, distanceZ));
        // If inside volume
        if (maxDistance <= 0f) return 0f;

        // Calculate blend factor
        return Mathf.Clamp01(maxDistance / blendDistance);
    }

    private bool IsCameraInRange(Camera camera)
    {
        var cameraLocalPos = transform.InverseTransformPoint(camera.transform.position);
        var halfSize = volumeSize * 0.5f + new Vector3(blendDistance, blendDistance, blendDistance);
        return Mathf.Abs(cameraLocalPos.x) <= halfSize.x &&
               Mathf.Abs(cameraLocalPos.y) <= halfSize.y &&
               Mathf.Abs(cameraLocalPos.z) <= halfSize.z;
    }

    private bool IsCameraInVolume(Camera camera)
    {
        var cameraLocalPos = transform.InverseTransformPoint(camera.transform.position);
        var halfSize = volumeSize * 0.5f;
        return Mathf.Abs(cameraLocalPos.x) <= halfSize.x &&
               Mathf.Abs(cameraLocalPos.y) <= halfSize.y &&
               Mathf.Abs(cameraLocalPos.z) <= halfSize.z;
    }

    private void CleanUp()
    {
        if (_reflectionCamera)
        {
            _reflectionCamera.targetTexture = null;
            SafeDestroyObject(_reflectionCamera.gameObject);
        }

        if (_reflectionTexture)
            RenderTexture.ReleaseTemporary(_reflectionTexture);
    }

    private void SafeDestroyObject(UnityEngine.Object obj)
    {
        if (Application.isEditor)
            DestroyImmediate(obj);
        else
            Destroy(obj);
    }

    private void UpdateReflectionCamera(Camera realCamera)
    {
        if (_reflectionCamera == null)
            _reflectionCamera = InitializeReflectionCamera();
        else if (_reflectionCamera.gameObject.hideFlags != (hideReflectionCamera 
            ? HideFlags.HideAndDontSave : HideFlags.DontSave))
        {
            // Update hide flags if they've changed
            _reflectionCamera.gameObject.hideFlags = hideReflectionCamera 
                ? HideFlags.HideAndDontSave : HideFlags.DontSave;
#if UNITY_EDITOR
            // Force update the hierarchy in editor
            EditorApplication.DirtyHierarchyWindowSorting();
#endif
        }

        var pos = Vector3.zero;
        var normal = Vector3.up;

        if (reflectionTarget != null)
        {
            pos = reflectionTarget.transform.position + Vector3.up * reflectionPlaneOffset;
            normal = reflectionTarget.transform.up;
        }

        UpdateCamera(realCamera, _reflectionCamera);
        _reflectionCamera.gameObject.hideFlags = hideReflectionCamera 
            ? HideFlags.HideAndDontSave : HideFlags.DontSave;

        var d = -Vector3.Dot(normal, pos);
        var reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        var reflection = Matrix4x4.identity;
        reflection *= Matrix4x4.Scale(new Vector3(1f, -1f, 1f));

        CalculateReflectionMatrix(ref reflection, reflectionPlane);
        var oldPosition = realCamera.transform.position - new Vector3(0f, pos.y * 2f, 0f);
        var newPosition = ReflectPosition(oldPosition);
        _reflectionCamera.transform.forward = Vector3.Scale(realCamera.transform.forward, new Vector3(1f, -1f, 1f));
        _reflectionCamera.worldToCameraMatrix = realCamera.worldToCameraMatrix * reflection;

        var clipPlane = CameraSpacePlane(_reflectionCamera, pos - Vector3.up * 0.1f, normal, 1f);
        var projection = realCamera.CalculateObliqueMatrix(clipPlane);
        _reflectionCamera.projectionMatrix = projection;
        _reflectionCamera.cullingMask = reflectionLayer;
        _reflectionCamera.transform.position = newPosition;
    }

    private static Vector3 ReflectPosition(Vector3 pos)
    {
        var reflectPos = new Vector3(pos.x, -pos.y, pos.z);
        return reflectPos;
    }

    private void UpdateCamera(Camera src, Camera dest)
    {
        if (dest == null) return;

        dest.CopyFrom(src);
        dest.useOcclusionCulling = false;
        if (dest.gameObject.TryGetComponent(out UniversalAdditionalCameraData camData))
        {
            camData.renderShadows = false;
            if (reflectSkybox)
                dest.clearFlags = CameraClearFlags.Skybox;
            else
            {
                dest.clearFlags = CameraClearFlags.SolidColor;
                dest.backgroundColor = Color.black;
            }
        }
    }

    private Camera InitializeReflectionCamera()
    {
        var go = new GameObject("", typeof(Camera));
        go.name = "Reflection Camera [" + go.GetInstanceID() + "]";

        var camData = go.AddComponent(typeof(UniversalAdditionalCameraData)) as UniversalAdditionalCameraData;
        camData.requiresColorOption = CameraOverrideOption.Off;
        camData.requiresDepthOption = CameraOverrideOption.Off;
        camData.SetRenderer(0);

        var t = transform;
        var reflectionCamera = go.GetComponent<Camera>();
        reflectionCamera.transform.SetPositionAndRotation(t.position, t.rotation);
        reflectionCamera.depth = -10f;
        reflectionCamera.enabled = false;
        return reflectionCamera;
    }

    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        var m = cam.worldToCameraMatrix;
        var cameraPosition = m.MultiplyPoint(pos);
        var cameraNormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
    }

    private RenderTextureDescriptor GetDescriptor(Camera camera, float pipelineRenderScale)
    {
        var width = (int)Mathf.Max(camera.pixelWidth * pipelineRenderScale * renderScale);
        var height = (int)Mathf.Max(camera.pixelHeight * pipelineRenderScale * renderScale);
        var hdr = camera.allowHDR;
        var renderTextureFormat = hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        return new RenderTextureDescriptor(width, height, renderTextureFormat, 16)
        {
            autoGenerateMips = true,
            useMipMap = true
        };
    }

    private void CreateReflectionTexture(Camera camera)
    {
        var descriptor = GetDescriptor(camera, UniversalRenderPipeline.asset.renderScale);

        if (_reflectionTexture == null)
        {
            _reflectionTexture = RenderTexture.GetTemporary(descriptor);
            _previousDescriptor = descriptor;
        }
        else if (!descriptor.Equals(_previousDescriptor))
        {
            if (_reflectionTexture) RenderTexture.ReleaseTemporary(_reflectionTexture);

            _reflectionTexture = RenderTexture.GetTemporary(descriptor);
            _previousDescriptor = descriptor;
        }
        _reflectionCamera.targetTexture = _reflectionTexture;
    }

    private void DoPlanarReflections(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType == CameraType.Reflection ||
            camera.cameraType == CameraType.Preview ||
            !reflectionTarget) return;

        // Update material reference if needed
        UpdateTargetMaterial();
        if (_targetMaterial == null) return;

        // Check if camera is in range (volume + blend distance)
        if (!IsCameraInRange(camera))
        {
            _targetMaterial.SetFloat(_planarReflectionBlendId, 1f);
            return;
        }

        // Calculate and set blend factor on the specific material
        var blendFactor = GetBlendFactor(camera);
        _targetMaterial.SetFloat(_planarReflectionBlendId, blendFactor);
        // If fully blended to probe reflections, don't render planar reflection
        if (blendFactor >= 1f) return;

        UpdateReflectionCamera(camera);
        CreateReflectionTexture(camera);

        var data = new PlanarReflectionSettingData();
        data.Set();
        BeginPlanarReflections?.Invoke(context, _reflectionCamera);

        if (_reflectionCamera.WorldToViewportPoint(reflectionTarget.transform.position).z < 100000f)
#pragma warning disable CS0618 
            UniversalRenderPipeline.RenderSingleCamera(context, _reflectionCamera);
#pragma warning restore CS0618

        data.Restore();
        Shader.SetGlobalTexture(_planarReflectionTextureId, _reflectionTexture);
    }

    public static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMatrix, Vector4 plane)
    {
        reflectionMatrix.m00 = 1F - 2F * plane[0] * plane[0];
        reflectionMatrix.m01 = -2F * plane[0] * plane[1];
        reflectionMatrix.m02 = -2F * plane[0] * plane[2];
        reflectionMatrix.m03 = -2F * plane[3] * plane[0];

        reflectionMatrix.m10 = -2F * plane[1] * plane[0];
        reflectionMatrix.m11 = 1F - 2F * plane[1] * plane[1];
        reflectionMatrix.m12 = -2F * plane[1] * plane[2];
        reflectionMatrix.m13 = -2F * plane[3] * plane[1];

        reflectionMatrix.m20 = -2F * plane[2] * plane[0];
        reflectionMatrix.m21 = -2F * plane[2] * plane[1];
        reflectionMatrix.m22 = 1F - 2F * plane[2] * plane[2];
        reflectionMatrix.m23 = -2F * plane[3] * plane[2];

        reflectionMatrix.m30 = 0F;
        reflectionMatrix.m31 = 0F;
        reflectionMatrix.m32 = 0F;
        reflectionMatrix.m33 = 1F;
    }

    private void OnDrawGizmos()
    {
        // Draw inner volume
        Gizmos.color = new Color(0f, 1f, 1f, 0f);
        Gizmos.matrix = transform.localToWorldMatrix;
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

    private class PlanarReflectionSettingData
    {
        private readonly bool fog;
        private readonly int maximumLODLevel;
        private readonly float lodBias;

        public PlanarReflectionSettingData()
        {
            fog = RenderSettings.fog;
            maximumLODLevel = QualitySettings.maximumLODLevel;
            lodBias = QualitySettings.lodBias;
        }

        public void Set()
        {
            GL.invertCulling = true;
            RenderSettings.fog = false;
            QualitySettings.maximumLODLevel = 1;
            QualitySettings.lodBias = lodBias * 0.5f;
        }

        public void Restore()
        {
            GL.invertCulling = false;
            RenderSettings.fog = fog;
            QualitySettings.maximumLODLevel = maximumLODLevel;
            QualitySettings.lodBias = lodBias;
        }
    }
}