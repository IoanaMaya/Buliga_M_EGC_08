using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Scena laboratorului. Are patru zone, câte una pentru fiecare problemă:
///  - în spate, la stânga: patru cuburi care fac același drum, fiecare cu alt easing (P2);
///  - în spate, la dreapta: o bilă care urmează un traseu dat prin cadre-cheie (P3);
///  - în față: jucătorul, condus de la tastatură (P4);
///  - sus, în fundal: un mini-sistem solar, construit ierarhic (P5*).
/// Toate animațiile primesc timpul de JOC (dt), deci pauza și scara timpului (P1) le afectează pe toate.
/// </summary>
public sealed class LabScene : Scene
{
    // Acțiunile proprii scenei.
    private const string Restart = "repornește animațiile";
    private const string ToggleEase = "easing pe traseul bilei";

    // P2: cuburile cu easing.
    private const float LaneStartX = -6.0f, LaneEndX = 2.0f, LaneDuration = 2.0f;
    private static readonly Color4[] LaneColors =
    {
        new(1.0f, 0.3f, 0.3f, 1.0f),    // Linear: roșu
        new(1.0f, 0.85f, 0.25f, 1.0f),  // EaseIn: galben
        new(0.35f, 0.9f, 0.4f, 1.0f),   // EaseOut: verde
        new(0.35f, 0.55f, 1.0f, 1.0f),  // EaseInOut: albastru
    };
    private readonly List<(SceneObject Cube, Tween Tween, string Name)> _lanes = new();

    // P3: bila pe traseu.
    private readonly KeyframeTrack _path = new(1.25f);   // o cheie la fiecare 1,25 s
    private SceneObject _ball = null!;
    private float _pathTime;

    // P4: jucătorul.
    private PlayerController _player = null!;

    // TEMA: mașina și mingea care se rotește în jurul ei.
    private const string ToggleCarMove = "mașina merge / stă";
    private const string OrbitSlower = "mingea mai încet";
    private const string OrbitFaster = "mingea mai repede";
    private SceneObject _car = null!;
    private SceneObject _orbit = null!;
    private SceneObject _orbitBall = null!;
    private readonly List<SceneObject> _wheels = new();
    private float _orbitSpeed = 90.0f;   // grade pe secundă (un tur în 4 s)
    private bool _carMoving = true;
    private Tween _carTween = null!;               // mersul față-spate al mașinii
    private const float CarWheelRadius = 0.4f * 0.6f;   // raza roții în lume (0.4 × scalarea mașinii)
    private float _hue;

