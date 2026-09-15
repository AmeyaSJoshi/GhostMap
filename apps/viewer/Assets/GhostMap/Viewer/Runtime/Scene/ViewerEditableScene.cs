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
