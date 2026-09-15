using System;
using GhostMap.Shared.Domain;

namespace GhostMap.Viewer.Scene
{
    /// <summary>
    /// The shape every "current accepted scene" producer exposes:
    /// <see cref="ViewerSceneStore"/> (raw scanner-accepted state) and
    /// <see cref="ViewerEditableScene"/> (the V5 ownership layer built on top
    /// of it). Renderers and camera/interaction code depend on this
    /// interface, never on a concrete type, so they work identically whether
    /// wired to the raw scanner stream or to the edit-aware one.
    /// </summary>
    public interface IViewerSceneSource
    {
        SceneSnapshot Current { get; }

        event Action<SceneSnapshot> Changed;
    }
}
