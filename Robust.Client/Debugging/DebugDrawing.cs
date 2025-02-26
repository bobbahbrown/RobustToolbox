using System;
using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Broadphase;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Prototypes;

namespace Robust.Client.Debugging
{
    /// <inheritdoc />
    public class DebugDrawing : IDebugDrawing
    {
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IInputManager _inputManager = default!;

        private bool _debugColliders;
        private bool _debugPositions;

        /// <inheritdoc />
        public bool DebugColliders
        {
            get => _debugColliders;
            set
            {
                if (value == DebugColliders)
                {
                    return;
                }

                _debugColliders = value;

                if (value && !_overlayManager.HasOverlay<PhysicsOverlay>())
                {
                    _overlayManager.AddOverlay(new PhysicsOverlay(_eyeManager,
                        _prototypeManager, _inputManager, _mapManager));
                }
                else
                {
                    _overlayManager.RemoveOverlay<PhysicsOverlay>();
                }
            }
        }

        /// <inheritdoc />
        public bool DebugPositions
        {
            get => _debugPositions;
            set
            {
                if (value == DebugPositions)
                {
                    return;
                }

                _debugPositions = value;

                if (value && !_overlayManager.HasOverlay<EntityPositionOverlay>())
                {
                    _overlayManager.AddOverlay(new EntityPositionOverlay(_entityManager, _eyeManager));
                }
                else
                {
                    _overlayManager.RemoveOverlay<EntityPositionOverlay>();
                }
            }
        }

        private class PhysicsOverlay : Overlay
        {
            private readonly IEyeManager _eyeManager;
            private readonly IMapManager _mapManager;
            private readonly IInputManager _inputManager;

            public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;
            private readonly ShaderInstance _shader;
            private readonly Font _font;

            private Vector2 _hoverStartScreen = Vector2.Zero;
            private List<IPhysBody> _hoverBodies = new();


            public PhysicsOverlay(IEyeManager eyeMan, IPrototypeManager protoMan, IInputManager inputManager, IMapManager mapManager)
            {
                _eyeManager = eyeMan;
                _inputManager = inputManager;
                _mapManager = mapManager;

                _shader = protoMan.Index<ShaderPrototype>("unshaded").Instance();
                var cache = IoCManager.Resolve<IResourceCache>();
                _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
            }

            /// <inheritdoc />
            protected internal override void Draw(in OverlayDrawArgs args)
            {
                switch (args.Space)
                {
                    case OverlaySpace.ScreenSpace:
                        DrawScreen(args);
                        break;
                    case OverlaySpace.WorldSpace:
                        DrawWorld(args);
                        break;
                }

            }

            private void DrawScreen(in OverlayDrawArgs args)
            {
                var screenHandle = args.ScreenHandle;
                var lineHeight = _font.GetLineHeight(1f);
                Vector2 drawPos = _hoverStartScreen + new Vector2(20, 0) + new Vector2(0, -(_hoverBodies.Count * 4 * lineHeight / 2f));
                int row = 0;

                foreach (var body in _hoverBodies)
                {
                    if (body != _hoverBodies[0])
                    {
                        DrawString(screenHandle, _font, drawPos + new Vector2(0, row * lineHeight), "------");
                        row++;
                    }

                    DrawString(screenHandle, _font, drawPos + new Vector2(0, row * lineHeight), $"Ent: {body.Owner}");
                    row++;
                    DrawString(screenHandle, _font, drawPos + new Vector2(0, row * lineHeight), $"Layer: {Convert.ToString(body.CollisionLayer, 2)}");
                    row++;
                    DrawString(screenHandle, _font, drawPos + new Vector2(0, row * lineHeight), $"Mask: {Convert.ToString(body.CollisionMask, 2)}");
                    row++;
                    DrawString(screenHandle, _font, drawPos + new Vector2(0, row * lineHeight), $"Enabled: {body.CanCollide}, Hard: {body.Hard}, Anchored: {(body).BodyType == BodyType.Static}");
                    row++;
                }

            }

