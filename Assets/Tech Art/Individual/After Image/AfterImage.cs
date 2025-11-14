using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Pool = UnityEngine.Pool;

public class AfterImageEffect : MonoBehaviour
{
    private sealed class AfterImageInstance
    {
        public GameObject gameObject;
        public MeshFilter meshFilter;
        public Renderer renderer;
        public float fadeTimer;
        public float disableTime;
        public bool scheduledForDisable;

        public void Reset()
        {
            fadeTimer = 0f;
            disableTime = 0f;
            scheduledForDisable = false;
            gameObject.SetActive(false);
        }
    }

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
    private Pool.ObjectPool<AfterImageInstance> objectPool;
    private MeshFilter[] meshFilters;
    private Material[] originalMaterials;
    private Transform myTransform;
    private Matrix4x4 matrix;
    private float timer;
    private bool hasSkinRenderers = false;
    private bool hasMeshRenderers = false;
    private readonly int _FadeId = Shader.PropertyToID("_Fade");
    private readonly int _ColorId = Shader.PropertyToID("_Color");
    private readonly List<AfterImageInstance> activeInstances = new();
    private NativeArray<float> fadeTimers;
    private NativeArray<float> disableTimes;
    private NativeArray<bool> scheduledForDisableFlags;
    private NativeArray<bool> instancesToRemove;
    private NativeArray<float> fadeValues;
    private JobHandle fadeUpdateJobHandle;
    private bool fadeUpdateJobScheduled = false;

