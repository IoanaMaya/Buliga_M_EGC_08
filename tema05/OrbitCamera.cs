using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc
{
    public class OrbitCamera : Camera
    {
        public Vector3 Target { get; set; } = Vector3.Zero;
        public float Distance { get; set; } = 5.0f;
        public float Azimuth { get; set; } = 45.0f;
        public float Elevation { get; set; } = 30.0f;

        public const float MaxElevation = 89.0f;
        public float MouseSensitivity = 0.3f;
        public float KeyboardSensitivity = 90.0f; // grade pe secundă
        public float ZoomSensitivity = 0.5f;
        public float MinDistance = 1.0f;
        public float MaxDistance = 50.0f;

        public override Vector3 Position
        {
            get
            {
                float azRad = MathHelper.DegreesToRadians(Azimuth);
                float elRad = MathHelper.DegreesToRadians(Elevation);

                float x = Distance * MathF.Cos(elRad) * MathF.Cos(azRad);
                float y = Distance * MathF.Sin(elRad);
                float z = Distance * MathF.Cos(elRad) * MathF.Sin(azRad);

                return Target + new Vector3(x, y, z);
            }
        }

        public override Vector3 Front => Vector3.Normalize(Target - Position);

        // Matricea view care se uită mereu spre țintă, cu vectorul UP = (0, 1, 0)
        public override Matrix4 ViewMatrix => Matrix4.LookAt(Position, Target, Vector3.UnitY);

        public override string Name => "Orbită";

        public OrbitCamera()
        {
            FieldOfView = 60.0f;
        }

        public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
        {
            // 1. Rotația din mouse: funcționează doar cu Clic Stânga ținut apăsat
            if (mouse.IsButtonDown(MouseButton.Left))
            {
                Azimuth -= mouseDelta.X * MouseSensitivity;
                Elevation += mouseDelta.Y * MouseSensitivity;
            }

            // 2. Rotația din săgețile de pe tastatură
            if (keyboard.IsKeyDown(Keys.Left)) Azimuth -= KeyboardSensitivity * dt;
            if (keyboard.IsKeyDown(Keys.Right)) Azimuth += KeyboardSensitivity * dt;
            if (keyboard.IsKeyDown(Keys.Up)) Elevation += KeyboardSensitivity * dt;
            if (keyboard.IsKeyDown(Keys.Down)) Elevation -= KeyboardSensitivity * dt;

            // 3. Limitarea elevației să nu dea peste cap camera (max ±89 grade)
            Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);

            // 4. Distanța (zoom-ul) din rotiță, limitată între min și max
            if (mouse.ScrollDelta.Y != 0)
            {
                Distance -= mouse.ScrollDelta.Y * ZoomSensitivity;
                Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
            }
        }

        public override void Reset()
        {
            Target = Vector3.Zero;
            Distance = 5.0f;
            Azimuth = 45.0f;
            Elevation = 30.0f;
        }

        public override IEnumerable<string> Describe()
        {
            Vector3 pos = Position;
            yield return $"Țintă        : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
            yield return $"Distanță     : {Distance,6:F2}";
            yield return $"Azimut / elev: {Azimuth,7:F1}° / {Elevation,6:F1}°";
            yield return $"Poziție      : ({pos.X,6:F2}, {pos.Y,6:F2}, {pos.Z,6:F2})";
        }
    }
}

