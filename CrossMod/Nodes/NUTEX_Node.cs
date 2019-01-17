﻿using CrossMod.Rendering;
using CrossMod.Tools;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using SFGraphics.GLObjects.Textures;
using SFGraphics.GLObjects.Textures.TextureFormats;

namespace CrossMod.Nodes
{
    public enum NUTEX_FORMAT
    {
        R8G8B8A8_UNORM = 0,
        R8G8B8A8_SRGB = 0x05,
        R32G32B32A32_FLOAT = 0x34,
        B8G8R8A8_UNORM = 0x50,
        B8G8R8A8_SRGB = 0x55,
        BC1_UNORM = 0x80,
        BC1_SRGB = 0x85,
        BC2_UNORM = 0x90,
        BC2_SRGB = 0x95,
        BC3_UNORM = 0xa0,
        BC3_SRGB = 0xa5,
        BC4_UNORM = 0xb0,
        BC4_SNORM = 0xb5,
        BC5_UNORM = 0xc0,
        BC5_SNORM = 0xc5,
        BC6_UFLOAT = 0xd7,
        BC7_UNORM = 0xe0,
        BC7_SRGB = 0xe5
    }

    [FileTypeAttribute(".nutexb")]
    public class NUTEX_Node : FileNode, IRenderableNode, IExportableTextureNode
    {
        public List<byte[]> Mipmaps;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }

        public NUTEX_FORMAT Format;
        public string TexName { get; private set; }

        // Don't generate redundant textures.
        private RTexture renderableTexture = null;

        public readonly Dictionary<NUTEX_FORMAT, InternalFormat> internalFormatByNuTexFormat = new Dictionary<NUTEX_FORMAT, InternalFormat>()
        {
            { NUTEX_FORMAT.R8G8B8A8_SRGB, InternalFormat.SrgbAlpha },
            { NUTEX_FORMAT.R8G8B8A8_UNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.R32G32B32A32_FLOAT, InternalFormat.Rgba },
            { NUTEX_FORMAT.B8G8R8A8_UNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.B8G8R8A8_SRGB, InternalFormat.Srgb },
            { NUTEX_FORMAT.BC1_UNORM, InternalFormat.CompressedRgbaS3tcDxt1Ext },
            { NUTEX_FORMAT.BC1_SRGB, InternalFormat.CompressedSrgbAlphaS3tcDxt1Ext },
            { NUTEX_FORMAT.BC2_UNORM, InternalFormat.CompressedRgbaS3tcDxt3Ext },
            { NUTEX_FORMAT.BC2_SRGB, InternalFormat.CompressedSrgbAlphaS3tcDxt3Ext },
            { NUTEX_FORMAT.BC3_UNORM, InternalFormat.CompressedRgbaS3tcDxt5Ext },
            { NUTEX_FORMAT.BC3_SRGB, InternalFormat.CompressedSrgbAlphaS3tcDxt5Ext },
            { NUTEX_FORMAT.BC4_UNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.BC4_SNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.BC5_UNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.BC5_SNORM, InternalFormat.Rgba },
            { NUTEX_FORMAT.BC6_UFLOAT, InternalFormat.CompressedRgbBptcUnsignedFloat },
            { NUTEX_FORMAT.BC7_UNORM, InternalFormat.CompressedRgbaBptcUnorm },
            { NUTEX_FORMAT.BC7_SRGB, InternalFormat.CompressedSrgbAlphaBptcUnorm }
        };

        /// <summary>
        /// Channel information for uncompressed formats.
        /// </summary>
        public readonly Dictionary<NUTEX_FORMAT, PixelFormat> pixelFormatByNuTexFormat = new Dictionary<NUTEX_FORMAT, PixelFormat>()
        {
            { NUTEX_FORMAT.R8G8B8A8_SRGB, PixelFormat.Rgba },
            { NUTEX_FORMAT.R8G8B8A8_UNORM, PixelFormat.Rgba },
            { NUTEX_FORMAT.B8G8R8A8_UNORM, PixelFormat.Bgra },
            { NUTEX_FORMAT.B8G8R8A8_SRGB, PixelFormat.Bgra },
        };

        public NUTEX_Node(string path): base(path)
        {
            ImageKey = "texture";
            SelectedImageKey = "texture";
        }

        public override string ToString()
        {
            return Text.Contains(".") ? Text.Substring(0, Text.IndexOf(".")) : Text;
        }

        public override void Open()
        {
            using (BinaryReader reader  = new BinaryReader(new FileStream(AbsolutePath, FileMode.Open)))
            {
                Mipmaps = new List<byte[]>();
                // TODO: Why are there empty streams?
                if (reader.BaseStream.Length == 0)
                    return;

                reader.BaseStream.Position = reader.BaseStream.Length - 0xB0;


                int[] mipmapSizes = new int[16];
                for (int i = 0; i < mipmapSizes.Length; i++)
                    mipmapSizes[i] = reader.ReadInt32();

                reader.ReadChars(4); // TNX magic

                TexName = ReadTexName(reader);

                Width = reader.ReadInt32();
                Height = reader.ReadInt32();
                Depth = reader.ReadInt32();

                Format = (NUTEX_FORMAT)reader.ReadByte();

                reader.ReadByte();

                ushort Padding = reader.ReadUInt16();
                reader.ReadUInt32();

                int MipCount = reader.ReadInt32();
                int Alignment = reader.ReadInt32();
                int ArrayCount = reader.ReadInt32();
                int ImageSize = reader.ReadInt32();
                char[] Magic = reader.ReadChars(4);
                int MajorVersion = reader.ReadInt16();
                int MinorVersion = reader.ReadInt16();

                uint blkWidth = (uint)blkDims[Format].X;
                uint blkHeight = (uint)blkDims[Format].Y;

                uint blockHeight = SwitchSwizzler.GetBlockHeight(SwitchSwizzler.DivRoundUp((uint)Height, blkHeight));
                uint BlockHeightLog2 = (uint)Convert.ToString(blockHeight, 2).Length - 1;
                uint tileMode = 0;

                uint bpp = GetBpps(Format);

                reader.BaseStream.Position = 0;
                int blockHeightShift = 0;
                for (int i = 0; i < 1; i++)
                {
                    int size = mipmapSizes[i];

                    if (i == 0 && size % Alignment != 0)
                        size += Alignment - (size % Alignment);

                    byte[] deswiz = SwitchSwizzler.Deswizzle((uint)Width, (uint)Height, blkWidth, blkHeight, 0, bpp, tileMode, (int)Math.Max(0, BlockHeightLog2 - blockHeightShift), reader.ReadBytes(ImageSize));
                    byte[] trimmed = new byte[mipmapSizes[0]];
                    Array.Copy(deswiz, 0, trimmed, 0, trimmed.Length);

                    Mipmaps.Add(trimmed);
                }
            }
        }

