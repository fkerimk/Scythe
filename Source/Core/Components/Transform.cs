using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;
using static Raylib_cs.Raylib;

[JsonObject(MemberSerialization.OptIn)]
internal class Transform(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaArrows;
    public override Color LabelColor => Colors.GuiTypeTransform;

    #region Local Transform

    [Label("Pos"), RecordHistory, JsonProperty]
    public new Vector3 Pos {
        get;
        set {
            field = value;
            PrefabUtility.UpdateTransformOverrideState(this, nameof(Pos), value);
            UpdateTransform();
        }
    } = Vector3.Zero;

    [Label("Euler"), RecordHistory, JsonProperty]
    public Vector3 Euler {
        get {

            var e = Raymath.QuaternionToEuler(Rot).ToDeg();

            return new Vector3(e.X, -e.Y, -e.Z);

        }
        set {

            var q = Raymath.QuaternionFromEuler(value.X.DegToRad(), (-value.Y).DegToRad(), (-value.Z).DegToRad());

            if (MathF.Abs(Quaternion.Dot(Rot, q)) > 0.9999f) return;

            PrefabUtility.UpdateTransformOverrideState(this, nameof(Euler), value);
            Rot = q;
        }
    }

    [Label("Scale"), RecordHistory, JsonProperty]
    public Vector3 Scale {
        get;
        set {
            field = value;
            PrefabUtility.UpdateTransformOverrideState(this, nameof(Scale), value);
            UpdateTransform();
        }
    } = Vector3.One;

    [RecordHistory, JsonProperty]
    public new Quaternion Rot {
        get;
        set {
            field = value;
            PrefabUtility.UpdateTransformOverrideState(this, nameof(Rot), value);
            UpdateTransform();
        }
    } = Quaternion.Identity;

    #endregion

    #region World Transform

    public Vector3 WorldPos {
        get {

            Obj.DecomposeWorldMatrix(out var trans, out _, out _);

            return trans;

        }
        set {

            if (Obj.Parent == null)
                Pos = value;
            else {

                var invParent = Raymath.MatrixInvert(Obj.Parent.WorldMatrix);
                Pos = Raymath.Vector3Transform(value, invParent);
            }
        }
    }

    public Quaternion WorldRot {
        get => Obj.Parent == null ? Rot : Obj.Parent.Transform.WorldRot * Rot;
        set {

            if (Obj.Parent == null)
                Rot = value;
            else {

                var parentRot = Obj.Parent.Transform.WorldRot;
                var q = Quaternion.Inverse(parentRot) * value;

                if (MathF.Abs(Quaternion.Dot(Rot, q)) > 0.9999f) return;

                Rot = q;
            }
        }
    }

    public Vector3 WorldEuler {
        get {

            var e = Raymath.QuaternionToEuler(WorldRot).ToDeg();

            return new Vector3(e.X, -e.Y, -e.Z);

        }
        set {

            var q = Raymath.QuaternionFromEuler(value.X.DegToRad(), (-value.Y).DegToRad(), (-value.Z).DegToRad());

            if (MathF.Abs(Quaternion.Dot(WorldRot, q)) > 0.9999f) return;

            WorldRot = q;
        }
    }

    public Vector3 WorldScale {
        get {

            Obj.DecomposeWorldMatrix(out _, out _, out var scale);

            return scale;

        }
        set {

            if (Obj.Parent == null)
                Scale = value;
            else {

                var parentScale = Obj.Parent.Transform.WorldScale;
                Scale = value / parentScale;
            }
        }
    }

    #endregion

    private const float MoveSnap = 0.2f;

    private static int _mode;
    private float _activeMove;
    private string _activeId = "";

    private bool _isWorldSpace, _canUseShortcuts;

    private Vector3 _activePos, _activeWorldPos, _activeLocalAxis, _activeScale, _activeNormal, _activeInitialPoint, _activeInitialVector;

    private Vector2 _activeMouseTemp;
    private Quaternion _activeRot = Quaternion.Identity;

    private Vector3 _visualVel;
    private Vector3 _visualPos;
    private Quaternion _visualRot = Quaternion.Identity;
    private Vector3 _visualScale = Vector3.One;

    private float GetCartoonStretchSizeFactor() {

        foreach (var component in Obj.ComponentEntries.Values) {

            if (component is not Model { IsLoaded: true } model) continue;

            var worldBounds = model.GetWorldBoundsSize();

            return MathF.Max(MathF.Max(worldBounds.X, worldBounds.Y), MathF.Max(worldBounds.Z, 0.0001f));
        }

        return 0f;
    }

    public void UpdateTransform() {

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Obj == null) return;

        var rotMatrix = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(Rot));

        var matrix = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Raymath.MatrixScale(Scale.X, Scale.Y, Scale.Z), rotMatrix), Raymath.MatrixTranslate(Pos.X, Pos.Y, Pos.Z));

        Obj.RotMatrix = rotMatrix;
        Obj.Matrix = matrix;

        RefreshWorldMatrices(Obj);
    }

    private static void RefreshWorldMatrices(Obj obj) {

        if (obj.Parent != null) {

            obj.WorldMatrix = obj.Parent.WorldMatrix * obj.Matrix;
            obj.WorldRotMatrix = obj.Parent.WorldRotMatrix * obj.RotMatrix;

        } else {

            obj.WorldMatrix = obj.Matrix;
            obj.WorldRotMatrix = obj.RotMatrix;
        }

        foreach (var child in obj.ChildEntries) RefreshWorldMatrices(child.Value);
    }

    public bool IsHovered { get; private set; }
    public bool IsDragging => !string.IsNullOrEmpty(_activeId);

    public override void Logic() { UpdateTransform(); }

    internal void UpdateCartoon() {

        var dt = GetFrameTime();
        if (dt > 0.1f) dt = 0.1f;

        if (dt <= 0) return;

        var targetPos = WorldPos;
        var targetRot = WorldRot;

        if (Quaternion.Dot(_visualRot, targetRot) < 0) targetRot = Quaternion.Negate(targetRot);

        // Force reset if too far (e.g. teleporting or level load)
        if (Vector3.Distance(_visualPos, targetPos) > 10f) {

            _visualPos = targetPos;
            _visualRot = targetRot;
            _visualVel = Vector3.Zero;
        }

        // Initialize if first frame
        if (_visualPos == Vector3.Zero && targetPos != Vector3.Zero) {

            _visualPos = targetPos;
            _visualRot = targetRot;
        }

        // Only process bounce if selected, moving, or still settling
        var isMoving = _visualVel.LengthSquared() > 0.0001f;
        var isRotating = MathF.Abs(Quaternion.Dot(_visualRot, targetRot)) < 0.9999f;

        if (!Obj.IsSelected && !isMoving && !isRotating) {

            _visualPos = targetPos;
            _visualRot = targetRot;

            return;
        }

        // Pos Spring logic
        var posDiff = targetPos - _visualPos;

        _visualVel += posDiff * 600f * dt;
        _visualVel *= MathF.Exp(-22f * dt);
        _visualPos += _visualVel * dt;

        // Rot Lag logic
        _visualRot = Quaternion.Slerp(_visualRot, targetRot, 1f - MathF.Exp(-18f * dt));

        // Building Matrix (Follows UpdateTransform logic: Scale * Rotation * Translation)

        // SCALE FIX: We extract the clean scale matrix directly from WorldMatrix to avoid "skew" distortion
        var mWorldTransInv = Raymath.MatrixInvert(Raymath.MatrixTranslate(targetPos.X, targetPos.Y, targetPos.Z));
        var mWorldRotInv = Raymath.MatrixInvert(Obj.WorldRotMatrix);
        var mScaleWorld = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Obj.WorldMatrix, mWorldTransInv), mWorldRotInv);

        var mRotVisual = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(_visualRot));

        // Combine Scale * Rotation (Pose kept 100% same, just scale is now undistorted)
        var baseMatrix = Raymath.MatrixMultiply(mScaleWorld, mRotVisual);

        // 3. Squash & Stretch (Aligned with velocity)
        var speed = _visualVel.Length();

        if (speed > 0.1f) {

            var sizeFactor = GetCartoonStretchSizeFactor();
            if (sizeFactor <= 0f) {

                var mTransOnly = Raymath.MatrixTranslate(_visualPos.X, _visualPos.Y, _visualPos.Z);
                Obj.VisualWorldMatrix = Raymath.MatrixMultiply(baseMatrix, mTransOnly);

                return;
            }

            var stretch = MathF.Min(speed * 0.05f / sizeFactor, 0.4f);
            var dir = Vector3.Normalize(_visualVel);
            var s = 1f + stretch;
            var k = 1f / MathF.Sqrt(s);

            // Build Row-Major stretch matrix and then transpose it
            var stretchM = new Matrix4x4(k + (s - k) * dir.X * dir.X, (s - k) * dir.X * dir.Y, (s - k) * dir.X * dir.Z, 0, (s - k) * dir.Y * dir.X, k + (s - k) * dir.Y * dir.Y, (s - k) * dir.Y * dir.Z, 0, (s - k) * dir.Z * dir.X, (s - k) * dir.Z * dir.Y, k + (s - k) * dir.Z * dir.Z, 0, 0, 0, 0, 1);

            baseMatrix = Raymath.MatrixMultiply(baseMatrix, Matrix4x4.Transpose(stretchM));
        }

        // 4. Final Translation
        var mTrans = Raymath.MatrixTranslate(_visualPos.X, _visualPos.Y, _visualPos.Z);

        // Write the final corrected visual matrix
        Obj.VisualWorldMatrix = Raymath.MatrixMultiply(baseMatrix, mTrans);
    }

    public override void Render3D() {

        if (CommandLine.Runtime || Core.ActiveCamera == null || Core.IsPreviewRender) return;

        // Only draw gizmo for the "main" selected object to avoid clutter
        if (Obj != LevelBrowser.SelectedObject) return;

        if (_canUseShortcuts && _activeMove == 0 && Editor.EditorRender.IsHovered) {

            if (IsKeyPressed(KeyboardKey.Q)) _mode = 0;
            if (IsKeyPressed(KeyboardKey.W)) _mode = 1;
            if (IsKeyPressed(KeyboardKey.E)) _mode = 2;
            if (IsKeyPressed(KeyboardKey.X)) _isWorldSpace = !_isWorldSpace;
        }

        var transformShader = AssetManager.Get<ShaderAsset>("Collection/transform.fs");
        if (transformShader != null) BeginShaderMode(transformShader.Shader);

        var ray = GetScreenToWorldRay(EditorRender.RelativeMouse3D, Core.ActiveCamera.Raylib);

        var useWorld = _isWorldSpace && _mode != 2;

        var r = useWorld ? Vector3.UnitX : Obj.Right;
        var u = useWorld ? Vector3.UnitY : Obj.Up;
        var f = useWorld ? Vector3.UnitZ : Obj.Fwd;

        IsHovered = false;

        Axis("x", Vector3.UnitX, r, new Color(0.9f, 0.3f, 0.3f), ray);
        Axis("y", Vector3.UnitY, u, new Color(0.3f, 0.9f, 0.3f), ray);
        Axis("z", Vector3.UnitZ, f, new Color(0.3f, 0.3f, 0.9f), ray);

        if (transformShader != null) EndShaderMode();
    }

    public override void Render2D() {

        if (CommandLine.Runtime || !Editor.EditorRender.IsHovered) return;

        _canUseShortcuts = false;

        if (IsCursorHidden()) return;

        if (!Obj.IsSelected) return;

        _canUseShortcuts = true;

        var modeName = _mode switch {
            0 => "Pos",
            1 => "Rot",
            2 => "Scale",
            _ => "Bruh"
        };
        var spaceName = _mode == 2 ? "" : _isWorldSpace ? "(World)" : "(Local)";

        var textA = $"{modeName} {spaceName}";
        var textPosA = Editor.EditorRender.RelativeMouse with { Y = Editor.EditorRender.RelativeMouse.Y - 15 };

        DrawTextEx(Fonts.RlMontserratRegular, textA, new Vector2(textPosA.X - 13, textPosA.Y - 19), 20, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, textA, new Vector2(textPosA.X - 14, textPosA.Y - 19), 20, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, textA, new Vector2(textPosA.X - 14, textPosA.Y - 20), 20, 1, Color.Yellow);
        DrawTextEx(Fonts.RlMontserratRegular, textA, new Vector2(textPosA.X - 15, textPosA.Y - 20), 20, 1, Color.Yellow);

        if (_activeMove == 0) return;

        var textB = _mode switch {
            0 or 2 => $"{_activeMove:F2}m",
            1      => $"{_activeMove:F2}°",
            _      => $"{_activeMove:F2}"
        };
        var textPosB = Editor.EditorRender.RelativeMouse with { Y = Editor.EditorRender.RelativeMouse.Y - 15 };

        DrawTextEx(Fonts.RlMontserratRegular, textB, new Vector2(textPosB.X - 13, textPosB.Y - 39), 20, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, textB, new Vector2(textPosB.X - 14, textPosB.Y - 39), 20, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, textB, new Vector2(textPosB.X - 14, textPosB.Y - 40), 20, 1, Color.Yellow);
        DrawTextEx(Fonts.RlMontserratRegular, textB, new Vector2(textPosB.X - 15, textPosB.Y - 40), 20, 1, Color.Yellow);
    }

    private void Axis(string id, Vector3 axis, Vector3 normal, Color color, Ray ray) {

        if (Core.ActiveCamera == null) return;

        var isActive = _activeId == id;

        Obj.DecomposeWorldMatrix(out var worldPos, out _, out _);

        var a = worldPos + Raymath.Vector3Normalize(normal) * 0.1f;
        var b = a + normal * 1.5f;

        if (!string.IsNullOrEmpty(_activeId) && _activeId != id) {

            DrawLine3D(a, b, color);

            return;
        }

        if (isActive) {

            switch (_mode) {

                case 0: {
                    // Position

                    var currentPoint = GetClosestPointLineRay(_activeWorldPos, _activeNormal, ray);
                    var delta = currentPoint - _activeInitialPoint;

                    if (!IsKeyDown(KeyboardKey.LeftShift)) {

                        delta.X = MathF.Round(delta.X / MoveSnap) * MoveSnap;
                        delta.Y = MathF.Round(delta.Y / MoveSnap) * MoveSnap;
                        delta.Z = MathF.Round(delta.Z / MoveSnap) * MoveSnap;
                    }

                    var diff = delta - (_activeWorldPos - worldPos);

                    foreach (var target in LevelBrowser.SelectedObjects) {

                        var newWorldPos = target.Transform.WorldPos - diff;
                        target.Transform.WorldPos = newWorldPos;
                    }

                    _activeMove = Vector3.Dot(delta, _activeNormal);

                    break;
                }

                case 1: {
                    // Rotation

                    if (IntersectRayPlane(ray, worldPos, _activeNormal, out var currentPoint)) {

                        var currentVector = Vector3.Normalize(currentPoint - _activeWorldPos);
                        var angleRad = MathF.Atan2(Vector3.Dot(_activeNormal, Vector3.Cross(_activeInitialVector, currentVector)), Vector3.Dot(_activeInitialVector, currentVector));
                        var angleDeg = angleRad * (180f / MathF.PI);

                        if (!IsKeyDown(KeyboardKey.LeftShift)) angleDeg = MathF.Round(angleDeg / 22.5f) * 22.5f;

                        var deltaAngle = angleDeg - _activeMove;
                        _activeMove = angleDeg;

                        var deltaRot = Quaternion.CreateFromAxisAngle(_activeLocalAxis, deltaAngle.DegToRad());

                        foreach (var target in LevelBrowser.SelectedObjects) {

                            if (_isWorldSpace) {

                                if (target.Parent != null) {

                                    var parentRot = target.Parent.Transform.WorldRot;
                                    var localDeltaInParentSpace = Quaternion.Inverse(parentRot) * deltaRot * parentRot;
                                    target.Transform.Rot = localDeltaInParentSpace * target.Transform.Rot;

                                } else
                                    target.Transform.Rot = deltaRot * target.Transform.Rot;

                            } else
                                target.Transform.Rot *= deltaRot;

                            // Preserve hemisphere
                            if (Quaternion.Dot(target.Transform.Rot, _activeRot) < 0) target.Transform.Rot = Quaternion.Negate(target.Transform.Rot);
                        }
                    }

                    break;
                }

                case 2: {
                    // Scale

                    var currentPoint = GetClosestPointLineRay(_activeWorldPos, _activeNormal, ray);
                    var delta = currentPoint - _activeInitialPoint;
                    var move = -Vector3.Dot(delta, _activeNormal);

                    if (!IsKeyDown(KeyboardKey.LeftShift)) move = MathF.Round(move / MoveSnap) * MoveSnap;

                    var diff = move - _activeMove;

                    _activeMove = move;

                    foreach (var target in LevelBrowser.SelectedObjects) {

                        var s = target.Transform.Scale;

                        switch (id) {

                            case "x": s.X += diff; break;
                            case "y": s.Y += diff; break;
                            case "z": s.Z += diff; break;
                        }

                        target.Transform.Scale = s;
                    }

                    break;
                }
            }
        }

        const float radius = 0.025f, rayRadius = 0.125f;

        const int rayQuality = 64;

        var isHovered = false;

        if (Editor.EditorRender.IsHovered) {

            if (_mode == 1) {
                // Rotation circle collision

                var normal1 = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
                var normal2 = Vector3.Cross(normal, normal1);

                for (var i = 0; i < rayQuality + 1; i++) {

                    var angle = i / (float)rayQuality * MathF.PI * 2f;
                    var step = worldPos + (normal1 * MathF.Cos(angle) + normal2 * MathF.Sin(angle)) * 1.5f;

                    if (GetRayCollisionSphere(ray, step, rayRadius * 1.5f).Hit) isHovered = true;
                }

            } else {
                // Line collision

                for (var i = 0; i < rayQuality + 1; i++) {

                    var step = Vector3.Lerp(a, b, 1f / rayQuality * i);

                    if (GetRayCollisionSphere(ray, step, rayRadius).Hit) isHovered = true;
                }
            }
        }

        if (isHovered && IsMouseButtonPressed(MouseButton.Left) && Editor.EditorRender.IsHovered) {

            _activeId = id;
            _activeLocalAxis = axis;
            _activePos = Pos;
            _activeRot = Rot;
            _activeScale = Scale;

            _activeMouseTemp = GetMousePosition();
            _activeNormal = normal;

            _activeWorldPos = worldPos;

            if (_mode == 1 && IntersectRayPlane(ray, _activeWorldPos, _activeNormal, out _activeInitialPoint))
                _activeInitialVector = Vector3.Normalize(_activeInitialPoint - _activeWorldPos);

            else
                _activeInitialPoint = GetClosestPointLineRay(_activeWorldPos, _activeNormal, ray);

            _activeMove = 0; // Reset active move delta tracking

            foreach (var target in LevelBrowser.SelectedObjects) History.StartRecording(target.Transform, "Transform");
        }

        if (isHovered) IsHovered = true;

        if ((isActive && IsMouseButtonReleased(MouseButton.Left)) || IsCursorHidden()) {

            _activeId = "";
            _activeMove = 0;

            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
            History.StopRecording();
        }

        var targetColor = !isActive && isHovered && !IsCursorHidden() ? Color.White : color;

        if (_mode == 1) {
            // Draw rotation circle

            var normal1 = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            var normal2 = Vector3.Cross(normal, normal1);

            for (var i = 0; i < 48; i++) {

                var angle1 = i / 48f * MathF.PI * 2f;
                var angle2 = (i + 1) / 48f * MathF.PI * 2f;
                var p1 = worldPos + (normal1 * MathF.Cos(angle1) + normal2 * MathF.Sin(angle1)) * 1.5f;
                var p2 = worldPos + (normal1 * MathF.Cos(angle2) + normal2 * MathF.Sin(angle2)) * 1.5f;

                DrawCylinderEx(p1, p2, radius, radius, 1, targetColor);
            }
        } else
            DrawCylinderEx(a, b, radius, radius, 1, targetColor);
    }

    public void RotateX(float deg) => Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg.DegToRad()) * Rot;

    public void RotateY(float deg) => Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (-deg).DegToRad()) * Rot;

    public void RotateZ(float deg) => Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (-deg).DegToRad()) * Rot;

    public void AddEuler(float x, float y, float z) {

        var q = Quaternion.CreateFromYawPitchRoll((-y).DegToRad(), x.DegToRad(), (-z).DegToRad());
        Rot = q * Rot;
    }

    private static Vector3 GetClosestPointLineRay(Vector3 lineStart, Vector3 lineDir, Ray ray) {

        var w0 = ray.Position - lineStart;
        var a = Vector3.Dot(lineDir, lineDir);
        var b = Vector3.Dot(lineDir, ray.Direction);
        var c = Vector3.Dot(ray.Direction, ray.Direction);
        var d = Vector3.Dot(lineDir, w0);
        var e = Vector3.Dot(ray.Direction, w0);
        var denominator = a * c - b * b;

        if (MathF.Abs(denominator) < 1e-6f) return lineStart;

        var s = (b * e - c * d) / denominator;

        return lineStart + lineDir * s;
    }

    private static bool IntersectRayPlane(Ray ray, Vector3 planePos, Vector3 planeNormal, out Vector3 intersection) {

        var denominator = Vector3.Dot(planeNormal, ray.Direction);

        if (MathF.Abs(denominator) > 1e-6f) {

            var t = Vector3.Dot(planePos - ray.Position, planeNormal) / denominator;

            if (t >= 0) {

                intersection = ray.Position + ray.Direction * t;

                return true;
            }
        }

        intersection = Vector3.Zero;

        return false;
    }
}
