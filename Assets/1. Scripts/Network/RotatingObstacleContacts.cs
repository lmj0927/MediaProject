using System.Collections.Generic;
using UnityEngine;

/// <summary>Host 회전 경로의 겹침 검사와 Y축 회전 기믹의 수평 반동.</summary>
internal sealed class RotatingObstacleContacts
{
    private readonly List<Transport> transports = new();
    private readonly List<(Transport transport, Vector3 point)> riders = new();

    public void CaptureLoadedScenes()
    {
        foreach (var sphere in Object.FindObjectsByType<SphereDriver>(FindObjectsSortMode.None))
        {
            if (transports.Exists(t => t.Sphere == sphere)) continue;
            transports.Add(new Transport(sphere));
        }
    }

    public void Move(Transform target, Vector3 position, Quaternion rotation, Collider[] colliders, float radius,
        Vector3 spinAxis = default, float spinDegrees = 0f, bool carrySupport = false, float deltaTime = 0f)
    {
        Vector3 fromPosition = target.position;
        Quaternion fromRotation = target.rotation;
        float travel = Vector3.Distance(fromPosition, position) +
            (Quaternion.Angle(fromRotation, rotation) + Mathf.Abs(spinDegrees)) * Mathf.Deg2Rad * radius;
        float spacing = float.PositiveInfinity;
        Vector3 angularVelocity = spinAxis == Vector3.up && deltaTime > 0f
            ? rotation * spinAxis * (spinDegrees * Mathf.Deg2Rad / deltaTime)
            : Vector3.zero;
        riders.Clear();
        foreach (var transport in transports)
        {
            transport.RotationKick = Vector3.zero;
            if (transport.Near(position, radius + travel)) {
                spacing = Mathf.Min(spacing, transport.Spacing);
                if (carrySupport && transport.TrySupportPoint(colliders, out var point)) riders.Add((transport, point));
            }
        }

        int steps = float.IsPositiveInfinity(spacing) ? 1 : Mathf.Max(1, Mathf.CeilToInt(travel / spacing));
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Quaternion pose = Quaternion.Slerp(fromRotation, rotation, t);
            if (spinDegrees != 0f) pose *= Quaternion.AngleAxis(spinDegrees * t, spinAxis);
            Vector3 nextPosition = Vector3.Lerp(fromPosition, position, t);
            foreach (var rider in riders) {
                Vector3 before = target.position + target.rotation * (Quaternion.Inverse(fromRotation) * (rider.point - fromPosition));
                Vector3 after = nextPosition + pose * (Quaternion.Inverse(fromRotation) * (rider.point - fromPosition));
                rider.transport.Sphere.OffsetObstacleSupport(after - before);
            }
            target.SetPositionAndRotation(nextPosition, pose);
            Resolve(target.position, radius, colliders, true, angularVelocity);
        }

