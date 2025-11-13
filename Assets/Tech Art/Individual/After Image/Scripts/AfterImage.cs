using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class AfterImageEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private GameObject presetObj;
    [Header("Pool")]
    [SerializeField] private int poolSize = 10;
    [SerializeField] private float delay = 0.04f;
    [SerializeField] private float disableDelay = 3f;
    [Header("Settings")]
    [SerializeField] private float fadeSpeed = 3f;
    [SerializeField] private bool randomColor = false;
    private SkinnedMeshRenderer[] skinRenderers;
    private MeshRenderer[] meshRenderers;
    private CombineInstance[] combine;
    private UnityEngine.Pool.ObjectPool<AfterImageInstance> objectPool;
    private MeshFilter[] meshFilters;
    private Material[] originalMaterials;
    private Transform myTransform;
    private Matrix4x4 matrix;
    private float timer;
    private bool hasSkinRenderers = false;
    private bool hasMeshRenderers = false;
    private readonly int _FadeId = Shader.PropertyToID("_Fade");
    private readonly int _ColorId = Shader.PropertyToID("_Color");

    private class AfterImageInstance
    {
        public GameObject gameObject;
        public MeshFilter meshFilter;
        public Renderer renderer;
        public float fadeTimer;
        public float disableTime;
        public bool scheduledForDisable;
        public Coroutine fadeCoroutine;

        public void Reset()
        {
            fadeTimer = 0f;
            disableTime = 0f;
            scheduledForDisable = false;
            gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        GetComponents();
        SetUpRenderers();
        InitializeObjectPool();
    }

    private void GetComponents()
    {
        myTransform = transform;
        if (!presetObj) Debug.LogWarning($"Preset object not assigned on {gameObject.name}");
        if (!playerMovement) playerMovement = GetComponentInParent<PlayerMovement>();
        skinRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        meshRenderers = GetComponentsInChildren<MeshRenderer>();
    }

    private void SetUpRenderers()
    {
        hasSkinRenderers = skinRenderers.Length > 0;
        hasMeshRenderers = meshRenderers.Length > 0;

        meshFilters = new MeshFilter[meshRenderers.Length];
        for (var i = 0; i < meshRenderers.Length; i++)
            meshRenderers[i].TryGetComponent(out meshFilters[i]);

        combine = new CombineInstance[skinRenderers.Length + meshRenderers.Length];
        for (var i = 0; i < skinRenderers.Length; i++)
            combine[i].mesh = new Mesh { name = $"BakedMesh_{i}" };

        originalMaterials = new Material[skinRenderers.Length + meshRenderers.Length];
        for (var i = 0; i < skinRenderers.Length; i++)
            originalMaterials[i] = skinRenderers[i].material;
        for (var i = 0; i < meshRenderers.Length; i++)
            originalMaterials[skinRenderers.Length + i] = meshRenderers[i].material;
    }

    private void InitializeObjectPool()
    {
        objectPool = new UnityEngine.Pool.ObjectPool<AfterImageInstance>(
            createFunc: CreatePooledItem,
            actionOnGet: OnTakeFromPool,
            actionOnRelease: OnReturnedToPool,
            actionOnDestroy: OnDestroyPoolObject,
            defaultCapacity: poolSize,
            maxSize: poolSize * 2 
        );

        var prewarmInstances = new List<AfterImageInstance>();
        for (var i = 0; i < poolSize; i++)
            prewarmInstances.Add(objectPool.Get());
        foreach (var instance in prewarmInstances)
            objectPool.Release(instance);
    }

    private AfterImageInstance CreatePooledItem()
    {
        var obj = Instantiate(presetObj);
        obj.name = $"AfterImage_{objectPool.CountAll }";

        var meshFilter = obj.GetComponent<MeshFilter>();
        var renderer = obj.GetComponent<Renderer>();

        var material = new Material(renderer.material) { enableInstancing = true };
        renderer.material = material;

        meshFilter.mesh = new Mesh
        {
            name = $"AfterImageMesh_{objectPool.CountAll}",
            indexFormat = IndexFormat.UInt16
        };

        return new AfterImageInstance
        {
            gameObject = obj,
            meshFilter = meshFilter,
            renderer = renderer
        };
    }

    private void OnTakeFromPool(AfterImageInstance instance) => instance.gameObject.SetActive(true);

    private void OnReturnedToPool(AfterImageInstance instance)
    {
        instance.Reset();
        if (randomColor)
            instance.renderer.material.SetColor(_ColorId, Color.white);
    }

    private void OnDestroyPoolObject(AfterImageInstance instance)
    {
        if (instance.meshFilter != null && instance.meshFilter.mesh != null)
            Destroy(instance.meshFilter.mesh);
        if (instance.gameObject != null)
            Destroy(instance.gameObject);
    }

    private void LateUpdate()
    {
        timer -= Time.deltaTime;
        if (timer < 0f &&
            playerMovement.IsMoving &&
            playerMovement.IsRunning)
        {
            timer = delay;
            CreateAfterImage();
        }
    }

    private void CreateAfterImage()
    {
        var instance = objectPool.Get();
        if (instance == null || !hasSkinRenderers && !hasMeshRenderers) return;

        SetupAfterImageMesh(instance);
        SetupAfterImageAppearance(instance);
        PositionAfterImage(instance);

        if (instance.fadeCoroutine != null)
            StopCoroutine(instance.fadeCoroutine);
        instance.fadeCoroutine = StartCoroutine(HandleAfterImageFade(instance));
    }

    private void SetupAfterImageMesh(AfterImageInstance instance)
    {
        matrix = myTransform.worldToLocalMatrix;
        var combineIndex = 0;

        if (hasSkinRenderers)
        {
            for (var i = 0; i < skinRenderers.Length; i++)
            {
                var mesh = combine[combineIndex].mesh;
                skinRenderers[i].BakeMesh(mesh, true);
                combine[combineIndex].transform = matrix * skinRenderers[i].localToWorldMatrix;
                combineIndex++;
            }
        }
        if (hasMeshRenderers)
        {
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                combine[combineIndex].mesh = meshFilters[i].sharedMesh;
                combine[combineIndex].transform = matrix * meshRenderers[i].transform.localToWorldMatrix;
                combineIndex++;
            }
        }

        var targetMesh = instance.meshFilter.mesh;
        targetMesh.Clear();
        targetMesh.CombineMeshes(combine, true);
    }

    private void SetupAfterImageAppearance(AfterImageInstance instance)
    {
        instance.fadeTimer = 1f;
        instance.renderer.material.SetFloat(_FadeId, instance.fadeTimer);

        if (randomColor)
        {
            var randomColorValue = new Color(
                Random.value * 0.5f + 0.5f,
                Random.value * 0.5f + 0.5f,
                Random.value * 0.5f + 0.5f
            );
            instance.renderer.material.SetColor(_ColorId, randomColorValue);
        }

        instance.scheduledForDisable = disableDelay > (1f / fadeSpeed);
        instance.disableTime = Time.time + disableDelay;
    }

    private void PositionAfterImage(AfterImageInstance instance) 
    => instance.gameObject.transform.SetPositionAndRotation(myTransform.position, myTransform.rotation);

    private IEnumerator HandleAfterImageFade(AfterImageInstance instance)
    {
        while (instance.fadeTimer > 0f && instance.gameObject.activeInHierarchy)
        {
            instance.fadeTimer -= Time.deltaTime * fadeSpeed;
            instance.renderer.material.SetFloat(_FadeId, instance.fadeTimer);

            if (instance.scheduledForDisable && Time.time >= instance.disableTime)
                break;

            yield return null;
        }

        objectPool.Release(instance);
    }

    private void OnDestroy()
    {
        if (combine == null) return;

        foreach (var combineInstance in combine)
            if (combineInstance.mesh != null)
                Destroy(combineInstance.mesh);
    }
}