        private string ReadTexName(BinaryReader reader)
        {
            var result = "";
            for (int i = 0; i < 0x40; i++)
            {
                byte b = reader.ReadByte();
                if (b != 0)
                    result += (char)b;
            }

            return result;
        }

        public static readonly Dictionary<NUTEX_FORMAT, Vector2> blkDims = new Dictionary<NUTEX_FORMAT, Vector2>()
        {
            { NUTEX_FORMAT.B8G8R8A8_UNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.B8G8R8A8_SRGB, new Vector2(1, 1) },
            { NUTEX_FORMAT.R8G8B8A8_UNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.R8G8B8A8_SRGB, new Vector2(1, 1) },
            { NUTEX_FORMAT.R32G32B32A32_FLOAT, new Vector2(1, 1) },
            { NUTEX_FORMAT.BC1_UNORM, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC1_SRGB, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC2_UNORM, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC2_SRGB, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC3_UNORM, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC3_SRGB, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC4_UNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.BC4_SNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.BC5_UNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.BC5_SNORM, new Vector2(1, 1) },
            { NUTEX_FORMAT.BC6_UFLOAT, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC7_SRGB, new Vector2(4, 4) },
            { NUTEX_FORMAT.BC7_UNORM, new Vector2(4, 4) },
        };

        public static uint GetBpps(NUTEX_FORMAT format)
        {
            switch (format)
            {
                case NUTEX_FORMAT.R8G8B8A8_UNORM:
                case NUTEX_FORMAT.R8G8B8A8_SRGB:
                case NUTEX_FORMAT.B8G8R8A8_UNORM:
                    return 4;
                case NUTEX_FORMAT.BC1_UNORM:
                    return 8;
                case NUTEX_FORMAT.BC1_SRGB:
                    return 8;
                case NUTEX_FORMAT.BC4_UNORM:
                    return 8;
                case NUTEX_FORMAT.BC4_SNORM:
                    return 8;
                case NUTEX_FORMAT.R32G32B32A32_FLOAT:
                case NUTEX_FORMAT.BC2_UNORM:
                    return 8;
                case NUTEX_FORMAT.BC2_SRGB:
                    return 8;
                case NUTEX_FORMAT.BC3_UNORM:
                    return 16;
                case NUTEX_FORMAT.BC3_SRGB:
                    return 16;
                case NUTEX_FORMAT.BC5_UNORM:
                case NUTEX_FORMAT.BC5_SNORM:
                case NUTEX_FORMAT.BC6_UFLOAT:
                case NUTEX_FORMAT.BC7_UNORM:
                case NUTEX_FORMAT.BC7_SRGB:
                    return 16;
                default:
                    return 0;
            }
        }

        public IRenderable GetRenderableNode()
        {
            // Don't initialize more than once.
            // We'll assume the context isn't destroyed.
            if (renderableTexture == null)
            {
                renderableTexture = new RTexture
                {
                    IsSrgb = Format.ToString().ToLower().Contains("srgb")
                };

                if (internalFormatByNuTexFormat.ContainsKey(Format))
                {
                    // This may require a higher OpenGL version for BC7.
                    if (!SFGraphics.GlUtils.OpenGLExtensions.IsAvailable("GL_ARB_texture_compression_bptc"))
                        throw new Rendering.Exceptions.MissingExtensionException("GL_ARB_texture_compression_bptc");

                    var sfTex = new Texture2D()
                    {
                        // Set defaults until all the sampler parameters are added.
                        TextureWrapS = TextureWrapMode.Repeat,
                        TextureWrapT = TextureWrapMode.Repeat
                    };

                    if (TextureFormatTools.IsCompressed(internalFormatByNuTexFormat[Format]))
                    {
                        sfTex.LoadImageData(Width, Height, Mipmaps, internalFormatByNuTexFormat[Format]);
                    }
                    else
                    {
                        // TODO: Uncompressed mipmaps.
                        var format = new TextureFormatUncompressed((PixelInternalFormat)internalFormatByNuTexFormat[Format], pixelFormatByNuTexFormat[Format], PixelType.UnsignedByte);
                        sfTex.LoadImageData(Width, Height, Mipmaps[0], format);
                    }

                    renderableTexture.renderTexture = sfTex;
                }
            }

            return renderableTexture;
        }

        public void SaveTexturePNG(string FileName)
        {
            System.Drawing.Bitmap Texture = ((RTexture)GetRenderableNode()).renderTexture.GetBitmap(0);
            Texture.Save(FileName);
            Texture.Dispose();
        }
    }
}
