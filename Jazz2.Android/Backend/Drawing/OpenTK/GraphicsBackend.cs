﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Duality.Drawing;
using Duality.Resources;
using OpenTK;
using OpenTK.Graphics.ES30;

namespace Duality.Backend.Android.OpenTK
{
    public class GraphicsBackend : IGraphicsBackend
    {
        private static readonly Version MinOpenGLVersion = new Version(3, 0);

        private static GraphicsBackend activeInstance;
        public static GraphicsBackend ActiveInstance
        {
            get { return activeInstance; }
        }

        private IDrawDevice currentDevice;
        private RenderStats renderStats;
        private RawList<uint> perVertexTypeVBO = new RawList<uint>();
        private Point2 externalBackbufferSize = Point2.Zero;

        private RawList<uint> perBatchEBO = new RawList<uint>();
        private int lastUsedEBO;
        private short[] indicesCache;

        private float[] modelViewData = new float[16];
        private float[] projectionData = new float[16];

        public IEnumerable<ScreenResolution> AvailableScreenResolutions
        {
            get
            {
                return DisplayDevice.Default.AvailableResolutions
                    .Select(resolution => new ScreenResolution(resolution.Width, resolution.Height, resolution.RefreshRate))
                    .Distinct();
            }
        }
        public Point2 ExternalBackbufferSize
        {
            get { return this.externalBackbufferSize; }
            set { this.externalBackbufferSize = value; }
        }

        string IDualityBackend.Id
        {
            get { return "AndroidGraphicsBackend"; }
        }
        string IDualityBackend.Name
        {
            get { return "OpenGL ES 3.0"; }
        }
        int IDualityBackend.Priority
        {
            get { return 1; }
        }

        bool IDualityBackend.CheckAvailable()
        {
            string versionString;
            try {
                versionString = GL.GetString(StringName.Version);
                CheckOpenGLErrors();
            } catch {
                return false;
            }

            // Parse the OpenGL ES version string in order to determine if it's sufficient
            if (versionString != null) {
                string[] token = versionString.Split(' ');
                for (int i = 0; i < token.Length; i++) {
                    Version version;
                    if (Version.TryParse(token[i], out version)) {
                        return !(version.Major < MinOpenGLVersion.Major ||
                                 (version.Major == MinOpenGLVersion.Major && version.Minor < MinOpenGLVersion.Minor));
                    }
                }
            }
            return false;
        }

        void IDualityBackend.Init()
        {
            GraphicsBackend.LogOpenGLSpecs();

            activeInstance = this;
        }
        void IDualityBackend.Shutdown()
        {
            if (activeInstance == this)
                activeInstance = null;

            if (DualityApp.ExecContext != DualityApp.ExecutionContext.Terminated) {
                //DefaultOpenTKBackendPlugin.GuardSingleThreadState();
                for (int i = 0; i < this.perVertexTypeVBO.Count; i++) {
                    uint handle = this.perVertexTypeVBO[i];
                    if (handle != 0) {
                        GL.DeleteBuffers(1, ref handle);
                    }
                }
                this.perVertexTypeVBO.Clear();
            }
        }

