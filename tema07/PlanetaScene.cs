using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// TEMA 7, varianta C: o planetă desenată în întregime de fragment shader (planet.frag), pe o sferă.
/// Relieful vine dintr-un zgomot 3D (fbm) calculat din poziția punctului pe sferă:
/// ocean (mai deschis lângă țărm), plajă, câmpie, pădure, munte, zăpadă; calote de gheață cu margine
/// neregulată; nori dintr-un al doilea zgomot, care se mișcă în timp.
///  - G: planetă nouă. "Sămânța" zgomotului (uSeed, un vec3) trece lin spre o valoare nouă (Tween),
///    deci continentele se transformă sub ochii noștri;
///  - Space: ascunde / arată norii, lin (Tween pe uCloudAmount);
///  - săgețile: nivelul mării, mărimea continentelor, calotele, viteza norilor, atmosfera (Param).
/// Bonus: atmosfera, o sferă puțin mai mare, transparentă, albăstruie spre margine (atmosphere.frag).
/// C# calculează doar valori (sămânța, cantitatea de nori) și le trimite ca uniforme.
/// </summary>
public sealed class PlanetScene : Scene
{
    private const string NewPlanet = "planetă nouă";
    private const string ToggleClouds = "nori: arată / ascunde";

    private const float PlanetRadius = 1.5f;
    private const float AtmosphereScale = 1.08f;  
    private const float SeedTime = 2.0f;          
    private const float CloudTime = 0.8f;         
    private const float SeedStep = 6.0f;           

    private static readonly Vector3 LightDir = new Vector3(0.8f, 0.3f, 0.5f).Normalized();

    private Material _planet = null!;
    private Material _atmosphere = null!;
    private SceneObject _planetObject = null!;

    private Tween _seed = null!;      
    private Tween _clouds = null!;    
    private bool _cloudsOn = true;
    private int _planetNumber = 1;
    private readonly Random _random = new(2026);

    public override string Name => "planeta";

    public override IEnumerable<string> ActionNames => new[] { NewPlanet, ToggleClouds };

    public override string Status =>
        $"planeta #{_planetNumber}{(_seed.IsFinished ? "" : " (se transformă)")} | nori: {(_cloudsOn ? "da" : "nu")}";

    protected override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 0.6f, 5.2f);
        CameraTarget = Vector3.Zero;

        input.Bind(NewPlanet, Keys.G);
        input.Bind(ToggleClouds, Keys.Space);

        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 96, 48));

        // Materialul planetei 
        _planet = AddMaterial("planetă", "planet.vert", "planet.frag")
            .Param("uSeaLevel", 0.3f, 0.7f, 0.5f)
            .Param("uNoiseScale", 0.5f, 5.0f, 1.8f)
            .Param("uIceLatitude", 0.5f, 1.0f, 0.82f)
            .Param("uCloudSpeed", 0.0f, 0.5f, 0.08f);

        var firstSeed = new Vector3(12.3f, 4.5f, 7.8f);
        _seed = new Tween(firstSeed, firstSeed, SeedTime, Easing.EaseInOut);
        _seed.Update(SeedTime);   
        _clouds = Tween.Float(1.0f, 1.0f, CloudTime, Easing.EaseInOut);

        _planet.Uniforms.Set("uSeed", firstSeed);
        _planet.Uniforms.Set("uCloudAmount", 1.0f);
        _planet.Uniforms.Set("uLightDir", LightDir);

        //  Materialul atmosferei 
        _atmosphere = AddMaterial("atmosferă", "atmosphere.vert", "atmosphere.frag")
            .Param("uAtmosphereStrength", 0.0f, 3.0f, 1.5f);
        _atmosphere.Uniforms.Set("uAtmosphereColor", new Vector3(0.35f, 0.6f, 1.0f));
        _atmosphere.Uniforms.Set("uLightDir", LightDir);
        _atmosphere.Transparent = true;

        // Obiectele 
        _planetObject = AddObject("planetă", sphere, _planet);
        _planetObject.Transform.Scale = new Vector3(PlanetRadius);
        _planetObject.Transform.Rotation = new Vector3(0.0f, 0.0f, -20.0f);
        _planetObject.Spin = new Vector3(0.0f, 10.0f, 0.0f);

        // Atmosfera
        SceneObject atmosphere = AddObject("atmosferă", sphere, _atmosphere);
        atmosphere.Transform.Scale = new Vector3(PlanetRadius * AtmosphereScale);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);   

        if (input.WasPressed(NewPlanet))
        {
            _planetNumber++;
            _seed.GoTo(NextSeed(), SeedTime);
            ConsolePanel.PrintMessage($"planeta #{_planetNumber}");
        }

        // Space
        if (input.WasPressed(ToggleClouds))
        {
            _cloudsOn = !_cloudsOn;
            _clouds.GoTo(_cloudsOn ? 1.0f : 0.0f, CloudTime);
            ConsolePanel.PrintMessage(_cloudsOn ? "norii apar" : "norii dispar");
        }

        _seed.Update(dt);
        _clouds.Update(dt);
        _planet.Uniforms.Set("uSeed", _seed.Value);
        _planet.Uniforms.Set("uCloudAmount", _clouds.Scalar);
    }

    private Vector3 NextSeed()
    {
        float Offset() => (_random.NextSingle() * 2.0f - 1.0f) * SeedStep;
        return _seed.To + new Vector3(Offset(), Offset(), Offset());
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;

        Vector3 s = _seed.Value;
        string transition = _seed.IsFinished ? "" : $"   tranziție {_seed.Progress:P0}";
        yield return $"Planeta        : #{_planetNumber}   sămânța ({s.X:F2}, {s.Y:F2}, {s.Z:F2}){transition}";
        yield return $"Nori           : {(_cloudsOn ? "porniți" : "opriți")}   uCloudAmount = {_clouds.Scalar:F2}";
        yield return $"Rotația        : {_planetObject.Transform.Rotation.Y % 360.0f:F0}°   (10°/s)";
    }
}