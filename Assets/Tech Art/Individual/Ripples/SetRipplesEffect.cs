using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SetRipplesEffect : MonoBehaviour
{
    [SerializeField] private RenderTexture rt;
    [SerializeField] private Transform target;
    [SerializeField] private bool updateEveryFrame = true;

    private Camera _camera;
    private Transform _transform;
    private Vector3 _lastPosition;

    private static readonly int _GlobalEffectRTId = Shader.PropertyToID("_GlobalEffectRT");
    private static readonly int _OrthographicCamSizeId = Shader.PropertyToID("_OrthographicCamSize");
    private static readonly int _PositionId = Shader.PropertyToID("_Position");

    private void Awake()
    {
        _transform = transform;
        _camera = GetComponent<Camera>();

        ValidateComponents();
        SetupShaderProperties();
    }

    private void ValidateComponents()
    {
        if (rt == null)
        {
            Debug.LogError("RenderTexture is not assigned in the inspector!", this);
            enabled = false;
            return;
        }
        if (_camera == null)
        {
            Debug.LogError("Camera component not found!", this);
            enabled = false;
            return;
        }
        if (!_camera.orthographic)
            Debug.LogWarning("Camera is not orthographic. _OrthographicCamSize might not behave as expected.", this);
        if (target == null)
        {
            Debug.LogWarning("Target is not assigned. Using self as target.", this);
            target = _transform;
        }
    }

    private void SetupShaderProperties()
    {
        Shader.SetGlobalTexture(_GlobalEffectRTId, rt);
        Shader.SetGlobalFloat(_OrthographicCamSizeId, _camera.orthographicSize);

        UpdateRipples();
        _lastPosition = _transform.position;
    }

    private void LateUpdate()
    {
        if (!updateEveryFrame && !PositionChanged()) return;
        
        UpdateRipples();
    }

    private bool PositionChanged() => Vector3.SqrMagnitude(_transform.position - _lastPosition) > 0.0001f;

    private void UpdateRipples()
    {
        var targetPosition = target.position;
        _transform.position = new Vector3(targetPosition.x, _transform.position.y, targetPosition.z);

        Shader.SetGlobalVector(_PositionId, _transform.position);
        _lastPosition = _transform.position;
    }

    private void OnDisable()
    {
        Shader.SetGlobalTexture(_GlobalEffectRTId, null);
        Shader.SetGlobalVector(_PositionId, Vector4.zero); 
    }
}