# 🔆 Simple GPU Ray Tracer

This project is a lightweight real-time ray tracer built in Unity using compute shaders.  
The goal was to learn the fundamentals of ray tracing from scratch — generating rays, testing intersections, shading surfaces, and accumulating samples — all without relying on Unity’s built-in rendering pipeline.  

---

## 🧩 What This Project Demonstrates

- A **Whitted-style ray tracer** running fully on the GPU  
- Rendering of many randomly generated spheres with diffuse or metallic materials  
- Support for **hard shadows** and **mirror reflections** using a directional light and skybox  
- Simple **progressive accumulation** for smoother, less noisy output  

---

## 🎨 Visual Examples

### 🔵 Diffuse Spheres
<img src="Assets/Resources/Output%20Images/raytracing-diffuse-spheres.png" width="600">

<br>

### 🟣 Glossy Metallic Spheres
<img src="Assets/Resources/Output%20Images/raytracing-glossy-metal-spheres.png" width="600">

<br>

### 🟠 Mixed Scene of Matte & Glossy Metallic Spheres
<img src="Assets/Resources/Output%20Images/raytracing-mixed-spheres.png" width="600">

<br>

---

## 🎥 Demo Video

Real-time capture running on an **NVIDIA GeForce RTX 3070 Laptop GPU** —  [Watch the video](https://youtu.be/Ldk_A19vR78)


---

## 🙏 Credits

Special thanks to [David Kuri](https://twitter.com/davidjkuri) for his excellent tutorial series, which greatly inspired this project. 

- 📘 Tutorial: https://web.archive.org/web/20230929075915/http://three-eyed-games.com/2018/05/03/gpu-ray-tracing-in-unity-part-1/  
- 💻 Source Code: https://bitbucket.org/Daerst/gpu-ray-tracing-in-unity/src/Tutorial_Pt1/