    public override string Name => "laborator";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left, Actions.Right, Actions.Forward, Actions.Back, Actions.Jump, Restart, ToggleEase,
        ToggleCarMove, OrbitSlower, OrbitFaster,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 7.0f, 10.0f);
        CameraTarget = new Vector3(0.5f, 0.8f, -3.0f);

        // Tastele scenei: o acțiune poate avea mai multe taste și chiar un buton al mouse-ului.
        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(Actions.Forward, Keys.W, Keys.Up);
        input.Bind(Actions.Back, Keys.S, Keys.Down);
        input.Bind(Actions.Jump, Keys.Space);
        input.Bind(Restart, Keys.R);
        input.Bind(Restart, MouseButton.Left);
        input.Bind(ToggleEase, Keys.E);
        input.Bind(ToggleCarMove, Keys.M);
        input.Bind(OrbitSlower, Keys.Z);
        input.Bind(OrbitFaster, Keys.X);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 32, 16));
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));
        Mesh cylinder = AddMesh(Shapes.ShadedCylinder(0.5f, 1.0f, 24));   // pentru roțile mașinii

        AddObject("podea", grid);

        BuildEasingLanes(cube);
        BuildPath(cube, sphere);
        BuildPlayer(cube, disk);
        BuildSolarSystem(sphere);
        BuildCar(cube, cylinder, sphere);
    }

    // --- P2: același drum, patru funcții de easing ---------------------------------------------

    private void BuildEasingLanes(Mesh cube)
    {
        for (int k = 0; k < Easing.All.Length; k++)
        {
            (string name, EasingFunction ease) = Easing.All[k];
            float z = -7.0f + k * 1.3f;

            // Plăcuțe gri la capetele drumului.
            foreach (float x in new[] { LaneStartX, LaneEndX })
            {
                SceneObject tile = AddObject($"capăt {name}", cube);
                tile.Transform.Position = new Vector3(x, 0.01f, z);
                tile.Transform.Scale = new Vector3(0.8f, 0.02f, 0.8f);
                tile.Tint = new Color4(0.55f, 0.55f, 0.6f, 1.0f);
            }

            SceneObject box = AddObject($"cub {name}", cube);
            box.Transform.Scale = new Vector3(0.6f);
            box.Tint = LaneColors[k];

            var tween = new Tween(new Vector3(LaneStartX, 0.3f, z), new Vector3(LaneEndX, 0.3f, z),
                                  LaneDuration, ease, pingPong: true);
            box.Transform.Position = tween.From;
            _lanes.Add((box, tween, name));
        }
    }

    // --- P3: traseul bilei, dat prin cadre-cheie ----------------------------------------------

    private void BuildPath(Mesh cube, Mesh sphere)
    {
        // Patru chei, la câte 1,25 s; după ultima, bila revine singură la prima (un tur = 5 s).
        _path.Add(new Vector3(4.5f, 0.4f, -7.0f))
             .Add(new Vector3(8.0f, 0.4f, -7.0f))
             .Add(new Vector3(8.0f, 3.0f, -3.0f))
             .Add(new Vector3(4.5f, 0.4f, -3.0f));

        // Un marcaj mic în locul fiecărei chei.
        for (int i = 0; i < _path.Count; i++)
        {
            SceneObject marker = AddObject($"cheia {i}", cube);
            marker.Transform.Position = _path.Keys[i];
            marker.Transform.Scale = new Vector3(0.15f);
            marker.Tint = new Color4(1.0f, 0.9f, 0.3f, 1.0f);
        }

        _ball = AddObject("bilă", sphere);
        _ball.Transform.Scale = new Vector3(0.4f);
        _ball.Tint = new Color4(1.0f, 0.55f, 0.2f, 1.0f);
        _ball.Transform.Position = _path.Sample(0.0f);
    }

    // --- P4: jucătorul ---------------------------------------------------------------------------

    private void BuildPlayer(Mesh cube, Mesh disk)
    {
        SceneObject shadow = AddObject("umbra jucătorului", disk);
        shadow.Tint = new Color4(0.05f, 0.05f, 0.08f, 1.0f);

        SceneObject body = AddObject("jucător", cube);
        body.Transform.Position = new Vector3(0.0f, 0.5f, 2.0f);

        // Zona jucătorului se oprește la x = 3, ca să nu intre peste mașină (care stă în dreapta).
        _player = new PlayerController(body, shadow, new Vector2(-8.0f, 0.0f), new Vector2(3.0f, 5.0f));
    }

    // --- P5*: sistemul solar, ierarhic -------------------------------------------------------

    private void BuildSolarSystem(Mesh sphere)
    {
        // Pivoții (obiecte fără formă) poartă rotațiile; formele sunt copiii lor.
        SceneObject system = AddObject("sistem", null);
        system.Transform.Position = new Vector3(-2.0f, 4.0f, -12.0f);

        SceneObject sun = AddObject("soare", sphere, system);
        sun.Tint = new Color4(1.0f, 0.85f, 0.3f, 1.0f);
        sun.Spin = new Vector3(0.0f, 10.0f, 0.0f);

        SceneObject planetOrbit = AddObject("orbita planetei", null, system);
        planetOrbit.Spin = new Vector3(0.0f, 40.0f, 0.0f);          // planeta face un tur în 9 s

        SceneObject planet = AddObject("planetă", sphere, planetOrbit);
        planet.Transform.Position = new Vector3(3.2f, 0.0f, 0.0f);
        planet.Transform.Scale = new Vector3(0.45f);
        planet.Tint = new Color4(0.3f, 0.6f, 1.0f, 1.0f);
        planet.Spin = new Vector3(0.0f, 120.0f, 0.0f);

        // Luna se rotește în jurul planetei: pivotul ei stă în centrul planetei, dar NU este copilul
        // planetei (altfel ar moșteni și rotația planetei în jurul axei proprii).
        SceneObject moonOrbit = AddObject("orbita lunii", null, planetOrbit);
        moonOrbit.Transform.Position = planet.Transform.Position;
        moonOrbit.Spin = new Vector3(0.0f, 150.0f, 0.0f);

        SceneObject moon = AddObject("lună", sphere, moonOrbit);
        moon.Transform.Position = new Vector3(0.9f, 0.0f, 0.0f);
        moon.Transform.Scale = new Vector3(0.15f);
        moon.Tint = new Color4(0.8f, 0.8f, 0.85f, 1.0f);
    }

    // --- TEMA: mașina, cu o minge care se rotește în jurul ei ---------------------------------
    //
    //   mașină (pivot)
    //    ├── caroserie, cabină, faruri, stopuri
    //    ├── roată (pivot) ── anvelopă (cilindru culcat)   × 4
    //    └── orbită (pivot, înclinată, se rotește cu Spin)
    //         └── minge
    //
    // Mingea este copilul orbitei, orbita este copilul mașinii: mingea rămâne în jurul mașinii
    // oriunde s-ar afla aceasta (tasta M rotește mașina).

    private void BuildCar(Mesh cube, Mesh cylinder, Mesh sphere)
    {
        // Pivotul mașinii: pe podea, în dreapta-față a scenei. Scalarea UNIFORMĂ (0.6) micșorează
        // toată mașina, inclusiv orbita mingii, fără să deformeze piesele.
        _car = AddObject("mașină", null);
        _car.Transform.Scale = new Vector3(0.6f);

        // Mersul față-spate: un tween dus-întors pe axa X (fața mașinii e spre +X), între x = 5.5
        // și x = 8, în 2,5 s, cu EaseInOut: mașina pornește lin, accelerează și frânează la capete.
        _carTween = new Tween(new Vector3(5.5f, 0.0f, 2.0f), new Vector3(8.0f, 0.0f, 2.0f),
                              2.5f, Easing.EaseInOut, pingPong: true);
        _car.Transform.Position = _carTween.From;

        // Caroseria: o cutie lungă pe axa X (fața mașinii este spre +X).
        SceneObject carBody = AddObject("caroserie", cube, _car);
        carBody.Transform.Position = new Vector3(0.0f, 0.8f, 0.0f);
        carBody.Transform.Scale = new Vector3(3.2f, 0.6f, 1.6f);
        carBody.Tint = new Color4(0.85f, 0.15f, 0.15f, 1.0f);

        // Cabina: deasupra caroseriei, puțin spre spate.
        SceneObject cabin = AddObject("cabină", cube, _car);
        cabin.Transform.Position = new Vector3(-0.25f, 1.45f, 0.0f);
        cabin.Transform.Scale = new Vector3(1.7f, 0.7f, 1.4f);
        cabin.Tint = new Color4(0.6f, 0.8f, 0.95f, 1.0f);

        // Farurile (față, +X) și stopurile (spate, -X).
        foreach (float z in new[] { -0.5f, 0.5f })
        {
            SceneObject headlight = AddObject("far", cube, _car);
            headlight.Transform.Position = new Vector3(1.61f, 0.85f, z);
            headlight.Transform.Scale = new Vector3(0.05f, 0.18f, 0.35f);
            headlight.Tint = new Color4(1.0f, 0.95f, 0.5f, 1.0f);

            SceneObject taillight = AddObject("stop", cube, _car);
            taillight.Transform.Position = new Vector3(-1.61f, 0.85f, z);
            taillight.Transform.Scale = new Vector3(0.05f, 0.15f, 0.3f);
            taillight.Tint = new Color4(1.0f, 0.1f, 0.1f, 1.0f);
        }

        // Roțile: cilindrul are axa pe Y; îl culcăm (Rotation.X = 90°) ca axa să fie pe Z.
        // Pivotul roții stă în centrul ei, ca roata să se poată învârti în jurul axei (Spin pe Z).
        foreach (float x in new[] { -1.05f, 1.05f })
        {
            foreach (float z in new[] { -0.8f, 0.8f })
            {
                SceneObject wheel = AddObject("roată", null, _car);
                wheel.Transform.Position = new Vector3(x, 0.4f, z);
                _wheels.Add(wheel);

                SceneObject tire = AddObject("anvelopă", cylinder, wheel);
                tire.Transform.Scale = new Vector3(0.8f, 0.3f, 0.8f);   // rază 0.4, lățime 0.3
                tire.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f);
                tire.Tint = new Color4(0.25f, 0.25f, 0.28f, 1.0f);
            }
        }

        // Orbita: pivot în centrul mașinii, ceva mai sus, înclinat 15° (mingea urcă și coboară).
        _orbit = AddObject("orbita mingii", null, _car);
        _orbit.Transform.Position = new Vector3(0.0f, 1.0f, 0.0f);
        _orbit.Transform.Rotation = new Vector3(0.0f, 0.0f, 15.0f);
        _orbit.Spin = new Vector3(0.0f, _orbitSpeed, 0.0f);

        // Mingea: la distanța 3 de pivot (în unitățile mașinii), se rotește și în jurul axei proprii.
        _orbitBall = AddObject("minge", sphere, _orbit);
        _orbitBall.Transform.Position = new Vector3(3.0f, 0.0f, 0.0f);
        _orbitBall.Transform.Scale = new Vector3(0.35f);
        _orbitBall.Spin = new Vector3(0.0f, 0.0f, -200.0f);
    }

    private void UpdateCar(float dt, Input input)
    {
        if (input.WasPressed(ToggleCarMove))
        {
            _carMoving = !_carMoving;
            ConsolePanel.PrintMessage(_carMoving ? "mașina merge" : "mașina stă");
        }
        if (input.WasPressed(OrbitSlower)) _orbitSpeed = MathF.Max(_orbitSpeed - 30.0f, -360.0f);
        if (input.WasPressed(OrbitFaster)) _orbitSpeed = MathF.Min(_orbitSpeed + 30.0f, 360.0f);
        _orbit.Spin = new Vector3(0.0f, _orbitSpeed, 0.0f);

        // Mersul: tween-ul avansează (doar dacă mașina merge), valoarea lui devine poziția mașinii.
        // Orbita și mingea sunt copiii mașinii, deci o urmează singure.
        float oldX = _car.Transform.Position.X;
        if (_carMoving)
        {
            _carTween.Update(dt);
        }
        _car.Transform.Position = _carTween.Value;
        float dx = _car.Transform.Position.X - oldX;   // cât a mers mașina în acest cadru

        // Roțile se rostogolesc fără să alunece: unghiul (radiani) = distanța / rază.
        // Spre +X roata se rotește în sens orar privită din +Z, adică unghi NEGATIV pe Z.
        // Când mașina merge înapoi, dx < 0 și roțile se învârt singure invers.
        float wheelDegrees = MathHelper.RadiansToDegrees(dx / CarWheelRadius);
        foreach (SceneObject wheel in _wheels)
        {
            wheel.Transform.Rotation.Z -= wheelDegrees;
        }

        // Culoarea mingii parcurge cercul culorilor (un tur în 5 s de joc).
        _hue += dt / 5.0f;
        _orbitBall.Tint = ColorUtil.FromHsv(_hue, 0.8f, 1.0f);
    }

    // --- Actualizarea, la fiecare cadru --------------------------------------------------------

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);   // Spin (sistemul solar)

        if (input.WasPressed(Restart))
        {
            foreach (var lane in _lanes) lane.Tween.Restart();
            _pathTime = 0.0f;
            _player.Reset();
            _carTween.Restart();
            ConsolePanel.PrintMessage("animațiile au repornit");
        }
        if (input.WasPressed(ToggleEase))
        {
            bool linear = _path.Ease == Easing.Linear;
            _path.Ease = linear ? Easing.EaseInOut : Easing.Linear;
            ConsolePanel.PrintMessage($"traseul bilei: {(linear ? "EaseInOut (oprire la fiecare cheie)" : "Linear")}");
        }

        // P2: fiecare tween avansează cu timpul de joc; valoarea lui devine poziția cubului.
        foreach (var lane in _lanes)
        {
            lane.Tween.Update(dt);
            lane.Cube.Transform.Position = lane.Tween.Value;
        }

        // P3: pista nu are timp propriu; scena ține ceasul pistei și îi cere valoarea.
        _pathTime += dt;
        _ball.Transform.Position = _path.Sample(_pathTime);

        // P4: jucătorul citește acțiunile și se mișcă.
        _player.Update(dt, input);

        // TEMA: mașina și mingea de pe orbită.
        UpdateCar(dt, input);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        var first = _lanes[0];
        yield return $"Easing (P2)    : progres liniar {first.Tween.Progress:F2}; " +
                     string.Join("  ", _lanes.Select(l => $"{l.Name} x={l.Cube.Transform.Position.X:F2}"));
        yield return $"Traseu (P3)    : t = {_pathTime % _path.Duration:F2} / {_path.Duration:F1} s   " +
                     $"easing: {(_path.Ease == Easing.Linear ? "Linear" : "EaseInOut")}   bilă: {Fmt(_ball.Transform.Position)}";
        yield return $"Jucător (P4)   : {Fmt(_player.Position)}   {_player.State}   " +
                     $"încărcare {_player.Charge:P0}   v_y = {_player.VerticalSpeed:F2}";
        yield return $"Mașina (TEMA)  : orbită {_orbitSpeed:F0}°/s   mașina merge: {(_carMoving ? "da" : "nu")}   x = {_car.Transform.Position.X:F2}   " +
                     $"minge: {Fmt(_orbitBall.WorldPosition)}";
    }

    private static string Fmt(Vector3 v) => $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
}
