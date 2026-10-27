using System.Numerics;

namespace Crucible.Tests;

// The C++ math library against System.Numerics (ADR 0024): every operation,
// through the bindings, over seeded random inputs. A swapped field or a
// transposed matrix anywhere on the boundary shows up here.
public class MathTests
{
    private const int Samples = 200;
    private const float Tolerance = 1e-5f;

    private static bool Near(float a, float b) =>
        MathF.Abs(a - b) <= Tolerance * (1 + MathF.Abs(a) + MathF.Abs(b));

    private static bool Near(Vector2 a, Vector2 b) => Near(a.X, b.X) && Near(a.Y, b.Y);

    private static bool Near(Vector3 a, Vector3 b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);

    private static bool Near(Vector4 a, Vector4 b) => Near(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z)) && Near(a.W, b.W);

    private static bool Near(Quaternion a, Quaternion b) =>
        Near(new Vector4(a.X, a.Y, a.Z, a.W), new Vector4(b.X, b.Y, b.Z, b.W));

    private static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column)
            {
                if (!Near(a[row, column], b[row, column]))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // Seeded, so a failure reproduces.
    private sealed class Inputs(int seed)
    {
        private readonly Random _random = new(seed);

        public float Value(float low = -1, float high = 1) => low + (float)_random.NextDouble() * (high - low);

        public Vector3 Vector() => new(Value(), Value(), Value());

        public Vector4 Vector4() => new(Value(), Value(), Value(), Value());

        public Quaternion Rotation() => Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector()), Value(-3, 3));

        public Matrix4x4 Matrix()
        {
            Matrix4x4 m = default;
            for (int i = 0; i < 16; ++i)
            {
                m[i / 4, i % 4] = Value(-2, 2);
            }
            return m;
        }

        // Scale, rotation, translation: always invertible and well conditioned.
        public Matrix4x4 Transform() =>
            Matrix4x4.CreateScale(Value(0.5f, 2), Value(0.5f, 2), Value(0.5f, 2)) *
            Matrix4x4.CreateFromQuaternion(Rotation()) * Matrix4x4.CreateTranslation(Vector() * 10);
    }

    // Runs `check` on Samples inputs; returns the first failing sample's
    // number, or null if all match.
    private static int? FirstMismatch(int seed, Func<Inputs, bool> check)
    {
        Inputs inputs = new(seed);
        for (int i = 0; i < Samples; ++i)
        {
            if (!check(inputs))
            {
                return i;
            }
        }
        return null;
    }

    [Test]
    public async Task VectorOperationsMatch()
    {
        await Assert.That(FirstMismatch(1, x =>
        {
            Vector3 a = x.Vector(), b = x.Vector();
            Vector4 c = x.Vector4(), d = x.Vector4();
            float t = x.Value(0, 1);
            Vector2 e = new(x.Value(), x.Value());
            return Near(Api.MathProbeNormalize(a), Vector3.Normalize(a)) &&
                   Near(Api.MathProbeCross(a, b), Vector3.Cross(a, b)) &&
                   Near(Api.MathProbeDot(c, d), Vector4.Dot(c, d)) &&
                   Near(Api.MathProbeLength(e), e.Length()) &&
                   Near(Api.MathProbeLerp(a, b, t), Vector3.Lerp(a, b, t));
        })).IsNull();
    }

    [Test]
    public async Task QuaternionOperationsMatch()
    {
        await Assert.That(FirstMismatch(2, x =>
        {
            Vector3 axis = Vector3.Normalize(x.Vector());
            float angle = x.Value(-3, 3);
            float yaw = x.Value(-3, 3), pitch = x.Value(-3, 3), roll = x.Value(-3, 3);
            Quaternion a = x.Rotation(), b = x.Rotation();
            Vector3 v = x.Vector();
            float t = x.Value(0, 1);
            return Near(Api.MathProbeFromAxisAngle(axis, angle), Quaternion.CreateFromAxisAngle(axis, angle)) &&
                   Near(Api.MathProbeFromYawPitchRoll(yaw, pitch, roll), Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)) &&
                   Near(Api.MathProbeMultiply(a, b), a * b) &&
                   Near(Api.MathProbeConcatenate(a, b), Quaternion.Concatenate(a, b)) &&
                   Near(Api.MathProbeInverse(a), Quaternion.Inverse(a)) &&
                   Near(Api.MathProbeSlerp(a, b, t), Quaternion.Slerp(a, b, t)) &&
                   Near(Api.MathProbeRotate(v, a), Vector3.Transform(v, a));
        })).IsNull();
    }

    [Test]
    public async Task MatrixBuildersMatch()
    {
        await Assert.That(FirstMismatch(3, x =>
        {
            float angle = x.Value(-3, 3);
            Quaternion q = x.Rotation();
            Vector3 v = x.Vector();
            return Near(Api.MathProbeRotationX(angle), Matrix4x4.CreateRotationX(angle)) &&
                   Near(Api.MathProbeRotationY(angle), Matrix4x4.CreateRotationY(angle)) &&
                   Near(Api.MathProbeRotationZ(angle), Matrix4x4.CreateRotationZ(angle)) &&
                   Near(Api.MathProbeRotation(q), Matrix4x4.CreateFromQuaternion(q)) &&
                   Near(Api.MathProbeTranslation(v), Matrix4x4.CreateTranslation(v)) &&
                   Near(Api.MathProbeScale(v), Matrix4x4.CreateScale(v));
        })).IsNull();
    }

    [Test]
    public async Task MatrixArithmeticMatches()
    {
        await Assert.That(FirstMismatch(4, x =>
        {
            Matrix4x4 a = x.Matrix(), b = x.Matrix();
            Matrix4x4 transform = x.Transform();
            MathProbeInverse inverse = Api.MathProbeInvert(transform);
            bool invertible = Matrix4x4.Invert(transform, out Matrix4x4 expected);
            return Near(Api.MathProbeMatrixMultiply(a, b), a * b) &&
                   Near(Api.MathProbeTranspose(a), Matrix4x4.Transpose(a)) &&
                   Near(Api.MathProbeDeterminant(a), a.GetDeterminant()) &&
                   inverse.Invertible == invertible && Near(inverse.Matrix, expected);
        })).IsNull();

        MathProbeInverse singular = Api.MathProbeInvert(Matrix4x4.CreateScale(1, 0, 1));
        await Assert.That(singular.Invertible).IsFalse();
        await Assert.That(float.IsNaN(singular.Matrix.M11)).IsTrue();
    }

    [Test]
    public async Task ProjectionsAndViewsMatchTheLeftHandedOnes()
    {
        await Assert.That(FirstMismatch(5, x =>
        {
            float fov = x.Value(0.3f, 2.5f), aspect = x.Value(0.5f, 3), near = x.Value(0.01f, 1), far = x.Value(10, 1000);
            float width = x.Value(1, 100), height = x.Value(1, 100);
            Vector3 eye = x.Vector() * 10, target = x.Vector() * 10;
            return Near(Api.MathProbePerspective(fov, aspect, near, far),
                       Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(fov, aspect, near, far)) &&
                   Near(Api.MathProbeOrthographic(width, height, near, far),
                       Matrix4x4.CreateOrthographicLeftHanded(width, height, near, far)) &&
                   Near(Api.MathProbeLookAt(eye, target, Vector3.UnitY),
                       Matrix4x4.CreateLookAtLeftHanded(eye, target, Vector3.UnitY));
        })).IsNull();
    }

    [Test]
    public async Task TransformsMatch()
    {
        await Assert.That(FirstMismatch(6, x =>
        {
            Matrix4x4 m = x.Matrix();
            Vector3 p = x.Vector();
            Vector4 v = x.Vector4();
            return Near(Api.MathProbeTransformPoint(p, m), Vector3.Transform(p, m)) &&
                   Near(Api.MathProbeTransformDirection(p, m), Vector3.TransformNormal(p, m)) &&
                   Near(Api.MathProbeTransform(v, m), Vector4.Transform(v, m));
        })).IsNull();
    }

    [Test]
    public async Task FixedSizeArraysCrossInPlace()
    {
        ProbeArray probe = Api.MakeProbeArray(1, 2, 3, 4);

        await Assert.That(probe.Values[0] + probe.Values[1] * 10 + probe.Values[2] * 100).IsEqualTo(321);
        await Assert.That(probe.Tail).IsEqualTo((byte)4);
        probe.Values[1] = 5;
        await Assert.That(Api.WeighProbeArray(probe)).IsEqualTo(4351);
    }
}
