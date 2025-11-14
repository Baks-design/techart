using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SetRipplesEffect : MonoBehaviour
{
    [Header("Reference Settings")]
    [SerializeField] private RenderTexture rt;
    [SerializeField] private Transform target;
    [SerializeField] private ParticleSystem ripplesParticleSystem;
    [Header("Camera Settings")]
    [SerializeField] private LayerMask cullingMask = 1 << 8; // Assuming "Ripples" is layer 8
    private Camera _camera;
    private Vector3 _lastTargetPosition;
    private bool _isParticlesPlaying = false;
    private const float movementThreshold = 0.01f;
    private readonly int _GlobalEffectRTId = Shader.PropertyToID("_GlobalEffectRT");
    private readonly int _OrthographicCamSizeId = Shader.PropertyToID("_OrthographicCamSize");
    private readonly int _PositionId = Shader.PropertyToID("_Position");

    private void Awake()
    {
        InitializeCamera();
        ValidateComponents();
        SetComponents();
    }

    private void InitializeCamera()
    {
        var go = new GameObject($"Ripples Camera [{GetInstanceID()}]", typeof(Camera))
        {
            hideFlags = HideFlags.HideInHierarchy
        };
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));

        var camData = go.AddComponent<UniversalAdditionalCameraData>();
        camData.requiresColorOption = CameraOverrideOption.On;
        camData.requiresDepthOption = CameraOverrideOption.On;
        camData.renderShadows = false;
        camData.SetRenderer(0);

        _camera = go.GetComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 15f;
        _camera.nearClipPlane = 0.3f;
        _camera.farClipPlane = 100f;
        _camera.cullingMask = cullingMask;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.clear;
        _camera.depth = -5;
        _camera.enabled = true;
        _camera.targetTexture = rt;
    }

    private void ValidateComponents()
    {
        if (rt == null)
            Debug.LogError("RenderTexture is not assigned in the inspector!", this);
        if (_camera == null)
            Debug.LogError("Camera component not found and failed to create!", this);
        if (ripplesParticleSystem == null)
            Debug.LogError("Ripples Particle System is not assigned in the inspector!", this);
        if (target == null)
            Debug.LogWarning("Target is not assigned. Using self as target.", this);
    }

    private void SetComponents()
    {
        Shader.SetGlobalTexture(_GlobalEffectRTId, rt);
        Shader.SetGlobalFloat(_OrthographicCamSizeId, _camera.orthographicSize);

        if (ripplesParticleSystem != null)
            ripplesParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Adjust orthographic size to maintain proper coverage
        var cameraAspect = _camera.aspect;
        var rtAspect = (float)rt.width / rt.height;
        if (Mathf.Abs(cameraAspect - rtAspect) > 0.01f)
        {
            var originalSize = _camera.orthographicSize;
            if (cameraAspect > rtAspect)
                _camera.orthographicSize = originalSize * (cameraAspect / rtAspect);
            else
                _camera.orthographicSize = originalSize * (rtAspect / cameraAspect);

            Shader.SetGlobalFloat(_OrthographicCamSizeId, _camera.orthographicSize);
        }
        else
            Shader.SetGlobalFloat(_OrthographicCamSizeId, _camera.orthographicSize);
    }

    private void Start() => InitPosition();

    private void InitPosition() => _lastTargetPosition = target.position;

    private void LateUpdate() => UpdateRipples();

    private void UpdateRipples()
    {
        var currentTargetPosition = target.position;

        Shader.SetGlobalVector(_PositionId, new Vector4(currentTargetPosition.x, currentTargetPosition.y, currentTargetPosition.z, 0f));

        var movementSqrMagnitude = Vector3.SqrMagnitude(currentTargetPosition - _lastTargetPosition);
        if (movementSqrMagnitude > movementThreshold * movementThreshold)
        {
            if (!_isParticlesPlaying)
            {
                ripplesParticleSystem.Play();
                _isParticlesPlaying = true;
            }
        }
        else
        {
            if (_isParticlesPlaying)
            {
                ripplesParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _isParticlesPlaying = false;
            }
        }

        _lastTargetPosition = currentTargetPosition;
    }

    private void OnDisable() => ResetSettings();

    private void OnDestroy()
    {
        if (_camera != null) 
            Destroy(_camera.gameObject);
        ResetSettings();
    }

    private void ResetSettings()
    {
        Shader.SetGlobalTexture(_GlobalEffectRTId, null);
        Shader.SetGlobalVector(_PositionId, Vector4.zero);

        ripplesParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _isParticlesPlaying = false;
    }
}