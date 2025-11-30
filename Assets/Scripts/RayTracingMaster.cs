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
    private RenderTexture convergedRT;

    private uint currentSample = 0;
    private Material accumulationMaterial;

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
        // VisualizeRays();

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
                // DIFFUSE (Lambertian) — rough, matte
                sphere.albedo = new Vector3(color.r, color.g, color.b);  // Kd
                sphere.specular = new Vector3(0.04f, 0.04f, 0.04f); // small Ks as real dielectric surfaces reflect about 4% of incoming white light
            }
            else
            {   // GLOSSY METALLIC — shiny metal, color-tinted reflections
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
            const int SphereStride = sizeof(float) * (3 + 1 + 3 + 3); // each sphere occupies 40 bytes in GPU memory
            sphereBuffer = new ComputeBuffer(spheres.Count, SphereStride);
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

        if (accumulationMaterial == null)
            accumulationMaterial = new Material(Shader.Find("Hidden/AccumulateSamples"));

        RenderTexture tempRT = RenderTexture.GetTemporary(convergedRT.width, convergedRT.height, 0, RenderTextureFormat.ARGBFloat);

        accumulationMaterial.SetTexture("_MainTex", targetRT); // new sample
        accumulationMaterial.SetTexture("_History", convergedRT); // previous frame
        accumulationMaterial.SetFloat("_Sample", currentSample);

        Graphics.Blit(null, tempRT, accumulationMaterial); // run accumulation shader → store result in tempRT
        Graphics.Blit(tempRT, convergedRT); // copy accumulated result into convergedRT (persistent buffer)
        Graphics.Blit(convergedRT, destination); // output converged image to the screen (framebuffer)
        RenderTexture.ReleaseTemporary(tempRT); // release temporary render texture memory

        currentSample++;
    }


    private void InitRenderTexture()
    {
        if (targetRT == null || targetRT.width != Screen.width || targetRT.height != Screen.height)
        {
            if (targetRT != null)
            {
                targetRT.Release();
            }

            if (convergedRT != null)
            {
                convergedRT.Release();
            }

            targetRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            targetRT.enableRandomWrite = true;
            targetRT.Create();

            convergedRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            convergedRT.enableRandomWrite = true;
            convergedRT.Create();

            currentSample = 0;
        }
    }
}