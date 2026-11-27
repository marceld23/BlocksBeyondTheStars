// Instance data that stays on the graphics card (#4046 part b) — shared by the shaders of the near drawers and the
// ground detail: crown clusters (BlockAtlas), plant bodies (PlantBody), grass clumps (GrassClump), the grass carpet
// (GrassShell), its flowers (MeadowFlower) and the tufts (ScatterLit).
//
// Those drawers used to hand every batch's matrices and per-instance vectors to the engine again each frame
// (Graphics.DrawMeshInstanced: arrays, at most 1023 instances a call). GpuInstances.cs uploads them ONCE per chunk
// build into a structured buffer and draws with Graphics.RenderMeshPrimitives, which selects each pass's
// PROCEDURAL_INSTANCING_ON variant. In that variant the pipeline's instancing header turns every
// UNITY_DEFINE_INSTANCED_PROP into a plain static variable and calls the pass's procedural function
// (#pragma instancing_options procedural:<function>) from UNITY_SETUP_INSTANCE_ID — so a shader only has to fill the
// matrices and its own instanced properties from the buffer there; everything after reads them as before.
//
// Include AFTER the pipeline's Core.hlsl, then write the pass's procedural function:
//
//     void MyInstance()
//     {
//         #if defined(BBTS_GPU_INSTANCES)
//             BBTS_GPU_INSTANCE(inst)   // reads the instance, sets the object matrices
//             _MyTile = inst.a;         // … then the shader's own instanced properties, in the drawer's order
//         #endif
//     }
//
// Every pass with #pragma multi_compile_instancing needs the option line and the function (GpuInstancesWiringTests
// pins it): a pass without them would draw every instance of a buffer draw at the world's origin.
//
// Desktop only (ADR 0021). The browser never takes this path — the drawers do not run there — and its targets compile
// the procedural variant to an empty function: WebGL 2 has no structured buffers in a vertex shader.
#ifndef BBTS_GPU_INSTANCES_INCLUDED
#define BBTS_GPU_INSTANCES_INCLUDED

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && (defined(SHADER_API_D3D11) || defined(SHADER_API_VULKAN) || defined(SHADER_API_GLCORE) || defined(SHADER_API_METAL))
    #define BBTS_GPU_INSTANCES 1

    // One instance, exactly as GpuInstances.Instance lays it out (192 bytes): the two object matrices and up to four
    // vectors — the drawer's per-instance values in the order of its former property arrays; unused ones are zero.
    struct BbtsGpuInstance
    {
        float4x4 objectToWorld;
        float4x4 worldToObject;
        float4 a;
        float4 b;
        float4 c;
        float4 d;
    };

    StructuredBuffer<BbtsGpuInstance> _BbtsInstances;

    // Reads this vertex's instance into `inst` and makes its matrices the object matrices of the draw.
    #define BBTS_GPU_INSTANCE(inst) \
        BbtsGpuInstance inst = _BbtsInstances[unity_InstanceID]; \
        unity_ObjectToWorld = inst.objectToWorld; \
        unity_WorldToObject = inst.worldToObject;
#endif

#endif
