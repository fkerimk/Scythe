using System.Numerics;
using Raylib_cs;

namespace Blocks;

internal class Explode : ScytheScript {
    
    private Rigidbody _rb = null!;

    public override void Start() {
        
        _rb = (Rigidbody)FindComponent("Rigidbody")!;
    }

    public override void Loop(float dt) {
        
        if (!Raylib.IsKeyPressed(KeyboardKey.Space)) return;

        var explosionPosition = new Vector3(0f, -2f, 0f);

        ApplyExplosion(explosionPosition, 4.5f, 10f, 1f, 1, 1);
    }

    private static readonly Random Random = new();

    private void ApplyExplosion(Vector3 explosionPosition, float explosionForce, float explosionRadius, float randomness = 1, float upwardsMultiplier = 1, float angularMultiplier = 1) {
        
        var delta = _rb.Pos - explosionPosition;

        var distSq = delta.LengthSquared();
        var radiusSq = explosionRadius * explosionRadius;

        if (distSq >= radiusSq) return;

        var distance = (float)Math.Sqrt(distSq);
        var power = 1f - distance / explosionRadius;

        var direction = distance > 0.0001f ? delta / distance : RandomUnitVector();

        if (randomness > 0f) {
            
            var randomDir = RandomUnitVector();
            
            direction += randomDir * randomness;
            direction = Vector3.Normalize(direction);
        }

        direction.Y *= upwardsMultiplier;

        direction = direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : new Vector3(0f, 1f, 0f);

        _rb.Velocity += direction * explosionForce * power;

        // Angular velocity
        var horizontalDirection = direction with { Y = 0f };

        if (!(horizontalDirection.LengthSquared() > 0.0001f)) return;
        
        horizontalDirection = Vector3.Normalize(horizontalDirection);

        var rollAxis = Vector3.Cross(new Vector3(0f, 1f, 0f), horizontalDirection);

        if (!(rollAxis.LengthSquared() > 0.0001f)) return;
        
        rollAxis = Vector3.Normalize(rollAxis);

        var randomAngular = RandomUnitVector() * randomness;
        var angularDir = rollAxis + randomAngular;

        //angularDir.Y = 0f; // Y spin istemiyorsan bu kalsın

        if (!(angularDir.LengthSquared() > 0.0001f)) return;
        
        angularDir = Vector3.Normalize(angularDir);
        _rb.AngularVelocity += angularDir * explosionForce * angularMultiplier * power;
    }

    private static Vector3 RandomUnitVector() {
        
        var x = (float)(Random.NextDouble() * 2.0 - 1.0);
        var y = (float)(Random.NextDouble() * 2.0 - 1.0);
        var z = (float)(Random.NextDouble() * 2.0 - 1.0);

        var v = new Vector3(x, y, z);

        return v.LengthSquared() < 0.0001f ? new Vector3(0f, 1f, 0f) : Vector3.Normalize(v);
    }
}