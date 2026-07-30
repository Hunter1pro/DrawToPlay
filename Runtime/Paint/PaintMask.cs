using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The painted weight mask of one shape: an RGBA8 raster covering a LOCAL-space rect,
    /// plus the readable Texture2D the paint shader samples. Port of the mask half of
    /// curve_shape_2d.gd (_ensure_mask / _stamp_cpu / _channel_color / _stamp_gpu /
    /// _refresh_display, lines 249-340).
    ///
    /// Channel model (unchanged from Godot): R/G/B are the weights of texture slot 1/2/3,
    /// A is the feathered "has paint" coverage. Painting lerps RGB toward the channel tint
    /// by the feathered amount and RAISES A (max); erasing leaves RGB alone and LOWERS A
    /// (min). Weights are normalised in the shader, never here.
    ///
    /// Units: <see cref="rect"/> is in the shape's local units (1 unit = 32 Godot px),
    /// <see cref="resolution"/> is mask pixels per local unit (DrawnShapeAsset.maskResolution,
    /// 128 = Godot paint_resolution 4 x 32). Stamp coordinates are MASK PIXELS — the caller
    /// converts with (localPos - rect.min) * resolution, exactly like paint().
    ///
    /// Display path: Godot keeps a separate GPU DrawableTexture2D and blits brush quads into
    /// it (_stamp_gpu) so it never re-uploads the whole image. Unity has no partial upload for
    /// a Texture2D, so the CPU array here IS the authoritative mask AND the display source:
    /// stamps write pixels + widen a dirty rect, and <see cref="Flush"/> pushes just that
    /// block into the texture before the next draw. That collapses _stamp_gpu, _refresh_display
    /// and the erase/replace blit materials into one code path.
    /// </summary>
    public sealed class PaintMask
    {
        /// <summary>Godot floors the image at 4 px on each axis (_ensure_mask).</summary>
        private const int k_MinSizePixels = 4;

        /// <summary>Godot has no ceiling: a big shape at 128 px/unit would allocate hundreds of
        /// megabytes and take the editor with it. <see cref="Create"/> lowers the resolution to
        /// fit instead (the mask stays self-consistent because every consumer reads
        /// <see cref="resolution"/> from here, not from the asset).</summary>
        private const int k_MaxSizePixels = 2048;

        /// <summary>Feather floor from _stamp_cpu: max(radius * softness, 0.5) MASK px.</summary>
        private const float k_MinFeather = 0.5f;

        private const string k_TextureName = "DrawnShape Paint Mask (generated)";

        private Color32[] m_Pixels;
        private Color32[] m_Block;
        private Texture2D m_Texture;
        private Rect m_Rect;
        private int m_Width;
        private int m_Height;
        private float m_Resolution;

        // inclusive dirty block in pixel coordinates; min > max means "nothing to upload"
        private int m_DirtyMinX;
        private int m_DirtyMinY;
        private int m_DirtyMaxX = -1;
        private int m_DirtyMaxY = -1;

        private PaintMask(Rect rect, float resolution, int width, int height, Color32[] pixels)
        {
            m_Rect = rect;
            m_Resolution = resolution;
            m_Width = width;
            m_Height = height;
            m_Pixels = pixels;
            MarkDirty(0, 0, width - 1, height - 1);
        }

        /// <summary>Local-space rect this mask covers. Mirrors DrawnShapeAsset.maskRect.</summary>
        public Rect rect => m_Rect;

        public int width => m_Width;

        public int height => m_Height;

        /// <summary>Mask pixels per local unit actually in force (see the size cap above).</summary>
        public float resolution => m_Resolution;

        public bool isValid => m_Pixels != null && m_Width > 0 && m_Height > 0;

        /// <summary>The texture the paint shader samples; uploading any pending stamps first.
        /// Null only when the mask has been released.</summary>
        public Texture2D texture
        {
            get
            {
                Flush();
                return m_Texture;
            }
        }

        /// <summary>Fresh transparent mask over <paramref name="localRect"/>. Image size is
        /// ceil(size * resolution) floored at 4 px, exactly like Image.create in _ensure_mask —
        /// the raster therefore covers a hair MORE than the rect, and the shader's UV mapping
        /// divides by the pixel size (not the rect size) to stay consistent with that.</summary>
        public static PaintMask Create(Rect localRect, float resolution)
        {
            float res = FitResolution(localRect, resolution, out int width, out int height);
            return new PaintMask(localRect, res, width, height, new Color32[width * height]);
        }

        /// <summary>Decode a committed mask (DrawnShapeAsset.maskPng) back into a live mask.
        /// <paramref name="localRect"/> is the asset's maskRect — PNG carries pixels only, so
        /// the rect must be restored by the caller alongside, same as Godot. Returns null when
        /// the bytes are empty or not decodable.</summary>
        public static PaintMask FromPng(byte[] bytes, Rect localRect, float resolution)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            // decode through a scratch texture: LoadImage reallocates the target with the PNG's
            // own format/flags, which would silently undo the linear (non-sRGB) flag the mask
            // needs. GetPixels32 hands back the raw bytes either way, so the round trip is exact.
            var scratch = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.DontSave
            };
            PaintMask mask = null;
            if (ImageConversion.LoadImage(scratch, bytes, false)
                && scratch.width > 0 && scratch.height > 0)
            {
                // re-run the same fit Create used, so a mask that had to be coarsened to stay
                // inside the size cap reloads at the resolution it was actually painted with
                float res = FitResolution(localRect, resolution, out _, out _);
                mask = new PaintMask(localRect, res, scratch.width, scratch.height,
                    scratch.GetPixels32());
            }
            DestroyGenerated(scratch);
            return mask;
        }

        /// <summary>Grow to <paramref name="localRect"/> (already merged by the caller),
        /// preserving painted content by copying the old raster to
        /// floor((old.min - new.min) * resolution) — port of the blit_rect branch of
        /// _ensure_mask. Resolution is kept so the copy is a pure translation. Returns false
        /// when the request would exceed the size cap; the mask then stays as it is and stamps
        /// outside it are clipped away.</summary>
        public bool Grow(Rect localRect)
        {
            if (!isValid)
                return false;
            int width = SizePixels(localRect.width, m_Resolution);
            int height = SizePixels(localRect.height, m_Resolution);
            if (width > k_MaxSizePixels || height > k_MaxSizePixels)
                return false;
            if (width == m_Width && height == m_Height && localRect == m_Rect)
                return true;

            var pixels = new Color32[width * height];
            int offsetX = Mathf.FloorToInt((m_Rect.xMin - localRect.xMin) * m_Resolution);
            int offsetY = Mathf.FloorToInt((m_Rect.yMin - localRect.yMin) * m_Resolution);
            for (int y = 0; y < m_Height; y++)
            {
                int destY = y + offsetY;
                if (destY < 0 || destY >= height)
                    continue;
                int sourceRow = y * m_Width;
                int destRow = destY * width;
                for (int x = 0; x < m_Width; x++)
                {
                    int destX = x + offsetX;
                    if (destX < 0 || destX >= width)
                        continue;
                    pixels[destRow + destX] = m_Pixels[sourceRow + x];
                }
            }

            m_Pixels = pixels;
            m_Rect = localRect;
            m_Width = width;
            m_Height = height;
            // the texture is reallocated on the next Flush; ShapePaint re-reads .texture every
            // sync so nothing keeps a stale handle
            DestroyGenerated(m_Texture);
            m_Texture = null;
            m_Block = null;
            ClearDirty();
            MarkDirty(0, 0, width - 1, height - 1);
            return true;
        }

        /// <summary>Stamp one brush dab. <paramref name="centerPixels"/> and
        /// <paramref name="radiusPixels"/> are in MASK pixels. Verbatim port of _stamp_cpu
        /// (lines 279-299) including the pixel-centre +0.5 sampling and the hard distance
        /// clamp at the radius.</summary>
        public void Stamp(Vector2 centerPixels, float radiusPixels, bool erase, int channel,
            float softness)
        {
            if (!isValid || radiusPixels <= 0f)
                return;
            int x0 = Mathf.Max(Mathf.FloorToInt(centerPixels.x - radiusPixels), 0);
            int y0 = Mathf.Max(Mathf.FloorToInt(centerPixels.y - radiusPixels), 0);
            int x1 = Mathf.Min(Mathf.CeilToInt(centerPixels.x + radiusPixels), m_Width - 1);
            int y1 = Mathf.Min(Mathf.CeilToInt(centerPixels.y + radiusPixels), m_Height - 1);
            if (x0 > x1 || y0 > y1)
                return;

            float feather = Mathf.Max(radiusPixels * softness, k_MinFeather);
            Color32 tint = ChannelColor(channel);
            float radiusSq = radiusPixels * radiusPixels;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * m_Width;
                float dy = y + 0.5f - centerPixels.y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - centerPixels.x;
                    float distanceSq = dx * dx + dy * dy;
                    if (distanceSq > radiusSq)
                        continue;
                    float a = Mathf.Clamp01((radiusPixels - Mathf.Sqrt(distanceSq)) / feather);
                    Color32 current = m_Pixels[row + x];
                    if (erase)
                    {
                        // erase only lowers coverage; the weights underneath survive so that
                        // re-painting the same spot returns the previous blend
                        byte alpha = ToByte(Mathf.Min(current.a * (1f / 255f), 1f - a));
                        m_Pixels[row + x] = new Color32(current.r, current.g, current.b, alpha);
                    }
                    else
                    {
                        m_Pixels[row + x] = new Color32(
                            LerpByte(current.r, tint.r, a),
                            LerpByte(current.g, tint.g, a),
                            LerpByte(current.b, tint.b, a),
                            (byte)Mathf.Max(current.a, ToByte(a)));
                    }
                }
            }
            MarkDirty(x0, y0, x1, y1);
        }

        /// <summary>Port of _channel_color: slot 1/2/3 tint the R/G/B weight channel.</summary>
        public static Color32 ChannelColor(int channel)
        {
            switch (channel)
            {
                case 1:
                    return new Color32(0, 255, 0, 255);
                case 2:
                    return new Color32(0, 0, 255, 255);
                default:
                    return new Color32(255, 0, 0, 255);
            }
        }

        /// <summary>PNG bytes for DrawnShapeAsset.maskPng — the undo snapshot unit and the
        /// serialised form, exactly like Godot's save_png_to_buffer in commit().</summary>
        public byte[] EncodePng()
        {
            Flush();
            if (m_Texture == null)
                return System.Array.Empty<byte>();
            return ImageConversion.EncodeToPNG(m_Texture) ?? System.Array.Empty<byte>();
        }

        /// <summary>Upload pending stamps. Only the dirty block is copied CPU-side; Unity has no
        /// partial upload for Texture2D so Apply still refreshes the whole surface, which is why
        /// the size cap above matters more than the block does.</summary>
        public void Flush()
        {
            if (!isValid)
                return;
            bool created = EnsureTexture();
            if (!created && m_DirtyMaxX < m_DirtyMinX)
                return;
            if (created)
                MarkDirty(0, 0, m_Width - 1, m_Height - 1);

            int x0 = Mathf.Clamp(m_DirtyMinX, 0, m_Width - 1);
            int y0 = Mathf.Clamp(m_DirtyMinY, 0, m_Height - 1);
            int x1 = Mathf.Clamp(m_DirtyMaxX, 0, m_Width - 1);
            int y1 = Mathf.Clamp(m_DirtyMaxY, 0, m_Height - 1);
            int blockWidth = x1 - x0 + 1;
            int blockHeight = y1 - y0 + 1;
            if (blockWidth == m_Width && blockHeight == m_Height)
            {
                m_Texture.SetPixels32(m_Pixels);
            }
            else
            {
                int count = blockWidth * blockHeight;
                // exact length on purpose: the block overload of SetPixels32 is documented to
                // take exactly blockWidth * blockHeight colours, and a steady drag reuses the
                // same block size anyway
                if (m_Block == null || m_Block.Length != count)
                    m_Block = new Color32[count];
                for (int y = 0; y < blockHeight; y++)
                {
                    int source = (y0 + y) * m_Width + x0;
                    int dest = y * blockWidth;
                    for (int x = 0; x < blockWidth; x++)
                        m_Block[dest + x] = m_Pixels[source + x];
                }
                // UNVERIFIED: Texture2D.SetPixels32(x, y, blockWidth, blockHeight, Color32[]) —
                // the block overload could not be checked against local Unity sources. If it is
                // missing, delete this branch and always take the full SetPixels32(m_Pixels)
                // path above; behaviour is identical, only the CPU copy grows.
                m_Texture.SetPixels32(x0, y0, blockWidth, blockHeight, m_Block);
            }
            m_Texture.Apply(false);
            ClearDirty();
        }

        /// <summary>Drop the GPU texture. The pixel array stays, so the mask can still be
        /// encoded; the texture is rebuilt on the next Flush.</summary>
        public void Release()
        {
            DestroyGenerated(m_Texture);
            m_Texture = null;
            m_Block = null;
            ClearDirty();
            MarkDirty(0, 0, m_Width - 1, m_Height - 1);
        }

        // --- local-space rect helpers (Godot Rect2 has these, UnityEngine.Rect does not) ---

        /// <summary>Godot Rect2.encloses: inclusive containment of one rect in another.</summary>
        public static bool Encloses(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin && inner.yMin >= outer.yMin
                && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;
        }

        /// <summary>Godot Rect2.merge: smallest rect containing both.</summary>
        public static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        /// <summary>Godot Rect2.expand: grow to include a point.</summary>
        public static Rect Expand(Rect rect, Vector2 point)
        {
            float xMin = Mathf.Min(rect.xMin, point.x);
            float yMin = Mathf.Min(rect.yMin, point.y);
            float xMax = Mathf.Max(rect.xMax, point.x);
            float yMax = Mathf.Max(rect.yMax, point.y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        /// <summary>Godot Rect2.grow: push every side out by <paramref name="amount"/>.</summary>
        public static Rect Grow(Rect rect, float amount)
        {
            return new Rect(rect.xMin - amount, rect.yMin - amount,
                rect.width + amount * 2f, rect.height + amount * 2f);
        }

        // --- internals ---------------------------------------------------------------

        private static int SizePixels(float localSize, float resolution)
        {
            return Mathf.Max(Mathf.CeilToInt(localSize * resolution), k_MinSizePixels);
        }

        /// <summary>Raster size for a rect at a requested resolution, lowering the resolution
        /// (never cropping the rect) when the cap would be blown. Deterministic for a given
        /// rect + resolution, so <see cref="FromPng"/> can recover the value <see cref="Create"/>
        /// settled on without storing it.</summary>
        private static float FitResolution(Rect localRect, float resolution, out int width,
            out int height)
        {
            float res = Mathf.Max(resolution, 1e-3f);
            width = SizePixels(localRect.width, res);
            height = SizePixels(localRect.height, res);
            if (width <= k_MaxSizePixels && height <= k_MaxSizePixels)
                return res;
            float limitX = localRect.width > 0f ? k_MaxSizePixels / localRect.width : res;
            float limitY = localRect.height > 0f ? k_MaxSizePixels / localRect.height : res;
            res = Mathf.Max(Mathf.Min(res, Mathf.Min(limitX, limitY)), 1e-3f);
            width = Mathf.Min(SizePixels(localRect.width, res), k_MaxSizePixels);
            height = Mathf.Min(SizePixels(localRect.height, res), k_MaxSizePixels);
            return res;
        }

        private bool EnsureTexture()
        {
            if (m_Texture != null && m_Texture.width == m_Width && m_Texture.height == m_Height)
                return false;
            DestroyGenerated(m_Texture);
            // linear: the mask stores weights and coverage, never colour — an sRGB decode on
            // sampling would bend the blend. Bilinear keeps the feathered edge smooth (Godot
            // samples the display texture with the project's default canvas filter).
            m_Texture = new Texture2D(m_Width, m_Height, TextureFormat.RGBA32, false, true)
            {
                name = k_TextureName,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return true;
        }

        private void ClearDirty()
        {
            m_DirtyMinX = 0;
            m_DirtyMinY = 0;
            m_DirtyMaxX = -1;
            m_DirtyMaxY = -1;
        }

        private void MarkDirty(int x0, int y0, int x1, int y1)
        {
            if (m_DirtyMaxX < m_DirtyMinX)
            {
                m_DirtyMinX = x0;
                m_DirtyMinY = y0;
                m_DirtyMaxX = x1;
                m_DirtyMaxY = y1;
                return;
            }
            m_DirtyMinX = Mathf.Min(m_DirtyMinX, x0);
            m_DirtyMinY = Mathf.Min(m_DirtyMinY, y0);
            m_DirtyMaxX = Mathf.Max(m_DirtyMaxX, x1);
            m_DirtyMaxY = Mathf.Max(m_DirtyMaxY, y1);
        }

        /// <summary>Godot Image.set_pixel on an RGBA8 image quantises with round(v * 255).</summary>
        private static byte ToByte(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }

        private static byte LerpByte(byte from, byte to, float t)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(from, to, t)), 0, 255);
        }

        /// <summary>Same policy as DrawnShapeRenderer: generated objects never reach a scene, so
        /// they are torn down immediately in the editor and deferred in play mode.</summary>
        private static void DestroyGenerated(Object generated)
        {
            if (generated == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(generated);
            else
                Object.DestroyImmediate(generated);
        }
    }
}
