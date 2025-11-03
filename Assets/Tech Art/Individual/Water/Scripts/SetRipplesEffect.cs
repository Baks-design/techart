#if UNITY_EDITOR
#endif
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SetRipplesEffect : MonoBehaviour
{
    [SerializeField] private RenderTexture rt;
    [SerializeField] private Transform target;

    private Camera _camera;
    private Transform _transform;

    private readonly int _GlobalEffectRTId = Shader.PropertyToID("_GlobalEffectRT");
    private readonly int _OrthographicCamSizeId = Shader.PropertyToID("_OrthographicCamSize");
    private readonly int _PositionId = Shader.PropertyToID("_Position");

    private void Awake()
    {
        _transform = transform;
        if (rt == null)
            Debug.LogError("RenderTexture is not assigned in the inspector!", this);

        _camera = GetComponent<Camera>();
        if (_camera == null)
            Debug.LogError("Camera component not found!", this);
        if (!_camera.orthographic)
            Debug.LogWarning("Camera is not orthographic. _OrthographicCamSize might not behave as expected.", this);

        // Set shader properties
        Shader.SetGlobalTexture(_GlobalEffectRTId, rt);
        Shader.SetGlobalFloat(_OrthographicCamSizeId, _camera.orthographicSize);
    }

    private void LateUpdate() => UpdateRipples();

    private void UpdateRipples()
    {
        if (target == null)
        {
            Debug.LogError("Target transform is not assigned!", this);
            return;
        }

        var targetPosition = target.position;
        _transform.position = new Vector3(targetPosition.x, _transform.position.y, targetPosition.z);

        Shader.SetGlobalVector(_PositionId, _transform.position);
    }

    private void OnDisable()
    {
        Shader.SetGlobalTexture(_GlobalEffectRTId, null);
        Shader.SetGlobalVector(_PositionId, Vector3.zero);
    }
}