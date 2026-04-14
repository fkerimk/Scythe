using Raylib_cs;

internal static class Picking {

    public static Obj? DragSource;
    public static Obj? DragTarget;
    public static bool IsDragging;
    private static bool _wasGizmoInteracting;

    public static void Update() {

        if (Core.ActiveCamera == null || Core.ActiveLevel == null) return;

        if (!Editor.EditorRender.IsHovered && !IsDragging) {
            DragSource = null;
            DragTarget = null;

            return;
        }

        // Start Drag
        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {

            // If we are clicking on gizmo, flag it and don't start a drag/pick
            if (LevelBrowser.SelectedObject?.Transform.IsHovered == true) {

                _wasGizmoInteracting = true;

                return;
            }

            _wasGizmoInteracting = false;
            DragSource = GetObjectAtMouse();
            IsDragging = false;
        }

        // Cancel Drag
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) {

            DragSource = null;
            DragTarget = null;
            IsDragging = false;

            return;
        }

        // Handle Dragging
        if (DragSource != null && Raylib.IsMouseButtonDown(MouseButton.Left)) {

            if (Raylib.GetMouseDelta().Length() > 2.0f) IsDragging = true;

            if (IsDragging) {

                DragTarget = GetObjectAtMouse();
                if (DragTarget != null && (DragTarget == DragSource || IsChildOf(DragTarget, DragSource))) DragTarget = null;
            }
        }

        // End Drag / Parent
        if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {

            // If we were just using gizmos, don't do anything else (prevents deselection)
            if (_wasGizmoInteracting) {

                _wasGizmoInteracting = false;

                return;
            }

            if (DragSource != null) {

                if (IsDragging) {

                    if (DragTarget != null) DragSource.RecordedSetParent(DragTarget);

                } else {
                    // Just a click, select it
                    var multi = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                    LevelBrowser.SelectObject(DragSource, multi);
                }
            } else {
                // Clicked air
                var multi = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                LevelBrowser.SelectObject(null, multi);
            }

            DragSource = null;
            DragTarget = null;
            IsDragging = false;
        }
    }

    public static void Render2D() {

        if (!IsDragging || DragSource == null) return;

        var mousePos = Editor.EditorRender.RelativeMouse;

        var text = DragTarget != null ? $"Set parent to: {DragTarget.Name}" : $"Dragging: {DragSource.Name}";

        var color = DragTarget != null ? Color.Green : Color.Yellow;

        Raylib.DrawTextEx(Fonts.RlMontserratRegular, text, new System.Numerics.Vector2(mousePos.X + 22, mousePos.Y - 18), 20, 1, Color.Black);
        Raylib.DrawTextEx(Fonts.RlMontserratRegular, text, new System.Numerics.Vector2(mousePos.X + 20, mousePos.Y - 20), 20, 1, color);
    }

    private static bool IsChildOf(Obj child, Obj potentialParent) {

        var curr = child.Parent;

        while (curr != null) {

            if (curr == potentialParent) return true;

            curr = curr.Parent;
        }

        return false;
    }

    private static Obj? GetObjectAtMouse() {

        if (Core.ActiveCamera == null || Core.ActiveLevel == null) return null;

        var ray = Raylib.GetScreenToWorldRay(EditorRender.RelativeMouse3D, Core.ActiveCamera.Raylib);
        Obj? closestObj = null;
        var minDistance = float.MaxValue;

        CheckObj(Core.ActiveLevel.Root);

        return closestObj;

        void CheckObj(Obj obj) {

            foreach (var component in obj.Components.Values) {

                if (component is not Model { IsLoaded: true } model) continue;
                if (model.AssetRef == null || !model.AssetRef.IsLoaded) continue;

                // Calculate correct world matrix with Import Scale
                var worldMatrix = obj.WorldMatrix;

                if (model.AssetRef.Settings.ImportScale != 1.0f) {
                    var s = model.AssetRef.Settings.ImportScale;

                    // Scale basis vectors only
                    worldMatrix.M11 *= s;
                    worldMatrix.M12 *= s;
                    worldMatrix.M13 *= s;
                    worldMatrix.M21 *= s;
                    worldMatrix.M22 *= s;
                    worldMatrix.M23 *= s;
                    worldMatrix.M31 *= s;
                    worldMatrix.M32 *= s;
                    worldMatrix.M33 *= s;
                }

                foreach (var mesh in model.Meshes) {

                    var collision = Raylib.GetRayCollisionMesh(ray, mesh.RlMesh, worldMatrix);

                    if (!collision.Hit || !(collision.Distance < minDistance)) continue;

                    minDistance = collision.Distance;
                    closestObj = obj;
                }
            }

            foreach (var child in obj.Children.Values) CheckObj(child);
        }
    }
}