        void IGraphicsBackend.BeginRendering(IDrawDevice device, VertexBatchStore vertexData, RenderOptions options, RenderStats stats)
        {
            DebugCheckOpenGLErrors();

            // ToDo: AA is disabled for now
            //this.CheckContextCaps();

            this.currentDevice = device;
            this.renderStats = stats;

            // Upload all vertex data that we'll need during rendering
            if (vertexData != null) {
                this.perVertexTypeVBO.Count = Math.Max(this.perVertexTypeVBO.Count, vertexData.Batches.Count);
                for (int typeIndex = 0; typeIndex < vertexData.Batches.Count; typeIndex++) {
                    // Filter out unused vertex types
                    IVertexBatch vertexBatch = vertexData.Batches[typeIndex];
                    if (vertexBatch == null) continue;
                    if (vertexBatch.Count == 0) continue;

                    // Generate a VBO for this vertex type if it didn't exist yet
                    if (this.perVertexTypeVBO[typeIndex] == 0) {
                        GL.GenBuffers(1, out this.perVertexTypeVBO.Data[typeIndex]);
                    }
                    GL.BindBuffer(BufferTarget.ArrayBuffer, this.perVertexTypeVBO[typeIndex]);

                    // Upload all data of this vertex type as a single block
                    int vertexDataLength = vertexBatch.Declaration.Size * vertexBatch.Count;
                    using (PinnedArrayHandle pinned = vertexBatch.Lock()) {
                        GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)vertexDataLength, IntPtr.Zero, BufferUsage.StreamDraw);
                        GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)vertexDataLength, pinned.Address, BufferUsage.StreamDraw);
                    }
                }
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            // Prepare the target surface for rendering
            NativeRenderTarget.Bind(options.Target as NativeRenderTarget);

            // Determine the available size on the active rendering surface
            //Point2 availableSize;
            //if (NativeRenderTarget.BoundRT != null) {
            //    availableSize = new Point2(NativeRenderTarget.BoundRT.Width, NativeRenderTarget.BoundRT.Height);
            //} else {
            //    availableSize = this.externalBackbufferSize;
            //}

            Rect openGLViewport = options.Viewport;

            // Setup viewport and scissor rects
            GL.Viewport((int)openGLViewport.X, (int)openGLViewport.Y, (int)MathF.Ceiling(openGLViewport.W), (int)MathF.Ceiling(openGLViewport.H));
            GL.Scissor((int)openGLViewport.X, (int)openGLViewport.Y, (int)MathF.Ceiling(openGLViewport.W), (int)MathF.Ceiling(openGLViewport.H));

            // Clear buffers
            ClearBufferMask glClearMask = 0;
            ColorRgba clearColor = options.ClearColor;
            if ((options.ClearFlags & ClearFlag.Color) != ClearFlag.None) glClearMask |= ClearBufferMask.ColorBufferBit;
            if ((options.ClearFlags & ClearFlag.Depth) != ClearFlag.None) glClearMask |= ClearBufferMask.DepthBufferBit;
            GL.ClearColor(clearColor.R / 255.0f, clearColor.G / 255.0f, clearColor.B / 255.0f, clearColor.A / 255.0f);
            GL.ClearDepth(options.ClearDepth);
            GL.Clear(glClearMask);

            // Configure Rendering params
            if (options.RenderMode == RenderMatrix.ScreenSpace) {
                GL.Enable(EnableCap.ScissorTest);
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Always);
            } else {
                GL.Enable(EnableCap.ScissorTest);
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Lequal);
            }

            Matrix4 modelView = options.ModelViewMatrix;
            Matrix4 projection = options.ProjectionMatrix;

            if (NativeRenderTarget.BoundRT != null) {
                modelView =  Matrix4.CreateScale(new Vector3(1f, -1f, 1f)) * modelView;
                if (options.RenderMode == RenderMatrix.ScreenSpace) {
                    modelView = Matrix4.CreateTranslation(new Vector3(0f, -device.TargetSize.Y, 0f)) * modelView;
                }
            }

            // Convert matrices to float arrays
            GetArrayMatrix(ref modelView, ref modelViewData);
            GetArrayMatrix(ref projection, ref projectionData);

            // All EBOs can be used again
            lastUsedEBO = 0;
        }

        unsafe void IGraphicsBackend.Render(IReadOnlyList<DrawBatch> batches)
        {
            DrawBatch lastRendered = null;
            int drawCalls = 0;

            for (int i = 0; i < batches.Count; i++) {
                DrawBatch batch = batches[i];
                VertexDeclaration vertexType = batch.VertexType;

                GL.BindBuffer(BufferTarget.ArrayBuffer, this.perVertexTypeVBO[vertexType.TypeIndex]);

                bool first = (i == 0);
                bool sameMaterial =
                    lastRendered != null &&
                    lastRendered.Material == batch.Material;

                // Setup vertex bindings. Note that the setup differs based on the 
                // materials shader, so material changes can be vertex binding changes.
                if (lastRendered != null) {
                    this.FinishVertexFormat(lastRendered.Material, lastRendered.VertexType);
                }
                this.SetupVertexFormat(batch.Material, vertexType);

                // Setup material when changed.
                if (!sameMaterial) {
                    this.SetupMaterial(
                        batch.Material,
                        lastRendered != null ? lastRendered.Material : null);
                }

                if (batch.VertexMode == VertexMode.Quads) {
                    VertexDrawRange[] rangeData = batch.VertexRanges.Data;
                    int rangeCount = batch.VertexRanges.Count;

                    // Compute number of all vertices in batch and resize static cache if necessary
                    int numberOfVertices = 0;
                    for (int r = 0; r < rangeCount; r++) {
                        numberOfVertices += rangeData[r].Count;
                    }

                    int numberOfIndices = (numberOfVertices / 4) * 6;
                    if (indicesCache == null || indicesCache.Length < numberOfIndices) {
                        indicesCache = new short[numberOfIndices];
                    }

                    // Expand every 1 Quad (4 vertices) to 2 Triangles (2x3 vertices) using IndexBuffer
                    int destIndex = 0;
                    for (int r = 0; r < rangeCount; r++) {
                        int srcIndex = rangeData[r].Index;
                        int count = rangeData[r].Count;

                        for (int offset = 0; offset < count; offset += 4, destIndex += 6) {
                            indicesCache[destIndex] = (short)(srcIndex + offset);
                            indicesCache[destIndex + 1] = (short)(srcIndex + offset + 1);
                            indicesCache[destIndex + 2] = (short)(srcIndex + offset + 2);
                            indicesCache[destIndex + 3] = (short)(srcIndex + offset);
                            indicesCache[destIndex + 4] = (short)(srcIndex + offset + 2);
                            indicesCache[destIndex + 5] = (short)(srcIndex + offset + 3);
                        }
                    }

                    // Find/allocate unused EBO and copy indices to it
                    uint buffer;
                    if (perBatchEBO.Count <= lastUsedEBO) {
                        GL.GenBuffers(1, out buffer);
                        perBatchEBO.Add(buffer);
                    } else {
                        buffer = perBatchEBO[lastUsedEBO++];
                    }

                    int bufferSize = numberOfIndices * sizeof(short);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)bufferSize, IntPtr.Zero, BufferUsage.StreamDraw);
                    fixed (short* ptr = indicesCache) {
                        GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)bufferSize, (IntPtr)ptr, BufferUsage.StreamDraw);
                    }

                    // Draw the current batch using indices
                    GL.DrawElements(
                        GetOpenTKVertexMode(batch.VertexMode),
                        numberOfIndices,
                        DrawElementsType.UnsignedShort,
                        IntPtr.Zero);
                } else {
                    // Draw the current batch
                    VertexDrawRange[] rangeData = batch.VertexRanges.Data;
                    int rangeCount = batch.VertexRanges.Count;
                    for (int r = 0; r < rangeCount; r++) {
                        GL.DrawArrays(
                            GetOpenTKVertexMode(batch.VertexMode),
                            rangeData[r].Index,
                            rangeData[r].Count);
                    }
                }

                drawCalls++;
                lastRendered = batch;
            }

            // Cleanup after rendering
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            if (lastRendered != null) {
                this.FinishMaterial(lastRendered.Material);
                this.FinishVertexFormat(lastRendered.Material, lastRendered.VertexType);
            }

            if (this.renderStats != null) {
                this.renderStats.DrawCalls += drawCalls;
            }
        }

        void IGraphicsBackend.EndRendering()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            this.currentDevice = null;

            DebugCheckOpenGLErrors();
        }

        INativeTexture IGraphicsBackend.CreateTexture()
        {
            return new NativeTexture();
        }
        INativeRenderTarget IGraphicsBackend.CreateRenderTarget()
        {
            return new NativeRenderTarget();
        }
        INativeShaderPart IGraphicsBackend.CreateShaderPart()
        {
            return new NativeShaderPart();
        }
        INativeShaderProgram IGraphicsBackend.CreateShaderProgram()
        {
            return new NativeShaderProgram();
        }
        INativeWindow IGraphicsBackend.CreateWindow(WindowOptions options)
        {
            return new NativeWindow(options);
        }

        void IGraphicsBackend.GetOutputPixelData<T>(T[] buffer, ColorDataLayout dataLayout, ColorDataElementType dataElementType, int x, int y, int width, int height)
        {
            // Removed thread guards because of performance
            //DefaultOpenTKBackendPlugin.GuardSingleThreadState();

            NativeRenderTarget lastRt = NativeRenderTarget.BoundRT;
            NativeRenderTarget.Bind(null);
            {
                GL.ReadPixels(x, y, width, height, dataLayout.ToOpenTK(), dataElementType.ToOpenTK(), buffer);

                // The image will be upside-down because of OpenGL's coordinate system. Flip it.
                int structSize = Marshal.SizeOf(typeof(T));
                T[] switchLine = new T[width * 4 / structSize];
                for (int flipY = 0; flipY < height / 2; flipY++) {
                    int lineIndex = flipY * width * 4 / structSize;
                    int lineIndex2 = (height - 1 - flipY) * width * 4 / structSize;

                    // Copy the current line to the switch buffer
                    for (int lineX = 0; lineX < width; lineX++) {
                        switchLine[lineX] = buffer[lineIndex + lineX];
                    }

                    // Copy the opposite line to the current line
                    for (int lineX = 0; lineX < width; lineX++) {
                        buffer[lineIndex + lineX] = buffer[lineIndex2 + lineX];
                    }

                    // Copy the switch buffer to the opposite line
                    for (int lineX = 0; lineX < width; lineX++) {
                        buffer[lineIndex2 + lineX] = switchLine[lineX];
                    }
                }
            }
            NativeRenderTarget.Bind(lastRt);
        }

        // ToDo: AA is disabled for now
        /*private void CheckContextCaps()
        {
            if (this.contextCapsRetrieved) return;
            this.contextCapsRetrieved = true;

            // Make sure we're not on a render target, which may override
            // some settings that we'd like to get from the main contexts
            // backbuffer.
            NativeRenderTarget oldTarget = NativeRenderTarget.BoundRT;
            NativeRenderTarget.Bind(null);

            int targetSamples, actualSamples;
            DualityActivity activity = DualityActivity.Current;
            if (activity != null) {
                targetSamples = activity.InnerView.GraphicsMode.Samples;
            } else {
                targetSamples = 0;
            }

            // Retrieve how many MSAA samples are actually available, despite what 
            // was offered and requested vis graphics mode.
            CheckOpenGLErrors(true);
            GL.GetInteger(GetPName.Samples, out actualSamples);
            if (CheckOpenGLErrors()) actualSamples = targetSamples;

            NativeRenderTarget.Bind(oldTarget);
        }*/

        private void SetupVertexFormat(BatchInfo material, VertexDeclaration vertexDeclaration)
        {
            DrawTechnique technique = material.Technique.Res ?? DrawTechnique.Solid.Res;
            NativeShaderProgram program = (technique.Shader.Res != null ? technique.Shader.Res : ShaderProgram.Minimal.Res).Native as NativeShaderProgram;
            if (program == null) {
                return;
            }

            VertexElement[] elements = vertexDeclaration.Elements;
            ShaderFieldInfo[] varInfo = program.Fields;
            int[] locations = program.FieldLocations;

            bool[] varUsed = new bool[varInfo.Length];
            for (int elementIndex = 0; elementIndex < elements.Length; elementIndex++) {
                int selectedVar = -1;
                for (int varIndex = 0; varIndex < varInfo.Length; varIndex++) {
                    if (varUsed[varIndex]) continue;

                    if (locations[varIndex] == -1 || varInfo[varIndex].Scope != ShaderFieldScope.Attribute) {
                        varUsed[varIndex] = true;
                        continue;
                    }

                    if (!ShaderVarMatches(
                        ref varInfo[varIndex],
                        elements[elementIndex].Type,
                        elements[elementIndex].Count))
                        continue;

                    //if (elements[elementIndex].Role != VertexElementRole.Unknown && varInfo[varIndex].Name != elements[elementIndex].Role.ToString()) {
                    //    continue;
                    //}

                    selectedVar = varIndex;
                    varUsed[varIndex] = true;
                    break;
                }
                if (selectedVar == -1) continue;

                VertexAttribPointerType attribType;
                switch (elements[elementIndex].Type) {
                    default:
                    case VertexElementType.Float: attribType = VertexAttribPointerType.Float; break;
                    case VertexElementType.Byte: attribType = VertexAttribPointerType.UnsignedByte; break;
                }

                bool isNormalized = (elements[elementIndex].Role == VertexElementRole.Color);

                GL.EnableVertexAttribArray(locations[selectedVar]);
                GL.VertexAttribPointer(
                    locations[selectedVar],
                    elements[elementIndex].Count,
                    attribType,
                    isNormalized,
                    vertexDeclaration.Size,
                    elements[elementIndex].Offset);
            }
        }

        private void SetupMaterial(BatchInfo material, BatchInfo lastMaterial)
        {
            if (material == lastMaterial) return;
            DrawTechnique tech = material.Technique.Res ?? DrawTechnique.Solid.Res;
            DrawTechnique lastTech = lastMaterial != null ? lastMaterial.Technique.Res : null;

            // Prepare Rendering
            if (tech.NeedsPreparation) {
                material = new BatchInfo(material);
                tech.PrepareRendering(this.currentDevice, material);
            }

            // Setup BlendType
            if (lastTech == null || tech.Blending != lastTech.Blending) {
                this.SetupBlendType(tech.Blending, this.currentDevice.DepthWrite);
            }

            // Bind Shader
            NativeShaderProgram shader = (tech.Shader.Res != null ? tech.Shader.Res : ShaderProgram.Minimal.Res).Native as NativeShaderProgram;
            NativeShaderProgram.Bind(shader);

            // Setup shader data
            if (shader != null) {
                ShaderFieldInfo[] varInfo = shader.Fields;
                int[] locations = shader.FieldLocations;
                int[] builtinIndices = shader.BuiltinVariableIndex;

                // Setup sampler bindings automatically
                int curSamplerIndex = 0;
                if (material.Textures != null) {
                    for (int i = 0; i < varInfo.Length; i++) {
                        if (locations[i] == -1) continue;
                        if (varInfo[i].Type != ShaderFieldType.Sampler2D) continue;

                        // Bind Texture
                        ContentRef<Texture> texRef = material.GetTexture(varInfo[i].Name);
                        NativeTexture.Bind(texRef, curSamplerIndex);

                        GL.Uniform1(locations[i], curSamplerIndex);

                        curSamplerIndex++;
                    }
                }
                NativeTexture.ResetBinding(curSamplerIndex);

                // Transfer uniform data from material to actual shader
                //if (material.Uniforms != null) {
                    for (int i = 0; i < varInfo.Length; i++) {
                        if (locations[i] == -1) continue;

                        float[] data;
                        if (varInfo[i].Name == "ModelView") {
                            data = modelViewData;
                        } else if (varInfo[i].Name == "Projection") {
                            data = projectionData;
                        } else if (material.Uniforms != null) {
                            data = material.GetUniform(varInfo[i].Name);
                            if (data == null) continue;
                        } else {
                            continue;
                        }

                        NativeShaderProgram.SetUniform(ref varInfo[i], locations[i], data);
                    }
                //}

                // Specify builtin shader variables, if requested
                float[] fieldValue = null;
                for (int i = 0; i < builtinIndices.Length; i++) {
                    if (BuiltinShaderFields.TryGetValue(this.currentDevice, builtinIndices[i], ref fieldValue))
                        NativeShaderProgram.SetUniform(ref varInfo[i], locations[i], fieldValue);
                }
            }
        }

        private void SetupBlendType(BlendMode mode, bool depthWrite = true)
        {
            switch (mode) {
                default:
                case BlendMode.Reset:
                case BlendMode.Solid:
                    GL.DepthMask(depthWrite);
                    GL.Disable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    break;
                case BlendMode.Mask:
                    GL.DepthMask(depthWrite);
                    GL.Disable(EnableCap.Blend);
                    GL.Enable(EnableCap.SampleAlphaToCoverage);
                    break;
                case BlendMode.Alpha:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                    break;
                case BlendMode.AlphaPre:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFuncSeparate(BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                    break;
                case BlendMode.Add:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One);
                    break;
                case BlendMode.Light:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFuncSeparate(BlendingFactorSrc.DstColor, BlendingFactorDest.One, BlendingFactorSrc.Zero, BlendingFactorDest.One);
                    break;
                case BlendMode.Multiply:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFunc(BlendingFactorSrc.DstColor, BlendingFactorDest.Zero);
                    break;
                case BlendMode.Invert:
                    GL.DepthMask(false);
                    GL.Enable(EnableCap.Blend);
                    GL.Disable(EnableCap.SampleAlphaToCoverage);
                    GL.BlendFunc(BlendingFactorSrc.OneMinusDstColor, BlendingFactorDest.OneMinusSrcColor);
                    break;
            }
        }
        private void FinishVertexFormat(BatchInfo material, VertexDeclaration vertexDeclaration)
        {
            DrawTechnique technique = material.Technique.Res ?? DrawTechnique.Solid.Res;
            NativeShaderProgram program = (technique.Shader.Res != null ? technique.Shader.Res : ShaderProgram.Minimal.Res).Native as NativeShaderProgram;
            if (program == null) {
                return;
            }

            //VertexDeclaration vertexDeclaration = renderBatch.VertexDeclaration;
            //VertexElement[] elements = vertexDeclaration.Elements;
            ShaderFieldInfo[] varInfo = program.Fields;
            int[] locations = program.FieldLocations;

            for (int varIndex = 0; varIndex < varInfo.Length; varIndex++) {
                if (locations[varIndex] == -1) continue;

                GL.DisableVertexAttribArray(locations[varIndex]);
            }
        }
        private void FinishMaterial(BatchInfo material)
        {
            //DrawTechnique tech = material.Technique.Res;
            this.SetupBlendType(BlendMode.Reset);
            NativeShaderProgram.Bind(null as NativeShaderProgram);
            NativeTexture.ResetBinding();
        }

        private static BeginMode GetOpenTKVertexMode(VertexMode mode)
        {
            switch (mode) {
                default:
                case VertexMode.Points: return BeginMode.Points;
                case VertexMode.Lines: return BeginMode.Lines;
                case VertexMode.LineStrip: return BeginMode.LineStrip;
                case VertexMode.LineLoop: return BeginMode.LineLoop;

                case VertexMode.Quads:
                case VertexMode.Triangles: return BeginMode.Triangles;

                case VertexMode.TriangleStrip: return BeginMode.TriangleStrip;
                case VertexMode.TriangleFan: return BeginMode.TriangleFan;
            }
        }

        private static void GetArrayMatrix(ref Matrix4 source, ref float[] target)
        {
            target[0] = source.M11;
            target[1] = source.M12;
            target[2] = source.M13;
            target[3] = source.M14;

            target[4] = source.M21;
            target[5] = source.M22;
            target[6] = source.M23;
            target[7] = source.M24;

            target[8] = source.M31;
            target[9] = source.M32;
            target[10] = source.M33;
            target[11] = source.M34;

            target[12] = source.M41;
            target[13] = source.M42;
            target[14] = source.M43;
            target[15] = source.M44;
        }

        private static bool ShaderVarMatches(ref ShaderFieldInfo varInfo, VertexElementType type, int count)
        {
            //if (varInfo.Scope != ShaderFieldScope.Attribute) return false;

            Type elementPrimitive = varInfo.Type.GetElementPrimitive();
            Type requiredPrimitive = null;
            switch (type) {
                case VertexElementType.Byte:
                    requiredPrimitive = typeof(byte);
                    break;
                case VertexElementType.Float:
                    requiredPrimitive = typeof(float);
                    break;
            }
            // ToDo: Bugfix to allow Color (byte to float) conversion
            if (elementPrimitive != requiredPrimitive && !(elementPrimitive == typeof(float) && requiredPrimitive == typeof(byte))) {
                return false;
            }

            int elementCount = varInfo.Type.GetElementCount();
            if (count != elementCount * varInfo.ArrayLength)
                return false;

            return true;
        }

        private static void LogOpenGLSpecs()
        {
            string versionString = null;
            try {
                CheckOpenGLErrors();
                versionString = GL.GetString(StringName.Version);
                Console.WriteLine(
                    "OpenGL Version: {0}" + Environment.NewLine +
                    "Vendor: {1}" + Environment.NewLine +
                    "Renderer: {2}" + Environment.NewLine +
                    "Shader Version: {3}",
                    versionString,
                    GL.GetString(StringName.Vendor),
                    GL.GetString(StringName.Renderer),
                    GL.GetString(StringName.ShadingLanguageVersion));
                CheckOpenGLErrors();
            } catch (Exception e) {
                Console.WriteLine("Can't determine OpenGL specs, because an error occurred: {0}", e);
            }

            // Parse the OpenGL version string in order to determine if it's sufficient
            if (versionString != null) {
                string[] token = versionString.Split(' ');
                for (int i = 0; i < token.Length; i++) {
                    Version version;
                    if (Version.TryParse(token[i], out version)) {
                        if (version.Major < MinOpenGLVersion.Major || (version.Major == MinOpenGLVersion.Major && version.Minor < MinOpenGLVersion.Minor)) {
                            Console.WriteLine(
                                "The detected OpenGL version {0} appears to be lower than the required minimum. Version {1} or higher is required to run Duality applications.",
                                version,
                                MinOpenGLVersion);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Checks for errors that might have occurred during video processing. You should avoid calling this method due to performance reasons.
        /// Only use it on suspect.
        /// </summary>
        /// <param name="silent">If true, errors aren't logged.</param>
        /// <returns>True, if an error occurred, false if not.</returns>
        public static bool CheckOpenGLErrors(bool silent = false, [CallerMemberName] string callerInfoMember = null, [CallerFilePath] string callerInfoFile = null, [CallerLineNumber] int callerInfoLine = -1)
        {
            ErrorCode error;
            bool found = false;
            while ((error = GL.GetErrorCode()) != ErrorCode.NoError) {
                if (!silent) {
                    Console.WriteLine(
                        "Internal OpenGL error, code {0} at {1} in {2}, line {3}.",
                        error,
                        callerInfoMember,
                        callerInfoFile,
                        callerInfoLine);
                }
                found = true;
            }
            if (found && !silent && System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Break();
            return found;
        }
        /// <summary>
        /// Checks for OpenGL errors using <see cref="CheckOpenGLErrors"/> when both compiled in debug mode and a with an attached debugger.
        /// </summary>
        /// <returns></returns>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void DebugCheckOpenGLErrors([CallerMemberName] string callerInfoMember = null, [CallerFilePath] string callerInfoFile = null, [CallerLineNumber] int callerInfoLine = -1)
        {
            if (!System.Diagnostics.Debugger.IsAttached) return;
            CheckOpenGLErrors(false, callerInfoMember, callerInfoFile, callerInfoLine);
        }
    }
}