        foreach (var transport in transports)
            if (transport.RotationKick != Vector3.zero) transport.Sphere.ApplyKnockback(transport.RotationKick);
    }

    public void Resolve(Vector3 position, float radius, Collider[] colliders, bool stepTarget, Vector3 angularVelocity = default)
    {
        foreach (var transport in transports)
            if (transport.Near(position, radius)) transport.Resolve(colliders, stepTarget, position, angularVelocity);
    }

    private sealed class Transport
    {
        public readonly SphereDriver Sphere;
        public readonly float Spacing;
        public Vector3 RotationKick;
        private const float RotationKickScale = 0.2f;
        private const float MaxRotationKick = 1.5f;
        private readonly Rigidbody body;
        private readonly PlatformTilt platform;
        private readonly List<Shape> shapes = new();
        private readonly float reach;
        private readonly SphereCollider sphereCollider;
        private readonly float sphereRadius;
        private Collider[] supports = new Collider[64];

        public Transport(SphereDriver sphere)
        {
            Sphere = sphere;
            body = sphere.GetComponent<Rigidbody>();
            sphereCollider = sphere.GetComponent<SphereCollider>();
            Vector3 scale = sphereCollider.transform.lossyScale;
            sphereRadius = sphereCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            foreach (var p in sphere.transform.root.GetComponentsInChildren<PlatformTilt>())
                if (p.Sphere == sphere.transform) { platform = p; break; }
            Rigidbody deck = platform != null ? platform.GetComponent<Rigidbody>() : null;
            Spacing = float.PositiveInfinity;
            foreach (var c in sphere.transform.root.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled || c.attachedRigidbody == null ||
                    (c.attachedRigidbody != body && c.attachedRigidbody != deck)) continue;
                if (c is MeshCollider mesh && !mesh.convex) continue;
                var shape = new Shape(c);
                shapes.Add(shape);
                Vector3 size = c.bounds.size;
                Spacing = Mathf.Min(Spacing, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f);
                reach = Mathf.Max(reach, Vector3.Distance(c.bounds.center, c.attachedRigidbody.position) +
                    c.bounds.extents.magnitude + Vector3.Distance(c.attachedRigidbody.position, body.position));
            }
        }

        public bool Near(Vector3 position, float radius) => Sphere != null && Sphere.Object != null &&
            Sphere.Object.IsValid && Sphere.Object.HasStateAuthority &&
            (body.position - position).sqrMagnitude <= (radius + reach) * (radius + reach);

        public bool TrySupportPoint(Collider[] obstacles, out Vector3 point)
        {
            point = default;
            if (body.linearVelocity.y > 2f) return false;
            float nearest = sphereRadius + .6f;
            bool found = false;
            var ray = new Ray(body.position, Vector3.down);
            foreach (var obstacle in obstacles) {
                if (Physics.GetIgnoreLayerCollision(sphereCollider.gameObject.layer, obstacle.gameObject.layer) ||
                    Physics.GetIgnoreCollision(sphereCollider, obstacle)) continue;
                if (obstacle.Raycast(ray, out var hit, nearest) && hit.normal.y > .7f) {
                    nearest = hit.distance; point = hit.point; found = true;
                }
            }
            return found;
        }

        public void Resolve(Collider[] obstacles, bool stepTarget, Vector3 pivot, Vector3 angularVelocity)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                foreach (var obstacle in obstacles)
                {
                    if (obstacle == null || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy) continue;
                    foreach (var shape in shapes)
                    {
                        var c = shape.Collider;
                        if (c == null || !c.enabled || !c.gameObject.activeInHierarchy ||
                            Physics.GetIgnoreLayerCollision(c.gameObject.layer, obstacle.gameObject.layer) ||
                            Physics.GetIgnoreCollision(c, obstacle)) continue;
                        shape.Pose(platform, stepTarget, out var position, out var rotation);
                        if (!Physics.ComputePenetration(c, position, rotation, obstacle,
                            obstacle.transform.position, obstacle.transform.rotation, out var direction, out float distance) || distance <= 0f) continue;
                        if (angularVelocity != Vector3.zero) RecordRotationKick(position, direction, pivot, angularVelocity);
                        Sphere.ResolveObstacleContact(direction, distance);
                        ResolveSupports(obstacles);
                        changed = true;
                    }
                }
                if (!changed) break;
            }
        }

        private void RecordRotationKick(Vector3 position, Vector3 direction, Vector3 pivot, Vector3 angularVelocity)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            direction.Normalize();

            float surfaceSpeed = Vector3.Dot(Vector3.Cross(angularVelocity, position - pivot), direction);
            float outwardSpeed = Mathf.Max(0f, Vector3.Dot(body.linearVelocity, direction));
            float kick = Mathf.Min(MaxRotationKick, (surfaceSpeed - outwardSpeed) * RotationKickScale);
            // 여러 콜라이더와 중간 자세가 닿아도 한 기믹당 가장 강한 접촉만 적용한다.
            if (kick > RotationKick.magnitude) RotationKick = direction * kick;
        }

        private void ResolveSupports(Collider[] obstacles)
        {
            Vector3 center = body.position + body.rotation * Vector3.Scale(sphereCollider.center, sphereCollider.transform.lossyScale);
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(center, sphereRadius, supports, ~0, QueryTriggerInteraction.Ignore)) == supports.Length)
                System.Array.Resize(ref supports, supports.Length * 2);
            for (int i = 0; i < count; i++)
            {
                var support = supports[i];
                if (support.transform.IsChildOf(Sphere.transform.root) || System.Array.IndexOf(obstacles, support) >= 0 ||
                    (support.attachedRigidbody != null && !support.attachedRigidbody.isKinematic) ||
                    support.GetComponentInParent<WeightSource>() != null ||
                    Physics.GetIgnoreLayerCollision(sphereCollider.gameObject.layer, support.gameObject.layer) ||
                    Physics.GetIgnoreCollision(sphereCollider, support)) continue;
                if (Physics.ComputePenetration(sphereCollider, body.position, body.rotation, support,
                    support.transform.position, support.transform.rotation, out var direction, out float distance) && distance > 0f)
                    Sphere.ResolveObstacleContact(direction, distance);
            }
        }
    }

    private sealed class Shape
    {
        public readonly Collider Collider;
        private readonly Rigidbody body;
        private readonly Vector3 offset;
        private readonly Quaternion localRotation;

        public Shape(Collider collider)
        {
            Collider = collider;
            body = collider.attachedRigidbody;
            offset = Quaternion.Inverse(body.transform.rotation) * (collider.transform.position - body.transform.position);
            localRotation = Quaternion.Inverse(body.transform.rotation) * collider.transform.rotation;
        }

        public void Pose(PlatformTilt platform, bool stepTarget, out Vector3 position, out Quaternion rotation)
        {
            if (stepTarget && platform != null && body.gameObject == platform.gameObject)
                platform.GetStepTargetPose(out position, out rotation);
            else { position = body.position; rotation = body.rotation; }
            position += rotation * offset;
            rotation *= localRotation;
        }
    }
}
