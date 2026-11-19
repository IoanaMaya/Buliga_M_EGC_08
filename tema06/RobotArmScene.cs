using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

// A / D(sau ← →) : rotește baza
// W / S: umărul înainte / înapoi, limitat la ±90°
// ↑ / ↓: cotul înainte / înapoi, limitat la ±150°
// Space: deschide / închide clește-ul
// K: salvează poza curentă
// Enter: redă pozele în buclă / oprește redarea
// E: schimbă easing-ul redării(Linear → EaseIn → EaseOut → EaseInOut)
// Backspace: șterge pozele

public sealed class RobotArmScene : Scene
{
    //  Acțiunile scenei 
    private const string BaseLeft = "baza spre stânga";
    private const string BaseRight = "baza spre dreapta";
    private const string ShoulderForward = "umărul înainte";
    private const string ShoulderBack = "umărul înapoi";
    private const string ElbowUp = "cotul înainte";
    private const string ElbowDown = "cotul înapoi";
    private const string Claw = "clește deschis / închis";
    private const string SavePose = "salvează poza (cadru-cheie)";
    private const string Play = "redă pozele / oprește redarea";
    private const string NextEase = "easing-ul redării";
    private const string ClearPoses = "șterge pozele";

    //  Dimensiuni și limite 
    private const float BaseHeight = 0.6f;      
    private const float UpperLength = 1.6f;     
    private const float ForeLength = 1.4f;      

    private const float ShoulderMin = -90.0f, ShoulderMax = 90.0f;   
    private const float ElbowMin = -150.0f, ElbowMax = 150.0f;

    private const float MaxJointSpeed = 70.0f;  
    private const float Responsiveness = 8.0f;  

    private const float ClawOpenGap = 0.30f;    
    private const float ClawEmptyGap = 0.06f;   
    private const float ClawBoxGap = 0.165f;    
    private const float ClawTime = 0.35f;       
    private const float FingerY = 0.28f;        

    private const float GrabDistance = 0.35f;   
    private const float BoxHalf = 0.125f;      
    private const float Gravity = 20.0f;

    //  Obiectele scenei
    private SceneObject _base = null!;
    private SceneObject _shoulder = null!;
    private SceneObject _elbow = null!;
    private SceneObject _fingerLeft = null!;
    private SceneObject _fingerRight = null!;
    private SceneObject _grip = null!;
    private SceneObject _box = null!;

    //  Starea brațului
    private Vector3 _angles;   
    private Vector3 _speeds;    

    private Tween _claw = null!;   
    private bool _clawClosed;
    private bool _wantGrab;        
    private bool _holding;
    private float _boxSpeedY;

    // Pozele (cadre-cheie) 
    private readonly KeyframeTrack _poses = new(1.5f);   
    private int _easeIndex;
    private bool _playing;
    private float _playTime;
    private Tween? _approach;      

    public override string Name => "brațul robotic";

    public override string Status =>
        $"poze: {_poses.Count} | {(_playing ? "redare" : "manual")} | {Easing.All[_easeIndex].Name} | " +
        $"clește {(_clawClosed ? "închis" : "deschis")}{(_holding ? " (ține cutia)" : "")}";

