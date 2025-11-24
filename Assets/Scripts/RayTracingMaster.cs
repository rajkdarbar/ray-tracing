using System.Collections.Generic;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{
    public ComputeShader RayTracingShader;
    public Texture SkyboxTexture;
    public Light DirectionalLight;

    [Range(0.0f, 1.0f)]
    [Tooltip("Probability that a generated sphere will be metallic (0 = all diffuse, 1 = all metal).")]
    public float chanceOfBeingMetalSphere = 0.5f;

    private float lastChanceOfBeingMetalSphere;

    [Header("Spheres")]
    public Vector2 SphereRadius = new Vector2(4.0f, 10.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;

    private Camera mainCamera;
    private float lastFieldOfView;
    private RenderTexture targetRT;
    private Material addMaterial;
    private uint currentSample = 0;
    private ComputeBuffer sphereBuffer;
    private List<Transform> trackedTransforms = new List<Transform>();

    struct Sphere
    {
        public Vector3 position; // 12 bytes
        public float radius; // 4 bytes
        public Vector3 albedo; // 12 bytes
        public Vector3 specular; // 12 bytes
    }

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();
        trackedTransforms.Add(transform);
        trackedTransforms.Add(DirectionalLight.transform);
    }

    private void OnEnable()
    {
        currentSample = 0;
        SetUpScene();
    }

    private void OnDisable()
    {
        if (sphereBuffer != null)
            sphereBuffer.Release();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        SetComputeShaderParameters();
        Render(destination);
    }

    private void Update()
    {
        VisualizeRays();

        if (mainCamera.fieldOfView != lastFieldOfView)
        {
            currentSample = 0;
            lastFieldOfView = mainCamera.fieldOfView;
        }

        // Rebuild scene when 'chanceOfBeingMetalSphere' changes
        if (!Mathf.Approximately(chanceOfBeingMetalSphere, lastChanceOfBeingMetalSphere))
        {
            lastChanceOfBeingMetalSphere = chanceOfBeingMetalSphere;
            currentSample = 0;
            SetUpScene();
        }

        foreach (Transform t in trackedTransforms)
        {
            if (t.hasChanged)
            {
                currentSample = 0;
                t.hasChanged = false;
            }
        }
    }

    private void VisualizeRays()
    {
        int width = Screen.width;
        int height = Screen.height;

        for (int x = 0; x < width; x += 100)
        {
            for (int y = 0; y < height; y += 100)
            {
                Vector2 uv = new Vector2((float)x / width, (float)y / height);
                Ray ray = mainCamera.ViewportPointToRay(uv);
                Debug.DrawRay(ray.origin, ray.direction * 100, Color.red);
            }
        }
    }

    private void SetUpScene()
    {
        List<Sphere> spheres = new List<Sphere>();
        const int MAX_RETRIES = 10;

        for (int i = 0; i < SpheresMax; i++)
        {
            int retries = 0;
            Sphere sphere;

        RetryPlacement:
            sphere = new Sphere();
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);

            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            // Check overlap
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                {
                    if (retries++ < MAX_RETRIES)
                        goto RetryPlacement; // try again
                    else
                        goto SkipSphere; // give up on this one
                }
            }

            // Assign material
            Color color = Random.ColorHSV();
            bool metal = Random.value > (1 - chanceOfBeingMetalSphere);

            if (!metal)
            {
                // Non-metals (dielectrics)
                sphere.albedo = new Vector3(color.r, color.g, color.b);  // Kd
                sphere.specular = new Vector3(0.04f, 0.04f, 0.04f); // small Ks as real dielectric surfaces reflect about 4% of incoming white light
            }
            else
            {   // Metals
                sphere.albedo = new Vector3(0, 0, 0); // Kd = 0
                sphere.specular = new Vector3(color.r, color.g, color.b); // colored Ks
            }

            spheres.Add(sphere);
            continue;

        SkipSphere:
            continue;
        }

        // upload to GPU
        if (sphereBuffer != null)
            sphereBuffer.Release();

        if (spheres.Count > 0)
        {
            sphereBuffer = new ComputeBuffer(spheres.Count, 40); // each sphere occupies 40 bytes in GPU memory
            sphereBuffer.SetData(spheres);
        }
    }


    private void SetComputeShaderParameters()
    {
        RayTracingShader.SetTexture(0, "SkyboxTexture", SkyboxTexture);
        RayTracingShader.SetMatrix("CameraToWorld", mainCamera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("CameraInverseProjection", mainCamera.projectionMatrix.inverse);
        RayTracingShader.SetVector("PixelOffset", new Vector2(Random.value, Random.value));

        Vector3 l = DirectionalLight.transform.forward; // incoming light direction
        RayTracingShader.SetVector("DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));

        if (sphereBuffer != null)
            RayTracingShader.SetBuffer(0, "SpheresBuffer", sphereBuffer);
    }


    private void Render(RenderTexture destination)
    {
        InitRenderTexture();

        // Set the target and dispatch the compute shader
        RayTracingShader.SetTexture(0, "Result", targetRT);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // Blit the result texture to the screen
        if (addMaterial == null)
            addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        addMaterial.SetFloat("sample", currentSample);

        Graphics.Blit(targetRT, destination, addMaterial);

        currentSample++;
    }


    private void InitRenderTexture()
    {
        if (targetRT == null || targetRT.width != Screen.width || targetRT.height != Screen.height)
        {
            // Release render texture if we already have one
            if (targetRT != null)
                targetRT.Release();

            // Get a render target for Ray Tracing
            targetRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            targetRT.enableRandomWrite = true;
            targetRT.Create();

            currentSample = 0; // reset sampling
        }
    }
}