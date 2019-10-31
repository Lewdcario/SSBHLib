﻿using OpenTK;
using SFGraphics.Cameras;
using SFGraphics.GLObjects.Shaders;
using OpenTK.Graphics.OpenGL;

namespace CrossMod.Rendering.Models
{
    public class RMesh
    {
        public static Resources.DefaultTextures defaultTextures = null;

        public RenderMesh RenderMesh { get; set; } = null;

        public string Name { get; set; }

        public Vector4 BoundingSphere { get; set; }

        public string SingleBindName { get; set; } = "";
        public int SingleBindIndex { get; set; } = -1;

        public Material Material { get; set; } = null;

        public bool Visible { get; set; } = true;

        private SFGenericModel.Materials.GenericMaterial genericMaterial = null;
        private SFGenericModel.Materials.UniformBlock uniformBlock = null;

        public void Draw(Shader shader, Camera camera, RSkeleton skeleton)
        {
            if (!Visible)
                return;

            if (skeleton != null)
            {
                shader.SetMatrix4x4("transform", skeleton.GetAnimationSingleBindsTransform(SingleBindIndex));
            }

            // TODO: ???
            GL.Enable(EnableCap.PrimitiveRestart);
            GL.PrimitiveRestartIndex(0xFFFFFFFF);

            RenderMesh?.Draw(shader);
        }

        public void SetMaterialUniforms(Shader shader)
        {
            // TODO: Rework default texture creation.
            if (defaultTextures == null)
                defaultTextures = new Resources.DefaultTextures();

            if (genericMaterial == null)
                genericMaterial = Material.CreateGenericMaterial(Material);
            genericMaterial.SetShaderUniforms(shader);

            if (uniformBlock == null)
            {
                uniformBlock = new SFGenericModel.Materials.UniformBlock(shader, "MaterialParams") { BlockBinding = 1 };
                Material.AddMaterialParams(uniformBlock);
            }
            // This needs to be updated more than once.
            Material.AddDebugParams(uniformBlock);

            uniformBlock.BindBlock(shader);
        }
    }
}
