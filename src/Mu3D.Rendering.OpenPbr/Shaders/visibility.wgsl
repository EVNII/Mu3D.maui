// Mu3D raster visibility. Vertex pulling shares the exact world-space geometry used by ray transport.
@group(0) @binding(0) var<uniform> frame: array<vec4<f32>, 20>;
@group(0) @binding(1) var<storage, read> triangles: array<vec4<f32>>;
struct Vertex {
    @builtin(position) position: vec4<f32>,
    @location(0) bary: vec3<f32>,
    @location(1) @interpolate(flat) triangle: u32,
}
@vertex fn vs_main(@builtin(vertex_index) index: u32) -> Vertex {
    let t = index / 3u;
    let corner = index % 3u;
    let p = triangles[t * 12u + corner].xyz;
    var clip = frame[16] * p.x + frame[17] * p.y + frame[18] * p.z + frame[19];
    if ((frame[8].z == 2.0 || frame[8].z == 4.0) && triangles[t * 12u + 2u].w == 2.0) {
        clip = vec4<f32>(2.0, 2.0, 2.0, 1.0);
    }
    var bary = vec3<f32>(0.0);
    bary[corner] = 1.0;
    return Vertex(clip, bary, t);
}
@fragment fn fs_main(input: Vertex) -> @location(0) vec4<f32> {
    return vec4<f32>(input.bary, f32(input.triangle + 1u));
}