    public override IEnumerable<string> ActionNames => new[]
    {
        BaseLeft, BaseRight, ShoulderForward, ShoulderBack, ElbowUp, ElbowDown,
        Claw, SavePose, Play, NextEase, ClearPoses,
    };

    
    // Construcția scenei
    public override void Load(Input input)
    {
        CameraPosition = new Vector3(5.0f, 4.5f, 7.0f);
        CameraTarget = new Vector3(-0.3f, 1.4f, 0.0f);

        input.Bind(BaseLeft, Keys.A, Keys.Left);
        input.Bind(BaseRight, Keys.D, Keys.Right);
        input.Bind(ShoulderForward, Keys.W);
        input.Bind(ShoulderBack, Keys.S);
        input.Bind(ElbowUp, Keys.Up);
        input.Bind(ElbowDown, Keys.Down);
        input.Bind(Claw, Keys.Space);
        input.Bind(SavePose, Keys.K);
        input.Bind(Play, Keys.Enter, Keys.KeyPadEnter);
        input.Bind(NextEase, Keys.E);
        input.Bind(ClearPoses, Keys.Backspace);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 32, 16));
        Mesh cylinder = AddMesh(Shapes.ShadedCylinder(0.5f, 1.0f, 32));   

        AddObject("podea", grid);
        BuildArm(cube, sphere, cylinder);
        BuildBox(cube);

        var open = new Vector3(ClawOpenGap, 0.0f, 0.0f);
        _claw = new Tween(open, open, ClawTime, Easing.EaseInOut);
        ApplyClaw();

        _angles = new Vector3(0.0f, 20.0f, 40.0f);
        ApplyAngles();
    }

    private void BuildArm(Mesh cube, Mesh sphere, Mesh cylinder)
    {
        var dark = new Color4(0.25f, 0.25f, 0.30f, 1.0f);
        var orange = new Color4(1.0f, 0.55f, 0.15f, 1.0f);
        var lightOrange = new Color4(1.0f, 0.75f, 0.35f, 1.0f);
        var steel = new Color4(0.70f, 0.72f, 0.78f, 1.0f);

        
        _base = AddObject("bază", null);

        SceneObject plate = AddObject("placa bazei", cylinder, _base);
        plate.Transform.Position = new Vector3(0.0f, 0.1f, 0.0f);
        plate.Transform.Scale = new Vector3(1.4f, 0.2f, 1.4f);
        plate.Tint = dark;

        SceneObject column = AddObject("coloana", cylinder, _base);
        column.Transform.Position = new Vector3(0.0f, 0.4f, 0.0f);
        column.Transform.Scale = new Vector3(0.5f, 0.4f, 0.5f);
        column.Tint = orange;

        SceneObject marker = AddObject("indicator", cube, _base);
        marker.Transform.Position = new Vector3(-0.6f, 0.22f, 0.0f);
        marker.Transform.Scale = new Vector3(0.15f, 0.05f, 0.15f);
        marker.Tint = new Color4(1.0f, 0.9f, 0.2f, 1.0f);

        _shoulder = AddObject("umăr", null, _base);
        _shoulder.Transform.Position = new Vector3(0.0f, BaseHeight, 0.0f);

        SceneObject shoulderJoint = AddObject("articulația umărului", sphere, _shoulder);
        shoulderJoint.Transform.Scale = new Vector3(0.22f);
        shoulderJoint.Tint = dark;

        SceneObject upperArm = AddObject("braț", cube, _shoulder);
        upperArm.Transform.Position = new Vector3(0.0f, UpperLength / 2.0f, 0.0f);
        upperArm.Transform.Scale = new Vector3(0.25f, UpperLength, 0.25f);
        upperArm.Tint = orange;

        _elbow = AddObject("cot", null, _shoulder);
        _elbow.Transform.Position = new Vector3(0.0f, UpperLength, 0.0f);

        SceneObject elbowJoint = AddObject("articulația cotului", sphere, _elbow);
        elbowJoint.Transform.Scale = new Vector3(0.18f);
        elbowJoint.Tint = dark;

        SceneObject foreArm = AddObject("antebraț", cube, _elbow);
        foreArm.Transform.Position = new Vector3(0.0f, ForeLength / 2.0f, 0.0f);
        foreArm.Transform.Scale = new Vector3(0.2f, ForeLength, 0.2f);
        foreArm.Tint = lightOrange;

        SceneObject wrist = AddObject("încheietură", null, _elbow);
        wrist.Transform.Position = new Vector3(0.0f, ForeLength, 0.0f);

        SceneObject palm = AddObject("palma", cube, wrist);
        palm.Transform.Position = new Vector3(0.0f, 0.04f, 0.0f);
        palm.Transform.Scale = new Vector3(0.75f, 0.08f, 0.3f);
        palm.Tint = steel;

        _fingerLeft = AddObject("degetul stâng", cube, wrist);
        _fingerLeft.Transform.Scale = new Vector3(0.08f, 0.4f, 0.25f);
        _fingerLeft.Tint = steel;

        _fingerRight = AddObject("degetul drept", cube, wrist);
        _fingerRight.Transform.Scale = new Vector3(0.08f, 0.4f, 0.25f);
        _fingerRight.Tint = steel;

        _grip = AddObject("punctul de prindere", null, wrist);
        _grip.Transform.Position = new Vector3(0.0f, FingerY, 0.0f);
    }

    private void BuildBox(Mesh cube)
    {
        _box = AddObject("cutie", cube);
        _box.Transform.Position = new Vector3(-1.5f, BoxHalf, 1.5f);
        _box.Transform.Scale = new Vector3(2.0f * BoxHalf);
        _box.Tint = new Color4(0.3f, 0.7f, 1.0f, 1.0f);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        HandlePoseActions(input);

        if (_playing)
        {
            UpdatePlayback(dt);
        }
        else
        {
            UpdateJoints(dt, input);
        }
        ApplyAngles();

        UpdateClaw(dt, input);
        UpdateBox(dt);
    }

    //  Articulațiile, de la tastatură 
    private void UpdateJoints(float dt, Input input)
    {
        Vector3 target = new Vector3(
            input.Axis(BaseRight, BaseLeft),
            input.Axis(ShoulderBack, ShoulderForward),
            input.Axis(ElbowDown, ElbowUp)) * MaxJointSpeed;

        float blend = 1.0f - MathF.Exp(-Responsiveness * dt);
        _speeds += (target - _speeds) * blend;

        _angles += _speeds * dt;

        ClampJoint(ref _angles.Y, ref _speeds.Y, ShoulderMin, ShoulderMax);
        ClampJoint(ref _angles.Z, ref _speeds.Z, ElbowMin, ElbowMax);
    }

    private static void ClampJoint(ref float angle, ref float speed, float min, float max)
    {
        if (angle < min || angle > max)
        {
            angle = Math.Clamp(angle, min, max);
            speed = 0.0f;  
        }
    }

    /// <summary>Copiază unghiurile în rotațiile pivoților.</summary>
    private void ApplyAngles()
    {
        _base.Transform.Rotation = new Vector3(0.0f, _angles.X, 0.0f);
        _shoulder.Transform.Rotation = new Vector3(0.0f, 0.0f, _angles.Y);
        _elbow.Transform.Rotation = new Vector3(0.0f, 0.0f, _angles.Z);
    }


    private void HandlePoseActions(Input input)
    {
        if (input.WasPressed(SavePose))
        {
            if (_playing)
            {
                ConsolePanel.PrintMessage("opriți redarea (Enter) înainte de a salva o poză");
            }
            else
            {
                _poses.Add(_angles);
                ConsolePanel.PrintMessage($"poza {_poses.Count} salvată: {Fmt(_angles)}");
            }
        }

        if (input.WasPressed(ClearPoses))
        {
            StopPlayback();
            _poses.Clear();
            ConsolePanel.PrintMessage("pozele au fost șterse");
        }

        if (input.WasPressed(NextEase))
        {
            _easeIndex = (_easeIndex + 1) % Easing.All.Length;
            _poses.Ease = Easing.All[_easeIndex].Function;
            ConsolePanel.PrintMessage($"easing-ul redării: {Easing.All[_easeIndex].Name}");
        }

        if (input.WasPressed(Play))
        {
            if (_playing)
            {
                StopPlayback();
                ConsolePanel.PrintMessage("redarea s-a oprit");
            }
            else if (_poses.Count < 2)
            {
                ConsolePanel.PrintMessage("salvați cel puțin două poze (K) înainte de redare");
            }
            else
            {
                _playing = true;
                _playTime = 0.0f;
                _speeds = Vector3.Zero;
                _approach = new Tween(_angles, _poses.Keys[0], 0.8f, Easing.EaseInOut);
                ConsolePanel.PrintMessage($"redare: {_poses.Count} poze, {Easing.All[_easeIndex].Name}");
            }
        }
    }

    private void UpdatePlayback(float dt)
    {
        if (_approach != null)
        {
            _approach.Update(dt);
            _angles = _approach.Value;
            if (_approach.IsFinished)
            {
                _approach = null;   
            }
            return;
        }

        _playTime += dt;
        _angles = _poses.Sample(_playTime);
    }

    private void StopPlayback()
    {
        _playing = false;
        _approach = null;
        _speeds = Vector3.Zero;
    }

    // Clește-ul și cutia 

    private void UpdateClaw(float dt, Input input)
    {
        if (input.WasPressed(Claw))
        {
            _clawClosed = !_clawClosed;
            if (_clawClosed)
            {
                _wantGrab = !_holding &&
                            Vector3.Distance(_grip.WorldPosition, _box.WorldPosition) < GrabDistance;
                float gap = _wantGrab ? ClawBoxGap : ClawEmptyGap;
                _claw.GoTo(new Vector3(gap, 0.0f, 0.0f), ClawTime);
            }
            else
            {
                _wantGrab = false;
                if (_holding)
                {
                    ReleaseBox();
                }
                _claw.GoTo(new Vector3(ClawOpenGap, 0.0f, 0.0f), ClawTime);
            }
        }

        _claw.Update(dt);
        ApplyClaw();

        if (_wantGrab && _claw.IsFinished)
        {
            GrabBox();
            _wantGrab = false;
        }
    }

    private void ApplyClaw()
    {
        float gap = _claw.Value.X;
        _fingerLeft.Transform.Position = new Vector3(-gap, FingerY, 0.0f);
        _fingerRight.Transform.Position = new Vector3(gap, FingerY, 0.0f);
    }

    private void GrabBox()
    {
        _box.Parent = _grip;
        _box.Transform.Position = Vector3.Zero;
        _box.Transform.Rotation = Vector3.Zero;
        _holding = true;
        _boxSpeedY = 0.0f;
        ConsolePanel.PrintMessage("cutia a fost prinsă");
    }

    private void ReleaseBox()
    {
        Vector3 world = _box.WorldPosition;
        _box.Parent = null;
        _box.Transform.Position = new Vector3(world.X, MathF.Max(world.Y, BoxHalf), world.Z);
        _box.Transform.Rotation = new Vector3(0.0f, _angles.X, 0.0f);
        _holding = false;
        _boxSpeedY = 0.0f;
        ConsolePanel.PrintMessage("cutia a fost eliberată");
    }

    private void UpdateBox(float dt)
    {
        if (_holding) return;

        Vector3 p = _box.Transform.Position;
        if (p.Y > BoxHalf || _boxSpeedY > 0.0f)
        {
            _boxSpeedY -= Gravity * dt;
            p.Y += _boxSpeedY * dt;
            if (p.Y <= BoxHalf)
            {
                p.Y = BoxHalf;
                _boxSpeedY = 0.0f;
            }
            _box.Transform.Position = p;
        }
    }

    // Afișare (tasta I)
    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;

        yield return $"Unghiuri       : bază {_angles.X:F1}°   umăr {_angles.Y:F1}° [{ShoulderMin}, {ShoulderMax}]   " +
                     $"cot {_angles.Z:F1}° [{ElbowMin}, {ElbowMax}]";
        yield return $"Viteze         : {Fmt(_speeds)} °/s";
        yield return $"Clește         : {(_clawClosed ? "închis" : "deschis")}   distanța degetelor {_claw.Value.X:F2}   " +
                     $"ține cutia: {(_holding ? "da" : "nu")}";
        yield return $"Cutie          : {Fmt(_box.WorldPosition)}   distanța până la clește " +
                     $"{Vector3.Distance(_grip.WorldPosition, _box.WorldPosition):F2}";

        string playback = !_playing ? "oprită"
                        : _approach != null ? "spre prima poză"
                        : $"t = {_playTime % _poses.Duration:F2} / {_poses.Duration:F1} s";
        yield return $"Redare         : {playback}   easing: {Easing.All[_easeIndex].Name}";

        for (int i = 0; i < _poses.Count; i++)
        {
            yield return $"  poza {i + 1,-2}      : {Fmt(_poses.Keys[i])}";
        }
    }

    private static string Fmt(Vector3 v) => $"({v.X:F1}, {v.Y:F1}, {v.Z:F1})";
}
