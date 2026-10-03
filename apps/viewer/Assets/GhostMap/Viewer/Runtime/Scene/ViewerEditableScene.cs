using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Viewer.Scene
{
    /// <summary>
    /// Task V5's ownership layer, sitting between the scanner-authoritative
    /// <see cref="ViewerSceneStore"/> and every renderer/camera/interaction
    /// consumer:
    ///
    /// <code>
    /// ViewerSceneStore            (scanner authority; frozen since V1)
    ///     down-arrow accepted scanner snapshots
    /// ViewerEditableScene         (this type; V5)
    ///     down-arrow the effective scene: live pre-finalization, locally
    ///     down-arrow edited post-finalization
    /// RoomRenderer / OrbitCameraController / selection &amp; edit controllers
    /// </code>
    ///
    /// <para><b>Ownership rule (implementation plan section 13/Task V5,
    /// <c>ADR-0003</c>):</b> while <c>finalized == false</c> the scanner is
    /// authoritative and every accepted snapshot passes straight through, with
    /// editing disabled. The instant a snapshot with <c>finalized == true</c>
    /// arrives for the tracked session, this type takes over: further
    /// scanner-originated traffic for that <i>same</i> session is ignored
    /// (protecting local edits from a duplicate/reconnect resend of the same
    /// finalized revision, and defensively from any other stray post-
    /// finalization send, which <c>ADR-0003</c> says should not happen), while
    /// <see cref="TryApplyLocalEdit"/> becomes the only way <see cref="Current"/>
    /// changes.</para>
    ///
    /// <para><b>A new session always wins.</b> A different <c>sessionId</c> —
    /// even at a lower revision, exactly like <see cref="ViewerSceneStore"/>'s
    /// own rule — replaces <see cref="Current"/> unconditionally and resets
    /// <see cref="EditingEnabled"/> from the incoming snapshot, discarding any
    /// local edits from the prior room. A reset/new Scanner session can never
    /// silently merge with edits from the room it replaces.</para>
    ///
    /// <para>Stale and duplicate-revision resends never reach this type at
    /// all: <see cref="ViewerSceneStore"/> already filters them out before its
    /// own <c>Changed</c> fires, so the "same finalized revision" protection
    /// above is normally never exercised in practice — it exists as defense
    /// in depth, and the guarantee is proved directly against the real
    /// <see cref="ViewerSceneStore"/> pipeline in
    /// <c>ViewerEditableSceneOwnershipTests</c>.</para>
    /// </summary>
    public sealed class ViewerEditableScene : IViewerSceneSource
    {
        private IViewerSceneSource _source;

        public SceneSnapshot Current { get; private set; }

        /// <summary>
        /// True once the tracked session's scan has been finalized locally.
        /// Every editing affordance (drag, resize, rotate, inspector fields)
        /// must gate on this — never on <c>Current.finalized</c> directly —
        /// so the single enabling condition lives in one place.
        /// </summary>
        public bool EditingEnabled { get; private set; }

        public event Action<SceneSnapshot> Changed;

        public void Attach(IViewerSceneSource source)
        {
            Detach();

            _source = source;
            if (_source == null)
            {
                return;
            }

            _source.Changed += OnSourceChanged;

            if (_source.Current != null)
            {
                OnSourceChanged(_source.Current);
            }
        }

        public void Detach()
        {
            if (_source != null)
            {
                _source.Changed -= OnSourceChanged;
                _source = null;
            }
        }

        private void OnSourceChanged(SceneSnapshot incoming)
        {
            if (incoming == null)
            {
                return;
            }

            bool isNewSession = Current == null || incoming.sessionId != Current.sessionId;

            if (!isNewSession && EditingEnabled)
            {
                // We already own this session locally. Per ADR-0003 the
                // scanner is read-only/finished for this session once
                // finalized, so any further same-session traffic — a
                // duplicate/reconnect resend of the finalized revision, or
                // (defensively) anything else — must not clobber local edits.
                return;
            }

            Current = Clone(incoming);
            EditingEnabled = Current.finalized;
            Changed?.Invoke(Current);
        }

        /// <summary>
        /// Applies a validated local edit to exactly one object, bumping the
        /// viewer-owned revision (implementation plan section 13.3/13.4:
        /// "increment viewer-side revision"). Fails without side effects if
        /// editing is disabled, no scene is loaded, the object id is unknown,
        /// or the edit does not pass <see cref="FurnitureValidator"/> — the
        /// same shared validator the scanner's own snapshots are checked
        /// against, never a Viewer reimplementation of the rules.
        /// </summary>
        public bool TryApplyLocalEdit(SceneObjectModel updated, out string error)
        {
            if (!EditingEnabled)
            {
                error = "Editing is disabled until the scan is finalized.";
                return false;
            }

            if (updated == null)
            {
                error = "Object is null.";
                return false;
            }

            if (Current?.room?.objects == null)
            {
                error = "No scene loaded.";
                return false;
            }

            ValidationResult validation = FurnitureValidator.Validate(updated);
            if (!validation.IsValid)
            {
                error = validation.Error;
                return false;
            }

            SceneObjectModel[] objects = Current.room.objects;
            int index = -1;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && objects[i].id == updated.id)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                error = $"Object '{updated.id}' does not exist in the current scene.";
                return false;
            }

            SceneSnapshot next = Clone(Current);
            next.room.objects[index] = Clone(updated);
            next.revision = Current.revision + 1;

            Current = next;
            error = string.Empty;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>
        /// Task V6 (fixed post-review): installs a previously-saved
        /// <see cref="SceneSnapshot"/> as the new effective scene, exactly
        /// like a scanner finalized snapshot would — same clone-and-
        /// <see cref="Changed"/> path, so <c>RoomRenderer</c>/
        /// <c>OrbitCameraController</c>/every interaction controller pick it
        /// up through the one existing <see cref="IViewerSceneSource"/>
        /// channel rather than a second, load-specific rendering path.
        ///
        /// <para><b>This is a public scene-replacement boundary and does not
        /// rely on the caller having already validated anything</b> — it
        /// re-runs <see cref="SceneSnapshotValidator"/> itself (the exact
        /// same check <c>Persistence.ScenePersistence.TryLoad</c> already
        /// ran, not a second copy of the rules) as defense in depth.</para>
        ///
        /// <para><b>Authority rule, exactly `ADR-0003`:</b> the Viewer owns
        /// scene state only after finalization, so only a snapshot whose
        /// <c>finalized</c> flag is already <c>true</c> may become the new
        /// effective scene. <see cref="EditingEnabled"/> is then set from
        /// that same flag (<c>Current.finalized</c>) — never hard-coded to
        /// <c>true</c> — so the two can never silently disagree. A persisted
        /// file always carries <c>finalized == true</c> in practice (the
        /// Save boundary, <c>UI.ViewerHudController.CanSave</c>, refuses to
        /// write a file otherwise), but a hand-edited or corrupted file
        /// claiming otherwise is rejected here rather than quietly becoming
        /// editable.</para>
        ///
        /// <para>Once installed, this reuses <see cref="OnSourceChanged"/>'s
        /// own same-session/EditingEnabled guard: if the tracked scanner
        /// session later resends the *same* <c>sessionId</c> the loaded file
        /// carries (a stale reconnect resend), it is ignored exactly as a
        /// duplicate finalized resend already is; a genuinely different
        /// <c>sessionId</c> still replaces the room, unconditionally, per
        /// <c>ADR-0003</c> — a new scan session always wins.</para>
        ///
        /// <para>Fails without side effects — <see cref="Current"/> is left
        /// exactly as it was — for a null snapshot, one with no room, one
        /// that is not finalized, or one that fails domain validation.</para>
        /// </summary>
        public bool LoadExternalSnapshot(SceneSnapshot loaded, out string error)
        {
            if (loaded == null || loaded.room == null)
            {
                error = "Loaded scene has no room.";
                return false;
            }

            if (!loaded.finalized)
            {
                error = "Loaded scene is not finalized; only a finalized, Viewer-owned scene can be loaded.";
                return false;
            }

            if (!SceneSnapshotValidator.TryValidate(loaded, out string validationError))
            {
                error = validationError;
                return false;
            }

            Current = Clone(loaded);
            EditingEnabled = Current.finalized;
            error = string.Empty;
            Changed?.Invoke(Current);
            return true;
        }

        /// <summary>
        /// Deep-clones through <c>JsonUtility</c> — the same serializer the
        /// wire protocol uses (protocol v1 section 4) — so every field the
        /// frozen schema defines is copied without this type hard-coding its
        /// own field list, which would drift the moment the schema changed.
        /// </summary>
        private static SceneSnapshot Clone(SceneSnapshot snapshot)
            => JsonUtility.FromJson<SceneSnapshot>(JsonUtility.ToJson(snapshot));

        private static SceneObjectModel Clone(SceneObjectModel model)
            => JsonUtility.FromJson<SceneObjectModel>(JsonUtility.ToJson(model));
    }
}
