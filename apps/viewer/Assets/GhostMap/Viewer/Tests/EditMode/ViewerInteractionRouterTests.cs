using GhostMap.Viewer.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// The one pure piece of Task V5's input coordinator: the click-vs-drag
    /// threshold. Everything else in <see cref="ViewerInteractionRouter"/>
    /// only reads <c>Input</c> and dispatches to
    /// <see cref="ObjectSelectionController"/>/<see cref="ObjectEditController"/>/
    /// <see cref="MeasurementController"/>, each covered directly by its own
    /// EditMode tests with explicit rays — the same split V4's
    /// <c>OrbitCameraController</c>/<c>OrbitCameraRig</c> established.
    /// </summary>
    public sealed class ViewerInteractionRouterTests
    {
        [Test]
        public void NoMovementIsAClick()
        {
            Assert.IsTrue(ViewerInteractionRouter.IsClick(new Vector2(100f, 100f), new Vector2(100f, 100f), 6f));
        }

        [Test]
        public void MovementUnderTheThresholdIsStillAClick()
        {
            Assert.IsTrue(ViewerInteractionRouter.IsClick(new Vector2(100f, 100f), new Vector2(103f, 101f), 6f));
        }

        [Test]
        public void MovementAtExactlyTheThresholdIsAClick()
        {
            Assert.IsTrue(ViewerInteractionRouter.IsClick(new Vector2(0f, 0f), new Vector2(6f, 0f), 6f));
        }

        [Test]
        public void MovementBeyondTheThresholdIsADrag()
        {
            Assert.IsFalse(ViewerInteractionRouter.IsClick(new Vector2(0f, 0f), new Vector2(50f, 0f), 6f));
        }
    }
}
