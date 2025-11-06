using System.Collections;
using UnityEngine;

public class AfterImageEffect : MonoBehaviour
{
    [SerializeField] private int poolSize = 10;
    [SerializeField] private float delay = 0.04f;
    [SerializeField] private float fadeSpeed = 3f;
    [SerializeField] private float disableDelay = 3f;
    [SerializeField] private bool RandomColor = false;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private GameObject presetObj;
    [SerializeField] private CharacterController controller;
    [SerializeField] private SkinnedMeshRenderer[] skinRenderers;
    [SerializeField] private MeshRenderer[] meshRenderers;

    private float timer;
    private float[] fadeTimers;
    private Matrix4x4 matrix;
    private Renderer[] renderers;
    private CombineInstance[] combine;
    private GameObject[] objectPool;
    private MeshFilter[] poolMeshFilters;
    private MeshFilter[] meshFilters;
    private Material[] originalMaterials;
    private int activeAfterImages = 0;
    private int currentPoolIndex = 0;
    private bool hasSkinRenderers = false;
    private bool hasMeshRenderers = false;
    private Transform myTransform;

    private readonly int _FadeId = Shader.PropertyToID("_Fade");
    private readonly int _ColorId = Shader.PropertyToID("_Color");

    private void Start()
    {
        GetComponents();
        SetUpRenderers();
        InitializeObjectPool();
    }

    private void GetComponents()
    {
        myTransform = transform;
        skinRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        controller = GetComponentInParent<CharacterController>();
    }

    private void SetUpRenderers()
    {

        hasSkinRenderers = skinRenderers.Length > 0;
        hasMeshRenderers = meshRenderers.Length > 0;

        meshFilters = new MeshFilter[meshRenderers.Length];
        for (var i = 0; i < meshRenderers.Length; i++)
            meshFilters[i] = meshRenderers[i].GetComponent<MeshFilter>();

        combine = new CombineInstance[skinRenderers.Length + meshRenderers.Length];

        originalMaterials = new Material[skinRenderers.Length + meshRenderers.Length];
        for (var i = 0; i < skinRenderers.Length; i++)
            originalMaterials[i] = skinRenderers[i].material;
        for (var i = 0; i < meshRenderers.Length; i++)
            originalMaterials[skinRenderers.Length + i] = meshRenderers[i].material;
    }

    private void InitializeObjectPool()
    {
        objectPool = new GameObject[poolSize];
        poolMeshFilters = new MeshFilter[poolSize];
        renderers = new Renderer[poolSize];
        fadeTimers = new float[poolSize];

        for (var i = 0; i < poolSize; i++)
        {
            var obj = Instantiate(presetObj);
            poolMeshFilters[i] = obj.GetComponent<MeshFilter>();
            renderers[i] = obj.GetComponent<Renderer>();
            renderers[i].material = new Material(renderers[i].material);

            poolMeshFilters[i].mesh = new Mesh { name = "AfterImageMesh" + i };

            obj.SetActive(false);
            objectPool[i] = obj;
        }
    }

    private void LateUpdate()
    {
        if (controller == null) return;

        timer -= Time.deltaTime;
        if (timer < 0f && playerMovement.IsRunning)
        {
            timer = delay;
            CreateAfterImage();
        }

        if (activeAfterImages > 0)
            UpdateActiveAfterImages();
    }

    private void UpdateActiveAfterImages()
    {
        for (var i = 0; i < poolSize; i++)
        {
            if (objectPool[i].activeInHierarchy)
            {
                fadeTimers[i] -= Time.deltaTime * fadeSpeed;
                renderers[i].material.SetFloat(_FadeId, fadeTimers[i]);

                if (fadeTimers[i] <= 0f)
                {
                    objectPool[i].SetActive(false);
                    activeAfterImages--;
                }
            }
        }
    }

    private GameObject GetPooledObject(out int index)
    {
        for (var i = 0; i < poolSize; i++)
        {
            currentPoolIndex = (currentPoolIndex + 1) % poolSize;
            if (!objectPool[currentPoolIndex].activeInHierarchy)
            {
                index = currentPoolIndex;
                return objectPool[currentPoolIndex];
            }
        }
        index = -1;
        return null;
    }

    private void CreateAfterImage()
    {
        var pooledObj = GetPooledObject(out var objIndex);
        if (pooledObj == null) return;

        matrix = myTransform.worldToLocalMatrix;

        var combineIndex = 0;

        if (hasSkinRenderers)
        {
            for (var i = 0; i < skinRenderers.Length; i++)
            {
                var mesh = combine[combineIndex].mesh;
                if (mesh == null)
                {
                    mesh = new Mesh();
                    combine[combineIndex].mesh = mesh;
                }

                skinRenderers[i].BakeMesh(mesh);
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

        fadeTimers[objIndex] = 1f;
        renderers[objIndex].material.SetFloat(_FadeId, fadeTimers[objIndex]);

        if (RandomColor)
        {
            var randomColor = new Color(
                Random.value * 0.5f + 0.5f,
                Random.value * 0.5f + 0.5f,
                Random.value * 0.5f + 0.5f
            );
            renderers[objIndex].material.SetColor(_ColorId, randomColor);
        }

        var targetMesh = poolMeshFilters[objIndex].mesh;
        targetMesh.Clear();
        targetMesh.CombineMeshes(combine);

        pooledObj.transform.SetPositionAndRotation(myTransform.position, myTransform.rotation);
        pooledObj.SetActive(true);

        activeAfterImages++;

        if (disableDelay > (1f / fadeSpeed))
            StartCoroutine(DisableAfterDelay(pooledObj));
    }

    private IEnumerator DisableAfterDelay(GameObject obj)
    {
        yield return new WaitForSeconds(disableDelay);
        if (obj.activeInHierarchy)
        {
            obj.SetActive(false);
            activeAfterImages--;
        }
    }

    private void OnDestroy()
    {
        if (combine != null)
            foreach (var combineInstance in combine)
                if (combineInstance.mesh != null)
                    DestroyImmediate(combineInstance.mesh);

        if (objectPool != null)
        {
            foreach (var obj in objectPool)
            {
                if (obj != null)
                {
                    var meshFilter = obj.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.mesh != null)
                        DestroyImmediate(meshFilter.mesh);
                    Destroy(obj);
                }
            }
        }
    }
}