            private void DrawWorld(in OverlayDrawArgs args)
            {
                var worldHandle = args.WorldHandle;
                worldHandle.UseShader(_shader);
                var drawing = new PhysDrawingAdapter(worldHandle);

                _hoverBodies.Clear();
                var mouseScreenPos = _inputManager.MouseScreenPosition;
                var mouseWorldPos = _eyeManager.ScreenToMap(mouseScreenPos).Position;
                _hoverStartScreen = mouseScreenPos.Position;

                var viewport = _eyeManager.GetWorldViewport();

                if (viewport.IsEmpty()) return;

                var mapId = _eyeManager.CurrentMap;
                var sleepThreshold = IoCManager.Resolve<IConfigurationManager>().GetCVar(CVars.TimeToSleep);
                var colorEdge = Color.Red.WithAlpha(0.33f);
                var drawnJoints = new HashSet<Joint>();

                foreach (var physBody in EntitySystem.Get<SharedBroadPhaseSystem>().GetCollidingEntities(mapId, viewport))
                {
                    // all entities have a TransformComponent
                    var transform = physBody.Owner.Transform;

                    var worldBox = physBody.GetWorldAABB(_mapManager);
                    if (worldBox.IsEmpty()) continue;

                    foreach (var fixture in physBody.Fixtures)
                    {
                        var shape = fixture.Shape;
                        var sleepPercent = physBody.Awake ? physBody.SleepTime / sleepThreshold : 1.0f;
                        shape.DebugDraw(drawing, transform.WorldMatrix, in viewport, sleepPercent);
                    }

                    foreach (var joint in physBody.Joints)
                    {
                        if (drawnJoints.Contains(joint)) continue;
                        drawnJoints.Add(joint);

                        joint.DebugDraw(drawing, in viewport);
                    }

                    if (worldBox.Contains(mouseWorldPos))
                    {
                        _hoverBodies.Add(physBody);
                    }

                    // draw AABB
                    worldHandle.DrawRect(worldBox, colorEdge, false);
                }
            }

            private static void DrawString(DrawingHandleScreen handle, Font font, Vector2 pos, string str)
            {
                var baseLine = new Vector2(pos.X, font.GetAscent(1) + pos.Y);

                foreach (var rune in str.EnumerateRunes())
                {
                    var advance = font.DrawChar(handle, rune, baseLine, 1, Color.White);
                    baseLine += new Vector2(advance, 0);
                }
            }

            private class PhysDrawingAdapter : DebugDrawingHandle
            {
                private readonly DrawingHandleWorld _handle;

                public PhysDrawingAdapter(DrawingHandleWorld worldHandle)
                {
                    _handle = worldHandle;
                }

                public override Color WakeMixColor => Color.White;
                public override Color GridFillColor => Color.Blue.WithAlpha(0.05f);
                public override Color RectFillColor => Color.Green.WithAlpha(0.25f);

                public override Color CalcWakeColor(Color color, float wakePercent)
                {
                    var percent = MathHelper.Clamp(wakePercent, 0, 1);

                    var r = 1 - (percent * (1 - color.R));
                    var g = 1 - (percent * (1 - color.G));
                    var b = 1 - (percent * (1 - color.B));

                    return new Color(r, g, b, color.A);
                }

                public override void DrawRect(in Box2 box, in Color color)
                {
                    _handle.DrawRect(box, color);
                }

                public override void DrawRect(in Box2Rotated box, in Color color)
                {
                    _handle.DrawRect(box, color);
                }

                public override void DrawCircle(Vector2 origin, float radius, in Color color)
                {
                    _handle.DrawCircle(origin, radius, color);
                }

                public override void DrawPolygonShape(Vector2[] vertices, in Color color)
                {
                    _handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, vertices, color);
                }

                public override void DrawLine(Vector2 start, Vector2 end, in Color color)
                {
                    _handle.DrawLine(start, end, color);
                }

                public override void SetTransform(in Matrix3 transform)
                {
                    _handle.SetTransform(transform);
                }


            }
        }

        private sealed class EntityPositionOverlay : Overlay
        {
            private readonly IEntityManager _entityManager;
            private readonly IEyeManager _eyeManager;

            public override OverlaySpace Space => OverlaySpace.WorldSpace;

            public EntityPositionOverlay(IEntityManager entityManager, IEyeManager eyeManager)
            {
                _entityManager = entityManager;
                _eyeManager = eyeManager;
            }

            protected internal override void Draw(in OverlayDrawArgs args)
            {
                const float stubLength = 0.25f;

                var worldHandle = (DrawingHandleWorld) args.DrawingHandle;
                foreach (var entity in _entityManager.GetEntities())
                {
                    var transform = entity.Transform;
                    if (transform.MapID != _eyeManager.CurrentMap ||
                        !_eyeManager.GetWorldViewport().Contains(transform.WorldPosition))
                    {
                        continue;
                    }

                    var center = transform.WorldPosition;
                    var xLine = transform.WorldRotation.RotateVec(Vector2.UnitX);
                    var yLine = transform.WorldRotation.RotateVec(Vector2.UnitY);

                    worldHandle.DrawLine(center, center + xLine * stubLength, Color.Red);
                    worldHandle.DrawLine(center, center + yLine * stubLength, Color.Green);
                }
            }
        }
    }
}