    private void Start()
    {
        GetComponents();
        SetUpRenderers();
        InitializeObjectPool();
        InitializeJobData();
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

    private void InitializeJobData()
    {
        var capacity = poolSize * 2;
        fadeTimers = new NativeArray<float>(capacity, Allocator.Persistent);
        disableTimes = new NativeArray<float>(capacity, Allocator.Persistent);
        scheduledForDisableFlags = new NativeArray<bool>(capacity, Allocator.Persistent);
        instancesToRemove = new NativeArray<bool>(capacity, Allocator.Persistent);
        fadeValues = new NativeArray<float>(capacity, Allocator.Persistent);
    }

    private void InitializeObjectPool()
    {
        objectPool = new(
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
        var poolParent = new GameObject("AfterImagePool").transform;
        var obj = Instantiate(presetObj);
        obj.name = $"AfterImage_{objectPool.CountAll}";
        obj.transform.SetParent(poolParent);

        var renderer = obj.GetComponent<Renderer>();
        var material = new Material(renderer.material) { enableInstancing = true };
        renderer.material = material;

        var meshFilter = obj.GetComponent<MeshFilter>();
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
        // Complete any scheduled fade jobs
        if (fadeUpdateJobScheduled)
        {
            fadeUpdateJobHandle.Complete();
            fadeUpdateJobScheduled = false;
            ApplyFadeJobResults();
        }

        // Update fades
        if (activeInstances.Count > 0)
        {
            // Only use jobs when we have enough instances to benefit from parallelism
            if (activeInstances.Count >= 8)
                UpdateFadesWithJobs();
            else
                UpdateAfterImageFadesMainThread();
        }

        timer -= Time.deltaTime;
        if (timer < 0f && playerMovement.IsMoving && playerMovement.IsRunning)
        {
            timer = delay;
            CreateAfterImage();
        }
    }

    private void UpdateFadesWithJobs()
    {
        // Prepare data for fade update job
        for (var i = 0; i < activeInstances.Count; i++)
        {
            fadeTimers[i] = activeInstances[i].fadeTimer;
            disableTimes[i] = activeInstances[i].disableTime;
            scheduledForDisableFlags[i] = activeInstances[i].scheduledForDisable;
        }

        // Schedule fade update job
        var fadeJob = new UpdateFadeJob
        {
            deltaTime = Time.deltaTime,
            fadeSpeed = fadeSpeed,
            currentTime = Time.time,
            fadeTimers = fadeTimers,
            disableTimes = disableTimes,
            scheduledForDisableFlags = scheduledForDisableFlags,
            instancesToRemove = instancesToRemove,
            fadeValues = fadeValues,
            instanceCount = activeInstances.Count
        };

        fadeUpdateJobHandle = fadeJob.Schedule();
        fadeUpdateJobScheduled = true;
    }

    private void ApplyFadeJobResults()
    {
        // Apply fade results and remove instances that need to be removed
        for (var i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (i < fadeTimers.Length)
            {
                // Update instance data from job results
                activeInstances[i].fadeTimer = fadeTimers[i];
                activeInstances[i].renderer.material.SetFloat(_FadeId, fadeValues[i]);

                // Check if instance should be removed
                if (instancesToRemove[i])
                {
                    var instance = activeInstances[i];
                    activeInstances.RemoveAt(i);
                    objectPool.Release(instance);
                }
            }
        }
    }

    private void UpdateAfterImageFadesMainThread()
    {
        for (var i = activeInstances.Count - 1; i >= 0; i--)
        {
            var instance = activeInstances[i];

            if (!instance.gameObject.activeInHierarchy)
            {
                activeInstances.RemoveAt(i);
                objectPool.Release(instance);
                continue;
            }

            instance.fadeTimer -= Time.deltaTime * fadeSpeed;
            instance.renderer.material.SetFloat(_FadeId, instance.fadeTimer);

            if (instance.fadeTimer <= 0f ||
                (instance.scheduledForDisable && Time.time >= instance.disableTime))
            {
                activeInstances.RemoveAt(i);
                objectPool.Release(instance);
            }
        }
    }

    private void CreateAfterImage()
    {
        var instance = objectPool.Get();
        if (instance == null || !hasSkinRenderers && !hasMeshRenderers) return;

        SetupAfterImageMesh(instance);
        SetupAfterImageAppearance(instance);
        PositionAfterImage(instance);

        activeInstances.Add(instance);
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
            var hue = Random.value;
            var saturation = Random.Range(0.7f, 1f);
            var value = Random.Range(0.8f, 1f);
            var randomColorValue = Color.HSVToRGB(hue, saturation, value);
            instance.renderer.material.SetColor(_ColorId, randomColorValue);
        }
        instance.scheduledForDisable = disableDelay > (1f / fadeSpeed);
        instance.disableTime = Time.time + disableDelay;
    }

    private void PositionAfterImage(AfterImageInstance instance)
    => instance.gameObject.transform.SetPositionAndRotation(myTransform.position, myTransform.rotation);

    private void OnDestroy()
    {
        // Complete any pending jobs before cleanup
        if (fadeUpdateJobScheduled)
            fadeUpdateJobHandle.Complete();

        // Dispose NativeArrays
        if (fadeTimers.IsCreated) fadeTimers.Dispose();
        if (disableTimes.IsCreated) disableTimes.Dispose();
        if (scheduledForDisableFlags.IsCreated) scheduledForDisableFlags.Dispose();
        if (instancesToRemove.IsCreated) instancesToRemove.Dispose();
        if (fadeValues.IsCreated) fadeValues.Dispose();

        if (combine == null) return;

        foreach (var combineInstance in combine)
            if (combineInstance.mesh != null)
                Destroy(combineInstance.mesh);
    }
}

// Job for updating fades
public struct UpdateFadeJob : IJob
{
    public float deltaTime;
    public float fadeSpeed;
    public float currentTime;

    public NativeArray<float> fadeTimers;
    public NativeArray<float> disableTimes;
    public NativeArray<bool> scheduledForDisableFlags;
    public NativeArray<bool> instancesToRemove;
    public NativeArray<float> fadeValues;
    public int instanceCount;

    public void Execute()
    {
        for (var i = 0; i < instanceCount; i++)
        {
            if (i >= fadeTimers.Length) break;

            // Update fade timer
            var newFadeTimer = fadeTimers[i] - (deltaTime * fadeSpeed);
            fadeTimers[i] = newFadeTimer;
            fadeValues[i] = newFadeTimer;

            // Check if instance should be removed
            var shouldRemove = newFadeTimer <= 0f || (scheduledForDisableFlags[i] && currentTime >= disableTimes[i]);
            instancesToRemove[i] = shouldRemove;
        }
    }
}