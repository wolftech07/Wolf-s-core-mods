using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace TavernNativeMenu
{
    internal static class MenuBranding
    {
        internal const string CommunityName = "The Modded Tavern";
        internal const string DiscordUrl = "https://discord.gg/jNQUUDAYSj";
        private static Texture2D badge;
        private static readonly HashSet<int> applied = new HashSet<int>();
        private static readonly HashSet<int> repositioned = new HashSet<int>();
        private static ServerSelectionMenu activeMenu;
        private static int remainingPasses;
        private static float nextPass;
        private static Material hiddenArtwork;

        internal static void Begin(ServerSelectionMenu menu)
        {
            applied.Clear();
            repositioned.Clear();
            activeMenu = menu;
            remainingPasses = 3;
            nextPass = Time.unscaledTime + 1f;
            Apply(menu);
        }

        internal static void Tick()
        {
            if (activeMenu == null || remainingPasses <= 0 || Time.unscaledTime < nextPass) return;
            remainingPasses--;
            nextPass = Time.unscaledTime + 2f;
            Apply(activeMenu);
        }

        internal static void Apply(ServerSelectionMenu menu)
        {
            if (menu == null) return;
            try
            {
                if (badge == null)
                {
                    using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("TavernNativeMenu.Badge.png"))
                    using (var bytes = new MemoryStream())
                    {
                        if (source == null) throw new InvalidOperationException("Embedded Tavern badge is missing.");
                        source.CopyTo(bytes);
                        badge = new Texture2D(2, 2);
                        if (!ImageConversion.LoadImage(badge, bytes.ToArray())) throw new InvalidDataException("Invalid Tavern badge.");
                        badge.wrapMode = TextureWrapMode.Clamp;
                        badge.filterMode = FilterMode.Trilinear;
                        badge.anisoLevel = 4;
                    }
                }
                int signs = 0, links = 0, removed = 0;
                // Only the instantiated server-picker scene, never loaded prefabs or gameplay scenes.
                foreach (GameObject root in menu.gameObject.scene.GetRootGameObjects())
                {
                    foreach (TextRenderer text in root.GetComponentsInChildren<TextRenderer>(true))
                    {
                        string current = text.Text ?? "";
                        if (current.IndexOf("discord.gg/townshiptale", StringComparison.OrdinalIgnoreCase) < 0 &&
                            current.IndexOf("discord.gg/jNQUUDAYSj", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        // Move the whole Discord sign with its icon. The native
                        // text is rotated 180 degrees: its -right is viewer-left.
                        Transform sign = text.transform;
                        for (Transform parent = text.transform; parent != null; parent = parent.parent)
                            if (parent.name == "DiscordLogo") { sign = parent; break; }
                        if (repositioned.Add(sign.GetInstanceID()))
                            sign.position -= text.transform.right * MenuArtworkRules.WallSignsLeftShiftMeters;
                        if (current != DiscordUrl) { text.Text = DiscordUrl; links++; }
                    }
                    foreach (Transform target in root.GetComponentsInChildren<Transform>(true))
                    {
                        Transform parent = target.parent;
                        if (MenuArtworkRules.RemoveWholeBoard(target.name, parent == null ? null : parent.name,
                            parent == null || parent.parent == null ? null : parent.parent.name))
                        {
                            // Remove the complete known sponsor group, including
                            // backing quad, frame, captions and colliders.
                            if (target.gameObject.activeSelf) { target.gameObject.SetActive(false); removed++; }
                            continue;
                        }
                        if (!MenuArtworkRules.ReplaceBadge(target.name) || applied.Contains(target.GetInstanceID())) continue;
                        if (ReplaceArtwork(target)) { applied.Add(target.GetInstanceID()); signs++; }
                    }
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        if (RemoveSponsorArtwork(renderer)) removed++;
                }
                if (signs + links + removed > 0)
                    MelonLogger.Msg("[Tavern In-Game Hub] In-game branding: " + signs + " badges, " + links + " Discord signs, " + removed + " sponsor/Vivox renderers updated.");
            }
            catch (Exception error) { MelonLogger.Warning("[Tavern In-Game Hub] Menu branding: " + error.Message); }
        }

        private static bool RemoveSponsorArtwork(Renderer renderer)
        {
            if (renderer == null || renderer.forceRenderingOff) return false;
            bool namedObject = false;
            for (Transform parent = renderer.transform; parent != null; parent = parent.parent)
            {
                // Never suppress replacement content parented beneath native artwork.
                if (parent.name == "The Modded Tavern - menu artwork") return false;
                if (MenuArtworkRules.Remove(parent.name)) namedObject = true;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (namedObject || (filter != null && filter.sharedMesh != null && MenuArtworkRules.Remove(filter.sharedMesh.name)))
            {
                renderer.forceRenderingOff = true;
                return true;
            }
            // Static-batched signs can have generic object names. Match their
            // materials/textures too; preserve unrelated slots on shared meshes.
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || material == hiddenArtwork) continue;
                if (!MenuArtworkRules.Remove(material.name) &&
                    !(material.HasProperty("_MainTex") && material.mainTexture != null && MenuArtworkRules.Remove(material.mainTexture.name))) continue;
                if (hiddenArtwork == null)
                {
                    Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
                    if (shader == null) return false;
                    var clear = new Texture2D(1, 1);
                    clear.SetPixel(0, 0, Color.clear);
                    clear.Apply();
                    hiddenArtwork = new Material(shader) { name = "Tavern hidden sponsor artwork", mainTexture = clear };
                }
                materials[i] = hiddenArtwork;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
            return changed;
        }

        private static bool ReplaceArtwork(Transform target)
        {
            Renderer[] originals = target.GetComponentsInChildren<Renderer>(true);
            if (originals.Length == 0) return false;
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in originals)
            {
                // Mesh bounds preserve the sign's orientation, unlike a world-axis bounding box.
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = target.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
            }
            if (first) return false;
            Vector3 size = bounds.size;
            int normal = size.x < size.y && size.x < size.z ? 0 : size.y < size.z ? 1 : 2;
            Quaternion rotation = normal == 0 ? Quaternion.Euler(0, 90, 0) : normal == 1 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            float width = normal == 0 ? size.z : size.x;
            float height = normal == 1 ? size.z : size.y;
            if (width <= 0 || height <= 0) return false;
            Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (shader == null) return false;
            var holder = new GameObject("The Modded Tavern - menu artwork");
            holder.layer = target.gameObject.layer;
            holder.transform.SetParent(target, false);
            holder.transform.localPosition = bounds.center;
            holder.transform.localRotation = rotation;
            // This is the wall badge alongside Discord; leave the filter-board
            // badge in its own original layout. Credits' +X is viewer-left.
            if (target.parent != null && target.parent.name == "Credits")
                holder.transform.position += target.parent.right * MenuArtworkRules.WallSignsLeftShiftMeters;
            float normalScale = holder.transform.TransformVector(Vector3.forward).magnitude;
            float surfaceGap = MenuArtworkRules.BadgeSurfaceGapMeters / Math.Max(normalScale, 0.0001f);
            // Both faces remain readable for signs whose original mesh used reversed normals.
            for (int side = 0; side < 2; side++)
            {
                var face = new GameObject("Tavern sign face");
                face.layer = holder.layer;
                face.transform.SetParent(holder.transform, false);
                face.transform.localRotation = Quaternion.Euler(0, side * 180, 0);
                face.transform.localPosition = new Vector3(0, 0, (side == 0 ? -1 : 1) * (bounds.size[normal] * 0.51f + surfaceGap));
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.layer = holder.layer;
                UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(face.transform, false);
                float diameter = Math.Min(width, height);
                quad.transform.localScale = new Vector3(diameter, diameter, 1);
                quad.GetComponent<Renderer>().material = new Material(shader) { mainTexture = badge };
            }
            foreach (Renderer renderer in originals) renderer.enabled = false;
            return true;
        }
    }
}
