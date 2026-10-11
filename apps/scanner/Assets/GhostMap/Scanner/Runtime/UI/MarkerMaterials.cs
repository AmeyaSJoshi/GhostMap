using UnityEngine;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The material every AR marker is drawn with.
    ///
    /// <para><c>GameObject.CreatePrimitive</c> gives a marker the built-in
    /// Standard material, and a player build strips Standard when no asset in
    /// the build references it, so markers rendered magenta. "Sprites/Default"
    /// is in Always Included Shaders, is unlit (a marker over a camera feed
    /// needs no lighting) and honors <c>material.color</c>, which every HUD
    /// already sets.</para>
    /// </summary>
    public static class MarkerMaterials
    {
        private static Material unlit;

        public static void MakeUnlit(GameObject marker)
        {
            if (unlit == null)
            {
                Shader shader = Shader.Find("Sprites/Default");

                if (shader == null)
                {
                    return;
                }

                unlit = new Material(shader) { name = "GhostMap Marker" };
            }

            var renderer = marker.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = unlit;
            }
        }
    }
}
